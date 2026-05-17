using Application.Abstractions.Data;
using Domain.AuditLogs;
using Domain.DownloadTokens;
using Domain.Statements;
using Domain.Users;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Infrastructure.Database;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    IDomainEventsDispatcher domainEventsDispatcher)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<Statement> Statements { get; set; }
    public DbSet<DownloadToken> DownloadTokens { get; set; }
    public DbSet<DownloadAuditLog> DownloadAuditLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        modelBuilder.HasDefaultSchema(Schemas.Default);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        List<IDomainEvent> domainEvents = ExtractDomainEvents();
        int result = await base.SaveChangesAsync(cancellationToken);
        await domainEventsDispatcher.DispatchAsync(domainEvents, CancellationToken.None);
        return result;
    }

    private List<IDomainEvent> ExtractDomainEvents()
    {
        return ChangeTracker
            .Entries<Entity>()
            .Select(entry => entry.Entity)
            .SelectMany(entity =>
            {
                List<IDomainEvent> events = entity.DomainEvents;
                entity.ClearDomainEvents();
                return events;
            })
            .ToList();
    }
}
