using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Storage;
using Domain.AuditLogs;
using Domain.Statements;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Web.Api.Features.Statements.Download;

namespace Web.Api.Features.Statements.DownloadInApp;

internal sealed class DownloadInAppQueryHandler(
    IApplicationDbContext context,
    IFileStorageService fileStorage,
    IUserContext userContext) : IQueryHandler<DownloadInAppQuery, StatementFileResponse>
{
    public async Task<Result<StatementFileResponse>> Handle(
        DownloadInAppQuery query,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;
        bool isAdmin = userContext.IsAdmin;

        // Read-only: the statement is only inspected for ownership/active status, never mutated
        // (the audit-log Add below is tracked independently), so skip change-tracking overhead.
        Statement? statement = await context.Statements
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == query.StatementId, cancellationToken);

        if (statement is null || !statement.IsActive)
        {
            return Result.Failure<StatementFileResponse>(StatementErrors.NotFound(query.StatementId));
        }

        // Ownership: a customer can only download their own statements; an admin can download any.
        if (!isAdmin && statement.CustomerId != userId)
        {
            return Result.Failure<StatementFileResponse>(StatementErrors.AccessDenied);
        }

        // Every access is audited, exactly like the token-based path (no token id here).
        context.DownloadAuditLogs.Add(DownloadAuditLog.Create(
            statement.Id,
            userId,
            AuditAction.StatementDownloaded,
            downloadTokenId: null,
            query.IpAddress,
            query.UserAgent));

        await context.SaveChangesAsync(cancellationToken);

        Uri? presignedUri = await fileStorage.GeneratePresignedDownloadUriAsync(
            statement.StoragePath,
            TimeSpan.FromMinutes(5),
            cancellationToken);

        if (presignedUri is not null)
        {
            return Result.Success(new StatementFileResponse(presignedUri));
        }

        Stream fileStream = await fileStorage.RetrieveAsync(statement.StoragePath, cancellationToken);

        return Result.Success(new StatementFileResponse(fileStream, statement.ContentType, statement.OriginalFileName));
    }
}
