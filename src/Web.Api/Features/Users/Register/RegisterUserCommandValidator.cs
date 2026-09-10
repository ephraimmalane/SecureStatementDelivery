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
        RuleFor(c => c.Password).NotEmpty().MinimumLength(8);

        RuleFor(c => c.SouthAfricanIdNumber)
            .NotEmpty()
            .Must(identityDocuments.Resolve(IdentityDocumentType.SouthAfricanId).IsValid)
            .WithMessage("South African ID number is not valid.");
    }
}
