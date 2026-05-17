using Application.Abstractions.Messaging;

namespace Application.Statements.Download;

public sealed record DownloadStatementQuery(
    string Token,
    string? IpAddress = null,
    string? UserAgent = null) : IQuery<StatementFileResponse>;

public sealed class StatementFileResponse
{
    // Pre-signed URI path: client follows HTTP 302 and downloads directly from S3.
    public StatementFileResponse(Uri redirectUri)
    {
        RedirectUri = redirectUri;
    }

    // Stream path: used when the storage provider does not support pre-signed URIs (Local).
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
