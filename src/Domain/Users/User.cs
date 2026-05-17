using SharedKernel;

namespace Domain.Users;

public sealed class User : Entity
{
    private User() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public int RoleId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public Role Role { get; private set; } = null!;
    public ICollection<RefreshToken> RefreshTokens { get; private set; } = [];

    public string FullName => $"{FirstName} {LastName}";

    public static User Create(
        string email,
        string firstName,
        string lastName,
        string passwordHash,
        int roleId = 2)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            PasswordHash = passwordHash,
            RoleId = roleId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        user.Raise(new UserRegisteredDomainEvent(user.Id));

        return user;
    }

    public void Deactivate() => IsActive = false;
}
