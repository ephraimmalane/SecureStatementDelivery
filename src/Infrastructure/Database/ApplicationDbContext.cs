using System.Text.Json;
using Application.Abstractions.Data;
using Domain.AuditLogs;
using Domain.DownloadTokens;
using Domain.Statements;
using Domain.Users;
using Infrastructure.Outbox;
using Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel;

namespace Infrastructure.Database;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Statement> Statements { get; set; }
    public DbSet<DownloadToken> DownloadTokens { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        modelBuilder.HasDefaultSchema(Schemas.Default);

        IFieldEncryptor fieldEncryptor = this.GetService<IDbContextOptions>()
            .FindExtension<FieldEncryptionDbContextOptionsExtension>()?.Encryptor
            ?? throw new InvalidOperationException(
                "Field encryption is not configured. Call optionsBuilder.UseFieldEncryption(...) " +
                "wherever ApplicationDbContext options are built.");

        modelBuilder.Entity<User>()
            .Property(u => u.SouthAfricanIdNumber)
            .HasConversion(
                plain => fieldEncryptor.Encrypt(plain),
                cipher => fieldEncryptor.Decrypt(cipher));
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AddDomainEventsAsOutboxMessages();
        return await base.SaveChangesAsync(cancellationToken);
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task<T?>>? verifyCommitted = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        IExecutionStrategy strategy = Database.CreateExecutionStrategy();

        bool isRetry = false;

        return await strategy.ExecuteAsync(async ct =>
        {
            if (isRetry && verifyCommitted is not null)
            {
                T? committed = await verifyCommitted(ct);
                if (committed is not null)
                {
                    return committed;
                }
            }

            isRetry = true;

            await using IDbContextTransaction transaction = await Database.BeginTransactionAsync(ct);

            T result = await operation(ct);

            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }

    private void AddDomainEventsAsOutboxMessages()
    {
        var outboxMessages = ChangeTracker
            .Entries<Entity>()
            .Select(entry => entry.Entity)
            .SelectMany(entity =>
            {
                List<IDomainEvent> events = entity.DomainEvents;
                entity.ClearDomainEvents();
                return events;
            })
            .Select(domainEvent => new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = domainEvent.GetType().AssemblyQualifiedName!,
                Content = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                OccurredOnUtc = DateTime.UtcNow
            })
            .ToList();

        if (outboxMessages.Count > 0)
        {
            OutboxMessages.AddRange(outboxMessages);
        }
    }
}
