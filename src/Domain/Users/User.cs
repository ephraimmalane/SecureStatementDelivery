using SharedKernel;

namespace Domain.Users;

public sealed class User : Entity
{
    private User() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;

    // The customer's South African ID number. Used as the open password for every statement PDF
    // delivered to this customer, so it must be present before a statement can be issued. Stored
    // encrypted at rest (it is sensitive PII as well as a credential).
    public string? SouthAfricanIdNumber { get; private set; }

    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    // Id must be the Keycloak subject (sub) UUID so domain IDs match JWT claims.
    public static User Create(
        Guid keycloakId,
        string email,
        string firstName,
        string lastName,
        string? southAfricanIdNumber = null)
    {
        var user = new User
        {
            Id = keycloakId,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            SouthAfricanIdNumber = string.IsNullOrWhiteSpace(southAfricanIdNumber)
                ? null
                : southAfricanIdNumber.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        user.Raise(new UserRegisteredDomainEvent(user.Id));

        return user;
    }

    // Set or update the customer's SA ID number (e.g. an admin backfilling it for a customer who
    // was provisioned before the field existed). Validated here so an invalid value can never be
    // persisted, whichever code path sets it.
    public Result SetSouthAfricanIdNumber(string idNumber)
    {
        if (!SouthAfricanIdValidator.IsValid(idNumber))
        {
            return Result.Failure(UserErrors.InvalidIdNumber);
        }

        SouthAfricanIdNumber = idNumber.Trim();
        return Result.Success();
    }

    public void Deactivate() => IsActive = false;
}
