using Domain.AuditLogs;
using Domain.DownloadTokens;
using Domain.Statements;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Statement> Statements { get; }
    DbSet<DownloadToken> DownloadTokens { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a database transaction executed via the context's
    /// execution strategy, so it composes with retry-on-failure (a manual BeginTransaction is not
    /// permitted under a retrying strategy). The transaction commits when the operation returns and
    /// rolls back if it throws.
    /// <para>
    /// <paramref name="verifyCommitted"/> guards non-idempotent work (e.g. consuming a single-use
    /// token): if a transient failure triggers a retry, it is invoked first to check whether the
    /// previous attempt actually committed. A non-null return short-circuits the retry with that
    /// result, so a committed-but-unacknowledged attempt is not re-run and mis-reported as failed.
    /// </para>
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task<T?>>? verifyCommitted = null,
        CancellationToken cancellationToken = default)
        where T : class;
}
