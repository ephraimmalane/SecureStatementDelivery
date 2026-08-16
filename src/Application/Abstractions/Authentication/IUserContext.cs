namespace Application.Abstractions.Authentication;

public interface IUserContext
{
    Guid UserId { get; }

    // True when the Keycloak JWT contains the "admin" realm role.
    bool IsAdmin { get; }
}
