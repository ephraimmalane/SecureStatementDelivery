namespace Application.Abstractions.Ingestion;

public interface IStatementIngestionSource
{
    Task<IReadOnlyList<StatementIngestionMessage>> ReceiveAsync(CancellationToken cancellationToken);

    Task AcknowledgeAsync(StatementIngestionMessage message, CancellationToken cancellationToken);
}
