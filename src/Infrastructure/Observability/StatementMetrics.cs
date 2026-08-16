using System.Diagnostics.Metrics;

namespace Infrastructure.Observability;

// Application-defined metrics, surfaced via OpenTelemetry (OTLP + Prometheus /metrics). These are
// the signals worth alerting on beyond the built-in HTTP RED metrics: ingestion volume, rejected
// uploads, and outbox health (a rising failure/backlog is the earliest sign domain events — incl.
// customer notifications — are not being delivered).
public sealed class StatementMetrics
{
    public const string MeterName = "SecureStatementDelivery";

    // Held in a field because the Meter's lifetime is owned by the IMeterFactory (disposed when the
    // DI container is). Keeping the reference here also prevents premature collection.
#pragma warning disable S1450 // Intentional long-lived reference, not a local: keeps the Meter alive.
    private readonly Meter _meter;
#pragma warning restore S1450
    private readonly Counter<long> _uploaded;
    private readonly Counter<long> _uploadRejected;
    private readonly Counter<long> _outboxProcessed;
    private readonly Counter<long> _outboxFailed;

    public StatementMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _uploaded = _meter.CreateCounter<long>(
            "statements.uploaded", description: "Statements successfully ingested.");
        _uploadRejected = _meter.CreateCounter<long>(
            "statements.upload_rejected", description: "Uploads rejected before persistence.");
        _outboxProcessed = _meter.CreateCounter<long>(
            "outbox.processed", description: "Outbox messages dispatched successfully.");
        _outboxFailed = _meter.CreateCounter<long>(
            "outbox.failed", description: "Outbox messages that errored during dispatch.");
    }

    public void StatementUploaded() => _uploaded.Add(1);

    public void UploadRejected(string reason) =>
        _uploadRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void OutboxProcessed() => _outboxProcessed.Add(1);

    public void OutboxFailed() => _outboxFailed.Add(1);
}
