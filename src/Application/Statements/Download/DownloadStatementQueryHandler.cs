using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Storage;
using Domain.AuditLogs;
using Domain.DownloadTokens;
using Domain.Statements;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Statements.Download;

internal sealed class DownloadStatementQueryHandler(
    IApplicationDbContext context,
    IDownloadTokenService downloadTokenService,
    IFileStorageService fileStorage) : IQueryHandler<DownloadStatementQuery, StatementFileResponse>
{
    public async Task<Result<StatementFileResponse>> Handle(
        DownloadStatementQuery query,
        CancellationToken cancellationToken)
    {
        DownloadTokenClaims? claims = downloadTokenService.ValidateToken(query.Token);

        if (claims is null)
        {
            return Result.Failure<StatementFileResponse>(DownloadTokenErrors.TokenInvalid);
        }

        DownloadToken? downloadToken = await context.DownloadTokens
            .SingleOrDefaultAsync(t => t.Id == claims.TokenId, cancellationToken);

        if (downloadToken is null)
        {
            return Result.Failure<StatementFileResponse>(DownloadTokenErrors.TokenInvalid);
        }

        if (downloadToken.IsExpired)
        {
            return Result.Failure<StatementFileResponse>(DownloadTokenErrors.TokenExpired);
        }

        if (downloadToken.IsSingleUse && downloadToken.IsUsed)
        {
            return Result.Failure<StatementFileResponse>(DownloadTokenErrors.TokenAlreadyUsed);
        }

        Statement? statement = await context.Statements
            .SingleOrDefaultAsync(s => s.Id == claims.StatementId, cancellationToken);

        if (statement is null || !statement.IsActive)
        {
            return Result.Failure<StatementFileResponse>(StatementErrors.NotFound(claims.StatementId));
        }

        if (downloadToken.IsSingleUse)
        {
            Result markResult = downloadToken.MarkAsUsed();
            if (markResult.IsFailure)
            {
                return Result.Failure<StatementFileResponse>(markResult.Error);
            }
        }

        context.DownloadAuditLogs.Add(DownloadAuditLog.Create(
            statement.Id,
            claims.UserId,
            AuditAction.StatementDownloaded,
            downloadToken.Id,
            query.IpAddress,
            query.UserAgent));

        await context.SaveChangesAsync(cancellationToken);

        Uri? presignedUri = await fileStorage.GeneratePresignedDownloadUriAsync(
            statement.StoragePath,
            TimeSpan.FromMinutes(5),
            cancellationToken);

        if (presignedUri is not null)
        {
            return new StatementFileResponse(presignedUri);
        }

        Stream fileStream = await fileStorage.RetrieveAsync(statement.StoragePath, cancellationToken);

        return new StatementFileResponse(fileStream, statement.ContentType, statement.OriginalFileName);
    }
}
