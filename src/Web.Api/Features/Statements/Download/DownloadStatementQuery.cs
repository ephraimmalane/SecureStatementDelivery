using Application.Abstractions.Messaging;

namespace Web.Api.Features.Statements.Download;

public sealed record DownloadStatementQuery(
    string Token,
    string? IpAddress = null,
    string? UserAgent = null) : IQuery<StatementFileResponse>;

public sealed class StatementFileResponse
{
    public StatementFileResponse(Uri redirectUri)
    {
        RedirectUri = redirectUri;
    }

    public StatementFileResponse(Stream fileStream, string contentType, string fileName)
    {
        FileStream = fileStream;
        ContentType = contentType;
        FileName = fileName;
    }

    public Uri? RedirectUri { get; }
    public Stream? FileStream { get; }
    public string? ContentType { get; }
    public string? FileName { get; }
}
