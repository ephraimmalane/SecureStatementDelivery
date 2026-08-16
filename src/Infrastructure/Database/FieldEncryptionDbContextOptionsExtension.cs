using Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Database;

// Carries the field encryptor on the DbContextOptions so ApplicationDbContext.OnModelCreating can
// configure the encryption-at-rest value converter.
//
// Why this exists: AddDbContextPool requires ApplicationDbContext to have a single public
// constructor taking only DbContextOptions<T>, so the encryptor can no longer be a constructor
// parameter. Attaching it to the options is the pooling-safe way to give the model a singleton
// dependency, and it works identically for the pooled runtime context, the integration-test
// (SQLite) context, and the design-time factory.
internal sealed class FieldEncryptionDbContextOptionsExtension(IFieldEncryptor encryptor)
    : IDbContextOptionsExtension
{
    public IFieldEncryptor Encryptor { get; } = encryptor;

    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

    // Pure data carrier — no EF services to register.
    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using FieldEncryption ";

        // The encryptor is a stateless singleton, so every instance is equivalent for the purpose
        // of EF's internal service-provider / model cache key.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["FieldEncryption"] = "1";
    }
}

public static class FieldEncryptionDbContextOptionsExtensions
{
    // Attaches the field encryptor used to build the encryption-at-rest converters in
    // ApplicationDbContext.OnModelCreating. Call this everywhere ApplicationDbContext options are
    // built (runtime DI, tests, design-time tooling).
    public static DbContextOptionsBuilder UseFieldEncryption(
        this DbContextOptionsBuilder optionsBuilder,
        IFieldEncryptor encryptor)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(encryptor);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(new FieldEncryptionDbContextOptionsExtension(encryptor));

        return optionsBuilder;
    }

    // Generic overload so the fluent chain keeps the DbContextOptionsBuilder<TContext> type and
    // .Options returns DbContextOptions<TContext> (used by the design-time factory).
    public static DbContextOptionsBuilder<TContext> UseFieldEncryption<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        IFieldEncryptor encryptor)
        where TContext : DbContext
    {
        UseFieldEncryption((DbContextOptionsBuilder)optionsBuilder, encryptor);
        return optionsBuilder;
    }
}
