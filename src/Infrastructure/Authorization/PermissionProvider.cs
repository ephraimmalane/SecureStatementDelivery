using System.Security.Claims;
using Application.Abstractions.Data;
using Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Infrastructure.Authorization;

internal sealed class PermissionProvider(IApplicationDbContext context, HybridCache cache)
{
    internal static string ActiveCacheKey(Guid userId) => $"auth:active:{userId}";

    internal static string UserTag(Guid userId) => $"user:{userId}";

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
