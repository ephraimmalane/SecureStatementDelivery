using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Storage;
using Domain.AuditLogs;
using Domain.DownloadTokens;
using Domain.Statements;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Web.Api.Features.Statements.Download;

internal sealed class DownloadStatementQueryHandler(
    IApplicationDbContext context,
    IDownloadTokenService downloadTokenService,
    IFileStorageService fileStorage,
    TimeProvider timeProvider) : IQueryHandler<DownloadStatementQuery, StatementFileResponse>
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

        if (downloadToken.StatementId != claims.StatementId)
        {
            return Result.Failure<StatementFileResponse>(DownloadTokenErrors.TokenInvalid);
        }

        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;

        if (downloadToken.IsExpired(utcNow))
        {
            return Result.Failure<StatementFileResponse>(DownloadTokenErrors.TokenExpired);
        }

        if (downloadToken.IsSingleUse && downloadToken.IsUsed)
        {
            return Result.Failure<StatementFileResponse>(DownloadTokenErrors.TokenAlreadyUsed);
        }

        if (downloadToken.IpAddress is not null &&
            !downloadToken.IpAddress.Equals(query.IpAddress, StringComparison.OrdinalIgnoreCase))
        {
            context.AuditLogs.Add(AuditLog.Create(
                downloadToken.StatementId,
                claims.UserId,
                AuditAction.DownloadDenied,
                downloadToken.Id,
                query.IpAddress,
                query.UserAgent,
                additionalData: $"IP mismatch: expected {downloadToken.IpAddress}, got {query.IpAddress}"));

            await context.SaveChangesAsync(cancellationToken);

            return Result.Failure<StatementFileResponse>(DownloadTokenErrors.IpAddressMismatch);
        }

        Statement? statement = await context.Statements
            .SingleOrDefaultAsync(s => s.Id == downloadToken.StatementId, cancellationToken);

        if (statement is null || !statement.IsActive)
        {
            return Result.Failure<StatementFileResponse>(StatementErrors.NotFound(downloadToken.StatementId));
        }

        Result consume = await context.ExecuteInTransactionAsync(async ct =>
        {
            if (downloadToken.IsSingleUse)
            {
                int consumed = await context.DownloadTokens
                    .Where(t => t.Id == downloadToken.Id && !t.IsUsed)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(t => t.IsUsed, true)
                            .SetProperty(t => t.UsedAt, utcNow),
                        ct);

                if (consumed == 0)
                {
                    return Result.Failure(DownloadTokenErrors.TokenAlreadyUsed);
                }
            }

            context.AuditLogs.Add(AuditLog.Create(
                statement.Id,
                claims.UserId,
                AuditAction.DownloadAuthorized,
                downloadToken.Id,
                query.IpAddress,
                query.UserAgent));

            await context.SaveChangesAsync(ct);
            return Result.Success();
        },
        verifyCommitted: async ct =>
        {
            bool authorized = await context.AuditLogs
                .AsNoTracking()
                .AnyAsync(
                    a => a.DownloadTokenId == downloadToken.Id && a.Action == AuditAction.DownloadAuthorized,
                    ct);

            return authorized ? Result.Success() : null;
        },
        cancellationToken);

        if (consume.IsFailure)
        {
            return Result.Failure<StatementFileResponse>(consume.Error);
        }

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
