using Application.Abstractions.Messaging;

namespace Application.Statements.Upload;

public sealed record UploadStatementCommand(
    Guid CustomerId,
    string OriginalFileName,
    Stream FileContent,
    string ContentType,
    string Period,
    string Description) : ICommand<Guid>;
