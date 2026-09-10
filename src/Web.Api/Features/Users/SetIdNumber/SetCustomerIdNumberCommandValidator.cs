using Domain.Users.Identity;
using FluentValidation;

namespace Web.Api.Features.Users.SetIdNumber;

internal sealed class SetCustomerIdNumberCommandValidator : AbstractValidator<SetCustomerIdNumberCommand>
{
    public SetCustomerIdNumberCommandValidator(IIdentityDocumentValidatorResolver identityDocuments)
    {
        RuleFor(c => c.CustomerId).NotEmpty();

        RuleFor(c => c.SouthAfricanIdNumber)
            .NotEmpty()
            .Must(identityDocuments.Resolve(IdentityDocumentType.SouthAfricanId).IsValid)
            .WithMessage("South African ID number is not valid.");
    }
}
