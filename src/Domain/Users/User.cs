using SharedKernel;

namespace Domain.Users;

public sealed class User : Entity
{
    private User() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;

    public string SouthAfricanIdNumber { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    public static Result<User> Create(
        Guid keycloakId,
        string email,
        string firstName,
        string lastName,
        string southAfricanIdNumber)
    {
        if (!SouthAfricanIdValidator.IsValid(southAfricanIdNumber))
        {
            return Result.Failure<User>(UserErrors.InvalidIdNumber);
        }

        var user = new User
        {
            Id = keycloakId,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            SouthAfricanIdNumber = southAfricanIdNumber.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        user.Raise(new UserRegisteredDomainEvent(user.Id));

        return user;
    }

    public void Deactivate() => IsActive = false;
}
