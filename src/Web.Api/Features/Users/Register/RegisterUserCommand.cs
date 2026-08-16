using Application.Abstractions.Messaging;

namespace Web.Api.Features.Users.Register;

public sealed record RegisterUserCommand(
    string Email,
    string FirstName,
    string LastName,
    string Password,
    string SouthAfricanIdNumber) : ICommand<Guid>
{
    // Both the password and the SA ID number are secrets (the ID doubles as the statement PDF
    // password), so neither is ever rendered in logs/exception output.
#pragma warning disable S2068
    public override string ToString() =>
        $"RegisterUserCommand {{ Email = {Email}, FirstName = {FirstName}, LastName = {LastName}, " +
        $"Password = [REDACTED], SouthAfricanIdNumber = [REDACTED] }}";
#pragma warning restore S2068
}
