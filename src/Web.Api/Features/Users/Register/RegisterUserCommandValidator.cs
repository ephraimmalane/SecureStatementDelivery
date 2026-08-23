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

        RuleFor(c => c.SouthAfricanIdNumber)
            .NotEmpty()
            .Must(SouthAfricanIdValidator.IsValid)
            .WithMessage("South African ID number is not valid.");
    }
}
