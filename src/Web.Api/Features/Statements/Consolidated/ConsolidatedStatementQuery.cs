using Application.Abstractions.Messaging;
using Web.Api.Features.Statements.Download;

namespace Web.Api.Features.Statements.Consolidated;

public sealed record ConsolidatedStatementQuery(
    string From,
    string To,
    Guid? CustomerId = null,
    string? IpAddress = null,
    string? UserAgent = null) : IQuery<StatementFileResponse>;
