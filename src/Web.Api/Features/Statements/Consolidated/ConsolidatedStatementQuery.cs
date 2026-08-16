using Application.Abstractions.Messaging;
using Web.Api.Features.Statements.Download;

namespace Web.Api.Features.Statements.Consolidated;

// Consolidated multi-month statement: merges the caller's monthly statements in [From, To] into a
// single PDF (the "view 1 / 2 / 3 months" experience). CustomerId is honoured only for admins; a
// customer always gets their own. Reuses StatementFileResponse from the download feature.
public sealed record ConsolidatedStatementQuery(
    string From,
    string To,
    Guid? CustomerId = null,
    string? IpAddress = null,
    string? UserAgent = null) : IQuery<StatementFileResponse>;
