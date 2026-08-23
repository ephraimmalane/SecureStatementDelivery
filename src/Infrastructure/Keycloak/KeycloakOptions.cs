namespace Infrastructure.Keycloak;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    public string BaseUrl { get; init; } = string.Empty;
    public string Realm { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;

    public string AdminUsername { get; init; } = string.Empty;
    public string AdminPassword { get; init; } = string.Empty;

    public string Authority => $"{BaseUrl}/realms/{Realm}";
    public string TokenUrl => $"{BaseUrl}/realms/{Realm}/protocol/openid-connect/token";
    public string MasterTokenUrl => $"{BaseUrl}/realms/master/protocol/openid-connect/token";
    public string AdminUsersUrl => $"{BaseUrl}/admin/realms/{Realm}/users";
}
