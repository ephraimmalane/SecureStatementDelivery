using System.Security.Claims;
using Application.Abstractions.Data;
using Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Infrastructure.Authorization;

internal sealed class PermissionProvider(IApplicationDbContext context, HybridCache cache)
{
    // Cache key for the per-user active-status check. Tagged by user so the entry can be
    // invalidated explicitly (cache.RemoveByTagAsync) when a user is suspended/reactivated.
    internal static string ActiveCacheKey(Guid userId) => $"auth:active:{userId}";

    internal static string UserTag(Guid userId) => $"user:{userId}";

    // Keycloak role name → permission set. Roles are read from JWT claims — no DB query needed.
    private static readonly Dictionary<string, HashSet<string>> RolePermissions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["admin"] =
            [
                Permissions.StatementsUpload,
                Permissions.StatementsReadAny,
                Permissions.StatementsReadOwn,
                Permissions.StatementsRevoke,
                Permissions.StatementsDownload,
                Permissions.AdminAuditLogs,
                Permissions.AdminUsers
            ],
            ["customer"] =
            [
                Permissions.StatementsReadOwn,
                Permissions.StatementsDownload
            ]
        };

    public async Task<HashSet<string>> GetPermissionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        Guid userId = principal.GetUserId();

        // Business-level suspension check. Keycloak enforces authentication; we enforce domain
        // suspension independently. This runs on every authorized request, so it is read through
        // HybridCache (L1 in-process + L2 Redis, with stampede protection): a cold key hits
        // Postgres once, then subsequent requests across all replicas are served from cache until
        // the short TTL elapses. Roles still come from the JWT, so only this DB lookup is cached.
        bool isActive = await cache.GetOrCreateAsync(
            ActiveCacheKey(userId),
            (context, userId),
            static async (state, ct) => await state.context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Id == state.userId && u.IsActive, ct),
            tags: [UserTag(userId)],
            cancellationToken: cancellationToken);

        if (!isActive)
        {
            return [];
        }

        // Roles come from Keycloak's realm_access.roles claim — no additional DB hit needed.
        IReadOnlyList<string> roles = principal.GetRealmRoles();

        var permissions = new HashSet<string>();
        foreach (string role in roles)
        {
            if (RolePermissions.TryGetValue(role, out HashSet<string>? rolePermissions))
            {
                permissions.UnionWith(rolePermissions);
            }
        }

        return permissions;
    }
}
