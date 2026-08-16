using Application.Abstractions.Messaging;
using Web.Api.Features.Statements.Download;

namespace Web.Api.Features.Statements.DownloadInApp;

// Authenticated, in-session download. The caller's JWT is the credential (no separate
// download token), modelling the "log into the app and download your statement" pattern
// that is the primary delivery channel for regulated statements.
public sealed record DownloadInAppQuery(
    Guid StatementId,
    string? IpAddress = null,
    string? UserAgent = null) : IQuery<StatementFileResponse>;
