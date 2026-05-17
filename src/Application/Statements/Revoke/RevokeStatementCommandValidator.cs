using FluentValidation;

namespace Application.Statements.Revoke;

internal sealed class RevokeStatementCommandValidator : AbstractValidator<RevokeStatementCommand>
{
    public RevokeStatementCommandValidator()
    {
        RuleFor(c => c.StatementId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
    }
}
