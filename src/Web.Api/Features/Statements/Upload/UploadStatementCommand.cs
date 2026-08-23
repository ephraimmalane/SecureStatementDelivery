using Application.Abstractions.Messaging;

namespace Web.Api.Features.Statements.Upload;

public sealed record UploadStatementCommand(
    Guid CustomerId,
    string OriginalFileName,
    Stream FileContent,
    string ContentType,
    string Period,
    string Description,
    Guid UploadedByPrincipalId,
    string? DocumentId = null) : ICommand<Guid>
{
    public override string ToString() =>
        $"UploadStatementCommand {{ CustomerId = {CustomerId}, OriginalFileName = {OriginalFileName}, " +
        $"Period = {Period}, UploadedByPrincipalId = {UploadedByPrincipalId} }}";
}
