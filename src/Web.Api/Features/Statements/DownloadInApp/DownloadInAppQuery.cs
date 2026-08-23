using Application.Abstractions.Messaging;
using Web.Api.Features.Statements.Download;

namespace Web.Api.Features.Statements.DownloadInApp;

public sealed record DownloadInAppQuery(
    Guid StatementId,
    string? IpAddress = null,
    string? UserAgent = null) : IQuery<StatementFileResponse>;
