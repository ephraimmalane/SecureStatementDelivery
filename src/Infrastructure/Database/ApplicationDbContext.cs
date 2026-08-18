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
using SharedKernel;

namespace Infrastructure.Database;

// Single DbContextOptions-only constructor so the context is eligible for AddDbContextPool. The
// field encryptor is supplied via the options (UseFieldEncryption) rather than constructor
// injection, which pooling forbids.
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Statement> Statements { get; set; }
    public DbSet<DownloadToken> DownloadTokens { get; set; }
    public DbSet<DownloadAuditLog> DownloadAuditLogs { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        modelBuilder.HasDefaultSchema(Schemas.Default);

        // Supplied via DbContextOptions (UseFieldEncryption) so the context can stay pooling-eligible
        // with a single DbContextOptions-only constructor.
        IFieldEncryptor fieldEncryptor = this.GetService<IDbContextOptions>()
            .FindExtension<FieldEncryptionDbContextOptionsExtension>()?.Encryptor
            ?? throw new InvalidOperationException(
                "Field encryption is not configured. Call optionsBuilder.UseFieldEncryption(...) " +
                "wherever ApplicationDbContext options are built.");

        // Encrypt the SA ID number at rest. It is a required field, so the converter always receives
        // a non-null value.
        modelBuilder.Entity<User>()
            .Property(u => u.SouthAfricanIdNumber)
            .HasConversion(
                plain => fieldEncryptor.Encrypt(plain),
                cipher => fieldEncryptor.Decrypt(cipher));
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Persist domain events as outbox rows in the SAME transaction as the state change.
        // They are dispatched asynchronously by OutboxProcessor, so a crash after commit can
        // never lose an event.
        AddDomainEventsAsOutboxMessages();
        return await base.SaveChangesAsync(cancellationToken);
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
