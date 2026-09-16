using System.Diagnostics.Metrics;
using Application.Abstractions.Observability;

namespace Infrastructure.Observability;

public sealed class StatementMetrics : IStatementMetrics
{
    public const string MeterName = "SecureStatementDelivery";

#pragma warning disable S1450
    private readonly Meter _meter;
#pragma warning restore S1450
    private readonly Counter<long> _uploaded;
    private readonly Counter<long> _revoked;
    private readonly Counter<long> _uploadRejected;
    private readonly Counter<long> _outboxProcessed;
    private readonly Counter<long> _outboxFailed;

    public StatementMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _uploaded = _meter.CreateCounter<long>(
            "statements.uploaded", description: "Statements successfully ingested.");
        _revoked = _meter.CreateCounter<long>(
            "statements.revoked", description: "Statements revoked by an administrator.");
        _uploadRejected = _meter.CreateCounter<long>(
            "statements.upload_rejected", description: "Uploads rejected before persistence.");
        _outboxProcessed = _meter.CreateCounter<long>(
            "outbox.processed", description: "Outbox messages dispatched successfully.");
        _outboxFailed = _meter.CreateCounter<long>(
            "outbox.failed", description: "Outbox messages that errored during dispatch.");
    }

    public void StatementUploaded() => _uploaded.Add(1);

    public void StatementRevoked() => _revoked.Add(1);

    public void UploadRejected(string reason) =>
        _uploadRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void OutboxProcessed() => _outboxProcessed.Add(1);

    public void OutboxFailed() => _outboxFailed.Add(1);
}
