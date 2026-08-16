using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Authentication;
using Application.Abstractions.Cache;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Storage;
using Domain.AuditLogs;
using Domain.Statements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;
using Web.Api.Features.Statements.Download;

namespace Web.Api.Features.Statements.Consolidated;

internal sealed class ConsolidatedStatementQueryHandler(
    IApplicationDbContext context,
    IFileStorageService fileStorage,
    IPdfConsolidator consolidator,
    ICacheService cache,
    IOptions<ConsolidationOptions> options,
    IUserContext userContext) : IQueryHandler<ConsolidatedStatementQuery, StatementFileResponse>
{
    private readonly ConsolidationOptions _options = options.Value;

    public async Task<Result<StatementFileResponse>> Handle(
        ConsolidatedStatementQuery query,
        CancellationToken cancellationToken)
    {
        string from = query.From?.Trim() ?? string.Empty;
        string to = query.To?.Trim() ?? string.Empty;

        if (!Statement.IsValidPeriod(from) || !Statement.IsValidPeriod(to))
        {
            return Result.Failure<StatementFileResponse>(StatementErrors.InvalidPeriodFormat);
        }

        List<string>? periods = ExpandRange(from, to, _options.MaxMonths);
        if (periods is null)
        {
            return Result.Failure<StatementFileResponse>(StatementErrors.InvalidPeriodRange);
        }

        Guid userId = userContext.UserId;
        bool isAdmin = userContext.IsAdmin;

        // A customer always gets their own statements; only an admin may target another customer.
        Guid customerId = isAdmin && query.CustomerId.HasValue ? query.CustomerId.Value : userId;

        // The SA ID opens the (encrypted) source statements and re-protects the merged output.
        string? idNumber = await context.Users
            .Where(u => u.Id == customerId && u.IsActive)
            .Select(u => u.SouthAfricanIdNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (idNumber is null)
        {
            bool exists = await context.Users
                .AnyAsync(u => u.Id == customerId && u.IsActive, cancellationToken);

            return Result.Failure<StatementFileResponse>(exists
                ? StatementErrors.CustomerIdNumberMissing
                : StatementErrors.CustomerNotFound);
        }

        List<Statement> statements = await context.Statements
            .Where(s => s.CustomerId == customerId
                && s.Status == StatementStatus.Active
                && periods.Contains(s.Period))
            .OrderBy(s => s.Period)
            .ToListAsync(cancellationToken);

        if (statements.Count == 0)
        {
            return Result.Failure<StatementFileResponse>(StatementErrors.NoStatementsInRange);
        }

        // Fingerprint the request by content, so the cache key changes whenever the included set or
        // the encryption password changes (new upload, revoke, ID correction). The cache can never
        // serve a stale or wrongly-encrypted consolidation.
        string cacheKey = BuildCacheKey(customerId, from, to, idNumber, statements);

        byte[]? pdfBytes = await cache.GetBytesAsync(cacheKey, cancellationToken);
        if (pdfBytes is null)
        {
            pdfBytes = await GenerateAsync(statements, idNumber, cancellationToken);

            if (pdfBytes.Length <= _options.MaxCacheableBytes)
            {
                await cache.SetBytesAsync(cacheKey, pdfBytes, _options.CacheTtl, cancellationToken);
            }
        }

        // Audit every included statement on each access — cache hit or miss.
        foreach (Statement statement in statements)
        {
            context.DownloadAuditLogs.Add(DownloadAuditLog.Create(
                statement.Id,
                userId,
                AuditAction.StatementDownloaded,
                downloadTokenId: null,
                query.IpAddress,
                query.UserAgent));
        }

        await context.SaveChangesAsync(cancellationToken);

        return new StatementFileResponse(
            new MemoryStream(pdfBytes, writable: false),
            "application/pdf",
            $"statements-{from}_to_{to}.pdf");
    }

    // Retrieves each source statement, merges them, and returns the encrypted PDF bytes.
    private async Task<byte[]> GenerateAsync(
        List<Statement> statements,
        string idNumber,
        CancellationToken cancellationToken)
    {
        var sources = new List<Stream>(statements.Count);
        try
        {
            foreach (Statement statement in statements)
            {
                Stream stored = await fileStorage.RetrieveAsync(statement.StoragePath, cancellationToken);
                var buffer = new MemoryStream();
                await using (stored)
                {
                    await stored.CopyToAsync(buffer, cancellationToken);
                }

                buffer.Position = 0;
                sources.Add(buffer);
            }

            await using Stream merged = await consolidator.ConsolidateAsync(sources, idNumber, cancellationToken);
            using var output = new MemoryStream();
            await merged.CopyToAsync(output, cancellationToken);
            return output.ToArray();
        }
        finally
        {
            foreach (Stream source in sources)
            {
                await source.DisposeAsync();
            }
        }
    }

    private static string BuildCacheKey(
        Guid customerId,
        string from,
        string to,
        string idNumber,
        List<Statement> statements)
    {
        // SHA-256 over the SA ID + the ordered statement ids. The ID is hashed, never placed in the
        // key in clear. Any change to the set or the password yields a different key.
        string input = idNumber + "|" + string.Join(",", statements.Select(s => s.Id));
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        return $"consolidated:{customerId}:{from}:{to}:{fingerprint}";
    }

    // Expands [from, to] (inclusive) into canonical YYYY-MM periods, or null if the range is
    // backwards or longer than maxMonths.
    private static List<string>? ExpandRange(string from, string to, int maxMonths)
    {
        int fromIndex = MonthIndex(from);
        int toIndex = MonthIndex(to);

        if (toIndex < fromIndex || toIndex - fromIndex >= maxMonths)
        {
            return null;
        }

        var periods = new List<string>(toIndex - fromIndex + 1);
        for (int index = fromIndex; index <= toIndex; index++)
        {
            int year = index / 12;
            int month = index % 12 + 1;
            periods.Add($"{year:D4}-{month:D2}");
        }

        return periods;
    }

    private static int MonthIndex(string period)
    {
        int year = int.Parse(period.AsSpan(0, 4), CultureInfo.InvariantCulture);
        int month = int.Parse(period.AsSpan(5, 2), CultureInfo.InvariantCulture);
        return year * 12 + month - 1;
    }
}
