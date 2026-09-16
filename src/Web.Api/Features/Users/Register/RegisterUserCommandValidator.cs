using Domain.Users.Identity;
using FluentValidation;

namespace Web.Api.Features.Users.Register;

internal sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator(IIdentityDocumentValidatorResolver identityDocuments)
    {
        RuleFor(c => c.FirstName).NotEmpty();
        RuleFor(c => c.LastName).NotEmpty();
        RuleFor(c => c.Email).NotEmpty().EmailAddress();

        RuleFor(c => c.Password)
            .NotEmpty()
            .MinimumLength(12).WithMessage("Password must be at least 12 characters long.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.")
            .Must((command, password) => !string.Equals(password, command.Email, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Password must not match the username.");

        RuleFor(c => c.SouthAfricanIdNumber)
            .NotEmpty()
            .Must(identityDocuments.Resolve(IdentityDocumentType.SouthAfricanId).IsValid)
            .WithMessage("South African ID number is not valid.");
    }
}
