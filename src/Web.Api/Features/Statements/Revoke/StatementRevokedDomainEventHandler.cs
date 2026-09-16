using Application.Abstractions.Observability;
using Domain.Statements.Events;
using SharedKernel;

namespace Web.Api.Features.Statements.Revoke;

internal sealed class StatementRevokedDomainEventHandler(
    IStatementMetrics metrics,
    ILogger<StatementRevokedDomainEventHandler> logger)
    : IDomainEventHandler<StatementRevokedDomainEvent>
{
    public Task Handle(StatementRevokedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        metrics.StatementRevoked();

        logger.LogInformation(
            "Statement {StatementId} revoked for customer {CustomerId} by admin {RevokedByAdminId}.",
            domainEvent.StatementId,
            domainEvent.CustomerId,
            domainEvent.RevokedByAdminId);

        return Task.CompletedTask;
    }
}
