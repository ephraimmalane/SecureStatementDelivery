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
    DbSet<DownloadAuditLog> DownloadAuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
