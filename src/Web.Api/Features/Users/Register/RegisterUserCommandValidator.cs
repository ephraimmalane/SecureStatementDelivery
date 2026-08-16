using Domain.Users;
using FluentValidation;

namespace Web.Api.Features.Users.Register;

internal sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(c => c.FirstName).NotEmpty();
        RuleFor(c => c.LastName).NotEmpty();
        RuleFor(c => c.Email).NotEmpty().EmailAddress();
        RuleFor(c => c.Password).NotEmpty().MinimumLength(8);

        // Full SA ID validation: 13 digits, a valid date of birth, and a correct Luhn check digit
        // (not just length). This value becomes the open password on every statement PDF.
        RuleFor(c => c.SouthAfricanIdNumber)
            .NotEmpty()
            .Must(SouthAfricanIdValidator.IsValid)
            .WithMessage("South African ID number is not valid.");
    }
}
