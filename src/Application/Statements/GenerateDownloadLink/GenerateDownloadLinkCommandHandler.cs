using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.AuditLogs;
using Domain.DownloadTokens;
using Domain.Statements;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Statements.GenerateDownloadLink;

internal sealed class GenerateDownloadLinkCommandHandler(
    IApplicationDbContext context,
    IDownloadTokenService downloadTokenService,
    IUserContext userContext) : ICommandHandler<GenerateDownloadLinkCommand, DownloadLinkResponse>
{
    public async Task<Result<DownloadLinkResponse>> Handle(
        GenerateDownloadLinkCommand command,
        CancellationToken cancellationToken)
    {
        Guid requestingUserId = userContext.UserId;

        Statement? statement = await context.Statements
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == command.StatementId, cancellationToken);

        if (statement is null)
        {
            return Result.Failure<DownloadLinkResponse>(StatementErrors.NotFound(command.StatementId));
        }

        bool isAdmin = await context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == requestingUserId && u.RoleId == Role.Admin.Id, cancellationToken);

        if (!isAdmin && statement.CustomerId != requestingUserId)
        {
            return Result.Failure<DownloadLinkResponse>(StatementErrors.AccessDenied);
        }

        if (!statement.IsActive)
        {
            return Result.Failure<DownloadLinkResponse>(StatementErrors.AlreadyRevoked);
        }

        DateTime expiresAt = DateTime.UtcNow.AddMinutes(command.ExpiryMinutes);

        (string rawToken, Guid tokenId) = downloadTokenService.GenerateToken(
            statement.Id,
            requestingUserId,
            expiresAt);

        string tokenHash = downloadTokenService.HashToken(rawToken);

        var downloadToken = DownloadToken.Create(
            tokenId,
            statement.Id,
            requestingUserId,
            tokenHash,
            expiresAt,
            command.IsSingleUse,
            command.RequestIpAddress);

        context.DownloadTokens.Add(downloadToken);

        context.DownloadAuditLogs.Add(DownloadAuditLog.Create(
            statement.Id,
            requestingUserId,
            AuditAction.DownloadLinkGenerated,
            tokenId,
            command.RequestIpAddress));

        await context.SaveChangesAsync(cancellationToken);

        return new DownloadLinkResponse(rawToken, expiresAt, command.IsSingleUse);
    }
}
