using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.Authentication;

internal static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal? principal)
    {
        // Keycloak JWT: "sub" claim — mapped to ClaimTypes.NameIdentifier by AddJwtBearer.
        string? userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(userId, out Guid parsedUserId)
            ? parsedUserId
            : throw new ApplicationException("User id is unavailable");
    }

    public static bool HasRealmRole(this ClaimsPrincipal? principal, string role)
    {
        // Keycloak embeds realm roles as a JSON object in the "realm_access" claim:
        // { "roles": ["admin", "customer", ...] }
        string? realmAccessJson = principal?.FindFirstValue("realm_access");
        if (realmAccessJson is null)
        {
            return false;
        }

        try
        {
            RealmAccessClaim? parsed = JsonSerializer.Deserialize<RealmAccessClaim>(realmAccessJson);
            return parsed?.Roles?.Contains(role, StringComparer.OrdinalIgnoreCase) ?? false;
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<string> GetRealmRoles(this ClaimsPrincipal? principal)
    {
        string? realmAccessJson = principal?.FindFirstValue("realm_access");
        if (realmAccessJson is null)
        {
            return [];
        }

        try
        {
            RealmAccessClaim? parsed = JsonSerializer.Deserialize<RealmAccessClaim>(realmAccessJson);
            return parsed?.Roles ?? [];
        }
        catch
        {
            return [];
        }
    }

    private sealed class RealmAccessClaim
    {
        [JsonPropertyName("roles")]
        public List<string>? Roles { get; init; }
    }
}
