using Application.Abstractions.Ingestion;
using Application.Abstractions.Messaging;
using Domain.Statements;
using SharedKernel;
using Web.Api.Features.Statements.Upload;

namespace Web.Api.Features.Statements.Ingestion;

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
            message.DocumentId);

        return await handler.Handle(command, cancellationToken);
    }
}
