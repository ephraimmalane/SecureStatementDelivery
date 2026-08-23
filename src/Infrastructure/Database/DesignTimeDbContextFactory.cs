using Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Infrastructure.Database;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
#pragma warning disable S2068
        const string connectionString =
            "Host=localhost;Port=5432;Database=secure_statements;Username=postgres;Password=postgres";
#pragma warning restore S2068

        DbContextOptions<ApplicationDbContext> options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schemas.Default))
                .UseSnakeCaseNamingConvention()
                .UseFieldEncryption(new PassthroughFieldEncryptor())
                .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class PassthroughFieldEncryptor : IFieldEncryptor
    {
        public string Encrypt(string plaintext) => plaintext;

        public string Decrypt(string ciphertext) => ciphertext;
    }
}
