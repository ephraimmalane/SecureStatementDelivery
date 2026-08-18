using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.Users;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.Keycloak;

internal sealed class KeycloakClient(
    HttpClient httpClient,
    IOptions<KeycloakOptions> options,
    KeycloakAdminTokenCache adminTokenCache) : IKeycloakClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<KeycloakTokenResponse> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = options.Value.ClientId,
            ["client_secret"] = options.Value.ClientSecret,
            ["username"] = email,
            ["password"] = password,
            ["scope"] = "openid"
        };

        return await PostTokenAsync(options.Value.TokenUrl, form, cancellationToken);
    }

    public async Task<KeycloakTokenResponse> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = options.Value.ClientId,
            ["client_secret"] = options.Value.ClientSecret,
            ["refresh_token"] = refreshToken
        };

        return await PostTokenAsync(options.Value.TokenUrl, form, cancellationToken);
    }

    public async Task<Guid> RegisterUserAsync(
        string email,
        string firstName,
        string lastName,
        string password,
        CancellationToken cancellationToken)
    {
        string adminToken = await GetMasterAdminTokenAsync(cancellationToken);

        // Create the user in Keycloak
        var userBody = new
        {
            username = email,
            email,
            firstName,
            lastName,
            enabled = true,
            emailVerified = true,
            credentials = new[]
            {
                new { type = "password", value = password, temporary = false }
            },
            realmRoles = new[] { "customer" }
        };

        using HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            options.Value.AdminUsersUrl,
            adminToken,
            new StringContent(
                JsonSerializer.Serialize(userBody, JsonOptions),
                Encoding.UTF8,
                "application/json"),
            cancellationToken);

        if (createResponse.StatusCode == HttpStatusCode.Conflict)
        {
            throw new KeycloakRegistrationException(UserErrors.EmailNotUnique);
        }

        if (!createResponse.IsSuccessStatusCode)
        {
            string body = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Keycloak user creation failed ({createResponse.StatusCode}): {body}");
        }

        // Extract the new user's UUID from the Location header: .../users/{uuid}
        string? location = createResponse.Headers.Location?.ToString();
        string[] locationParts = location?.Split('/') ?? [];
        if (location is null || !Guid.TryParse(locationParts[^1], out Guid keycloakUserId))
        {
            throw new InvalidOperationException("Keycloak did not return a user Location header.");
        }

        // Assign the customer realm role
        await AssignRealmRoleAsync(adminToken, keycloakUserId, "customer", cancellationToken);

        return keycloakUserId;
    }

    public async Task<Guid?> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        string adminToken = await GetMasterAdminTokenAsync(cancellationToken);

        using HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            $"{options.Value.AdminUsersUrl}?email={Uri.EscapeDataString(email)}&exact=true",
            adminToken,
            content: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Array ||
            document.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        if (document.RootElement[0].TryGetProperty("id", out JsonElement idElement) &&
            Guid.TryParse(idElement.GetString(), out Guid userId))
        {
            return userId;
        }

        return null;
    }

    public async Task DeleteUserAsync(Guid keycloakUserId, CancellationToken cancellationToken)
    {
        string adminToken = await GetMasterAdminTokenAsync(cancellationToken);

        using HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Delete,
            $"{options.Value.AdminUsersUrl}/{keycloakUserId}",
            adminToken,
            content: null,
            cancellationToken);

        // A 404 means the user is already gone — treat as success (idempotent rollback).
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    private async Task AssignRealmRoleAsync(
        string adminToken,
        Guid userId,
        string roleName,
        CancellationToken cancellationToken)
    {
        // Fetch the role representation from Keycloak
        string rolesUrl = $"{options.Value.BaseUrl}/admin/realms/{options.Value.Realm}/roles/{roleName}";
        using HttpResponseMessage roleResponse = await SendWithBearerAsync(
            HttpMethod.Get, rolesUrl, adminToken, content: null, cancellationToken);
        roleResponse.EnsureSuccessStatusCode();

        string roleJson = await roleResponse.Content.ReadAsStringAsync(cancellationToken);

        // Assign the role to the user
        string roleMappingUrl = $"{options.Value.AdminUsersUrl}/{userId}/role-mappings/realm";
        using HttpResponseMessage assignResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            roleMappingUrl,
            adminToken,
            new StringContent($"[{roleJson}]", Encoding.UTF8, "application/json"),
            cancellationToken);
        assignResponse.EnsureSuccessStatusCode();
    }

    // Sends an admin request with the bearer token attached. Centralises request creation + the
    // Authorization header so each call site only supplies the verb, URL, and optional body. The
    // request (and its content) is disposed after the send; the caller owns the returned response.
    private async Task<HttpResponseMessage> SendWithBearerAsync(
        HttpMethod method,
        string url,
        string bearerToken,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return await httpClient.SendAsync(request, cancellationToken);
    }

    // Cache-backed: only performs a real password-grant login when the cache is empty or the token is
    // near expiry; otherwise returns the shared, still-valid admin token.
    private Task<string> GetMasterAdminTokenAsync(CancellationToken cancellationToken) =>
        adminTokenCache.GetTokenAsync(FetchMasterAdminTokenAsync, cancellationToken);

    private Task<KeycloakTokenResponse> FetchMasterAdminTokenAsync(CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = options.Value.AdminUsername,
            ["password"] = options.Value.AdminPassword
        };

        return PostTokenAsync(options.Value.MasterTokenUrl, form, cancellationToken);
    }

    private async Task<KeycloakTokenResponse> PostTokenAsync(
        string url,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        };

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new KeycloakAuthException(response.StatusCode, body);
        }

        return JsonSerializer.Deserialize<KeycloakTokenResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize Keycloak token response.");
    }
}

// Typed exceptions allow feature handlers to map Keycloak errors to domain errors cleanly.
public sealed class KeycloakAuthException(HttpStatusCode statusCode, string body) : Exception(body)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class KeycloakRegistrationException(Error domainError) : Exception(domainError.Description)
{
    public Error DomainError { get; } = domainError;
}
