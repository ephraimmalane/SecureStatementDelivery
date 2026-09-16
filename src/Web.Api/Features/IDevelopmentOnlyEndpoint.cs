namespace Web.Api.Features;

/// <summary>
/// Marks an endpoint that must only be exposed outside Production. Used for the ROPC
/// (password-grant) auth endpoints, which exist for local development and tests only. In
/// Production the API is a pure OAuth2 resource server and never handles user credentials —
/// token acquisition is performed by the client against Keycloak via Authorization Code + PKCE.
/// </summary>
public interface IDevelopmentOnlyEndpoint : IEndpoint;
