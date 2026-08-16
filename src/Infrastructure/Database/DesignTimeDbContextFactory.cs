using Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Infrastructure.Database;

// Used only by EF Core tooling (dotnet ef migrations add / database update). It builds the
// DbContext directly so the design-time experience does not depend on the full Web.Api host
// (Keycloak/secret validation, etc.). Not used at runtime.
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // A connection string is required for the tooling to construct the provider, but
        // `migrations add` never connects. `database update` overrides this via --connection.
#pragma warning disable S2068 // Design-time-only placeholder; not a real credential.
        const string connectionString =
            "Host=localhost;Port=5432;Database=secure_statements;Username=postgres;Password=postgres";
#pragma warning restore S2068

        // Design-time only: the converter lambdas are used to build expressions for the model, never
        // executed against data, so a passthrough encryptor is sufficient for migration scaffolding.
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
