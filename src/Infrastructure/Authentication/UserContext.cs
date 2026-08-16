using Application.Abstractions.Authentication;
using Microsoft.AspNetCore.Http;

namespace Infrastructure.Authentication;

internal sealed class UserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    public Guid UserId =>
        httpContextAccessor.HttpContext?.User.GetUserId()
        ?? throw new UserContextUnavailableException();

    public bool IsAdmin =>
        httpContextAccessor.HttpContext?.User.HasRealmRole("admin") ?? false;
}
