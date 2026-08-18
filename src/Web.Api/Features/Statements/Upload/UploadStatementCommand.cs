using Application.Abstractions.Messaging;

namespace Web.Api.Features.Statements.Upload;

// The single ingestion funnel for a statement, regardless of adapter: the admin multipart upload,
// the M2M push endpoint, and the object-storage event worker all build this same command. The
// actor is passed explicitly (UploadedByPrincipalId) so the handler never reaches for an HTTP user
// context — a machine ingestion path has none.
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
