using FluentValidation;

namespace Web.Api.Features.Statements.GenerateDownloadLink;

internal sealed class GenerateDownloadLinkCommandValidator : AbstractValidator<GenerateDownloadLinkCommand>
{
    public GenerateDownloadLinkCommandValidator()
    {
        RuleFor(c => c.StatementId).NotEmpty();

        RuleFor(c => c.ExpiryMinutes)
            .InclusiveBetween(1, 60)
            .WithMessage("Expiry must be between 1 and 60 minutes.");
    }
}
