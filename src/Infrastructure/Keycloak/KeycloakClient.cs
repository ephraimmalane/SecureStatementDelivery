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
    IOptions<KeycloakOptions> options) : IKeycloakClient
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

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, options.Value.AdminUsersUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(userBody, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        using HttpResponseMessage createResponse = await httpClient.SendAsync(createRequest, cancellationToken);

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

    public async Task DeleteUserAsync(Guid keycloakUserId, CancellationToken cancellationToken)
    {
        string adminToken = await GetMasterAdminTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{options.Value.AdminUsersUrl}/{keycloakUserId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

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
        using var roleRequest = new HttpRequestMessage(HttpMethod.Get, rolesUrl);
        roleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        using HttpResponseMessage roleResponse = await httpClient.SendAsync(roleRequest, cancellationToken);
        roleResponse.EnsureSuccessStatusCode();

        string roleJson = await roleResponse.Content.ReadAsStringAsync(cancellationToken);

        // Assign the role to the user
        string roleMappingUrl = $"{options.Value.AdminUsersUrl}/{userId}/role-mappings/realm";
        using var assignRequest = new HttpRequestMessage(HttpMethod.Post, roleMappingUrl)
        {
            Content = new StringContent($"[{roleJson}]", Encoding.UTF8, "application/json")
        };
        assignRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        using HttpResponseMessage assignResponse = await httpClient.SendAsync(assignRequest, cancellationToken);
        assignResponse.EnsureSuccessStatusCode();
    }

    private async Task<string> GetMasterAdminTokenAsync(CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = options.Value.AdminUsername,
            ["password"] = options.Value.AdminPassword
        };

        KeycloakTokenResponse response = await PostTokenAsync(
            options.Value.MasterTokenUrl,
            form,
            cancellationToken);

        return response.AccessToken;
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
