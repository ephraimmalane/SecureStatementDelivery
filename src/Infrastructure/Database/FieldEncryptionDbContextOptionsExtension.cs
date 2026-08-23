using Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Database;

internal sealed class FieldEncryptionDbContextOptionsExtension(IFieldEncryptor encryptor)
    : IDbContextOptionsExtension
{
    public IFieldEncryptor Encryptor { get; } = encryptor;

    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

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

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["FieldEncryption"] = "1";
    }
}

public static class FieldEncryptionDbContextOptionsExtensions
{
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

    public static DbContextOptionsBuilder<TContext> UseFieldEncryption<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        IFieldEncryptor encryptor)
        where TContext : DbContext
    {
        UseFieldEncryption((DbContextOptionsBuilder)optionsBuilder, encryptor);
        return optionsBuilder;
    }
}
