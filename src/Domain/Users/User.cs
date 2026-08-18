using SharedKernel;

namespace Domain.Users;

public sealed class User : Entity
{
    private User() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;

    // The customer's South African ID number. Required for every customer: it is the open password
    // on every statement PDF delivered to them, so an account can never exist without one. Stored
    // encrypted at rest (it is sensitive PII as well as a credential).
    public string SouthAfricanIdNumber { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    // Id must be the Keycloak subject (sub) UUID so domain IDs match JWT claims. The SA ID number is
    // mandatory and validated here (13 digits, valid date of birth, Luhn check), so an invalid or
    // missing value can never be persisted regardless of which caller creates the user.
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
