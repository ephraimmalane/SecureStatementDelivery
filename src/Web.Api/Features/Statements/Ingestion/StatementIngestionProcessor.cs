using Application.Abstractions.Ingestion;
using Application.Abstractions.Messaging;
using Domain.Statements;
using SharedKernel;
using Web.Api.Features.Statements.Upload;

namespace Web.Api.Features.Statements.Ingestion;

// Turns one pulled ingestion message into a statement by driving the same funnel the HTTP upload
// and M2M push endpoints use. Kept separate from the worker loop so the per-message logic is unit
// testable without a running BackgroundService, and so it can be resolved in a fresh DI scope per
// message (the funnel handler and its DbContext are scoped).
public sealed class StatementIngestionProcessor(
    ICommandHandler<UploadStatementCommand, Guid> handler)
{
    public async Task<Result<Guid>> ProcessAsync(
        StatementIngestionMessage message,
        CancellationToken cancellationToken)
    {
        await using Stream content = await message.OpenContentAsync(cancellationToken);

        var command = new UploadStatementCommand(
            message.CustomerId,
            message.FileName,
            content,
            message.ContentType,
            message.Period,
            message.Description ?? string.Empty,
            SystemPrincipals.StatementIngestionService,
            message.IdempotencyKey);

        return await handler.Handle(command, cancellationToken);
    }
}
