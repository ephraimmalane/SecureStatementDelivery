namespace Application.Abstractions.Observability;

/// <summary>
/// Emits domain metrics for statement processing. Implemented in Infrastructure over the
/// System.Diagnostics.Metrics meter; handlers depend only on this abstraction.
/// </summary>
public interface IStatementMetrics
{
    void StatementUploaded();

    void StatementRevoked();

    void UploadRejected(string reason);

    void OutboxProcessed();

    void OutboxFailed();
}
