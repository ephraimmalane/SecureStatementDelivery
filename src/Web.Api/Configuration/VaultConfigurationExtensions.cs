using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.AppRole;
using VaultSharp.V1.AuthMethods.Token;
using VaultSharp.V1.Commons;

namespace Web.Api.Configuration;

/// <summary>
/// Loads application secrets from HashiCorp Vault (KV v2) into <see cref="IConfiguration"/>.
/// The Vault source is added last so it takes precedence over appsettings/env, and is only added
/// when a Vault address is configured — local dev and tests continue to use user-secrets/env vars
/// with no Vault dependency. Secrets are stored under keys using the ':' configuration delimiter
/// (e.g. "Keycloak:ClientSecret", "DownloadToken:Secret", "ConnectionStrings:Database").
/// </summary>
public static class VaultConfigurationExtensions
{
    public static IConfigurationBuilder AddVaultSecrets(
        this IConfigurationBuilder builder,
        IConfiguration bootstrap)
    {
        string? address = bootstrap["Vault:Address"] ?? Environment.GetEnvironmentVariable("VAULT_ADDR");

        if (string.IsNullOrWhiteSpace(address))
        {
            return builder;
        }

        var options = new VaultSecretsOptions
        {
            Address = address,
            Token = bootstrap["Vault:Token"] ?? Environment.GetEnvironmentVariable("VAULT_TOKEN"),
            RoleId = bootstrap["Vault:RoleId"] ?? Environment.GetEnvironmentVariable("VAULT_ROLE_ID"),
            SecretId = bootstrap["Vault:SecretId"] ?? Environment.GetEnvironmentVariable("VAULT_SECRET_ID"),
            MountPoint = bootstrap["Vault:MountPoint"] ?? "secret",
            Path = bootstrap["Vault:Path"] ?? "secure-statement-delivery"
        };

        builder.Add(new VaultConfigurationSource(options));
        return builder;
    }
}

internal sealed class VaultSecretsOptions
{
    public required string Address { get; init; }
    public string? Token { get; init; }
    public string? RoleId { get; init; }
    public string? SecretId { get; init; }
    public required string MountPoint { get; init; }
    public required string Path { get; init; }
}

internal sealed class VaultConfigurationSource(VaultSecretsOptions options) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new VaultConfigurationProvider(options);
}

internal sealed class VaultConfigurationProvider(VaultSecretsOptions options) : ConfigurationProvider
{
    public override void Load()
    {
        IAuthMethodInfo authMethod = !string.IsNullOrWhiteSpace(options.RoleId)
            ? new AppRoleAuthMethodInfo(options.RoleId, options.SecretId)
            : new TokenAuthMethodInfo(
                options.Token
                ?? throw new InvalidOperationException(
                    "Vault is configured but no authentication was provided. Set Vault:Token " +
                    "(VAULT_TOKEN) or Vault:RoleId/Vault:SecretId (AppRole)."));

        var client = new VaultClient(new VaultClientSettings(options.Address, authMethod));

        Secret<SecretData> secret = client.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(path: options.Path, mountPoint: options.MountPoint)
            .GetAwaiter()
            .GetResult();

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, object> entry in secret.Data.Data)
        {
            data[entry.Key] = entry.Value?.ToString();
        }

        Data = data;
    }
}
