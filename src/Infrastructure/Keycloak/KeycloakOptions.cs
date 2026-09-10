using System.ComponentModel.DataAnnotations;

namespace Infrastructure.Keycloak;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    [Required]
    public string BaseUrl { get; init; } = string.Empty;

    [Required]
    public string Realm { get; init; } = string.Empty;

    [Required]
    public string ClientId { get; init; } = string.Empty;

    [Required]
    public string ClientSecret { get; init; } = string.Empty;

    [Required]
    public string AdminUsername { get; init; } = string.Empty;

    [Required]
    public string AdminPassword { get; init; } = string.Empty;

    public string Authority => $"{BaseUrl}/realms/{Realm}";
    public string TokenUrl => $"{BaseUrl}/realms/{Realm}/protocol/openid-connect/token";
    public string MasterTokenUrl => $"{BaseUrl}/realms/master/protocol/openid-connect/token";
    public string AdminUsersUrl => $"{BaseUrl}/admin/realms/{Realm}/users";
}
