using Domain.Users;
using FluentValidation;

namespace Web.Api.Features.Users.SetIdNumber;

internal sealed class SetCustomerIdNumberCommandValidator : AbstractValidator<SetCustomerIdNumberCommand>
{
    public SetCustomerIdNumberCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();

        // Same full SA ID validation as registration (13 digits, valid DOB, Luhn check digit).
        RuleFor(c => c.SouthAfricanIdNumber)
            .NotEmpty()
            .Must(SouthAfricanIdValidator.IsValid)
            .WithMessage("South African ID number is not valid.");
    }
}
