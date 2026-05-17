using Application.Abstractions.Data;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Authorization;

internal sealed class PermissionProvider(IApplicationDbContext context)
{
    private static readonly Dictionary<int, HashSet<string>> RolePermissions = new()
    {
        [Role.Admin.Id] =
        [
            Permissions.StatementsUpload,
            Permissions.StatementsReadAny,
            Permissions.StatementsReadOwn,
            Permissions.StatementsRevoke,
            Permissions.StatementsDownload,
            Permissions.AdminAuditLogs,
            Permissions.AdminUsers
        ],
        [Role.Customer.Id] =
        [
            Permissions.StatementsReadOwn,
            Permissions.StatementsDownload
        ]
    };

    public async Task<HashSet<string>> GetForUserIdAsync(Guid userId)
    {
        var user = await context.Users
            .AsNoTracking()
            .Select(u => new { u.Id, u.RoleId, u.IsActive })
            .SingleOrDefaultAsync(u => u.Id == userId);

        if (user is null || !user.IsActive)
        {
            return [];
        }

        return RolePermissions.TryGetValue(user.RoleId, out HashSet<string>? permissions)
            ? permissions
            : [];
    }
}
