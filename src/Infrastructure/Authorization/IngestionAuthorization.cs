namespace Infrastructure.Authorization;

// Authorization for the machine-to-machine statement ingestion endpoint. Unlike the customer/admin
// endpoints — which authorize a human through the permission model and a per-user active check —
// the ingestion identity is a Keycloak *service account* authenticated with the client_credentials
// grant. It carries the realm role below and is authorized directly off that role claim; there is
// no domain User row to look up, so it deliberately bypasses PermissionProvider.
public static class IngestionAuthorization
{
    // Keycloak realm role granted to the statement-generation service account.
    public const string Role = "statement-ingest";

    // Named authorization policy applied to the ingestion endpoint.
    public const string PolicyName = "StatementsIngest";
}
