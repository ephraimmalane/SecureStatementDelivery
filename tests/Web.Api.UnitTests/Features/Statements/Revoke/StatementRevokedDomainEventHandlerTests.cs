using Application.Abstractions.Observability;
using Domain.Statements.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Web.Api.Features.Statements.Revoke;

namespace Web.Api.UnitTests.Features.Statements.Revoke;

public sealed class StatementRevokedDomainEventHandlerTests
{
    [Fact]
    public async Task Handle_Should_EmitStatementRevokedMetric_Once()
    {
        var metrics = new RecordingStatementMetrics();
        var handler = new StatementRevokedDomainEventHandler(
            metrics, NullLogger<StatementRevokedDomainEventHandler>.Instance);

        var domainEvent = new StatementRevokedDomainEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await handler.Handle(domainEvent, CancellationToken.None);

        metrics.RevokedCount.ShouldBe(1);
    }

    private sealed class RecordingStatementMetrics : IStatementMetrics
    {
        public int RevokedCount { get; private set; }

        public void StatementRevoked() => RevokedCount++;

        public void StatementUploaded()
        {
        }

        public void UploadRejected(string reason)
        {
        }

        public void OutboxProcessed()
        {
        }

        public void OutboxFailed()
        {
        }
    }
}
