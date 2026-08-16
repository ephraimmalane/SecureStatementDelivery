using Application.Abstractions.Messaging;

namespace Web.Api.Features.Admin.AuditLogs;

public sealed record GetAuditLogsQuery(
    Guid? StatementId,
    Guid? UserId,
    string? Action,
    DateTime? From,
    DateTime? To,
    int Page = 1,
    int PageSize = 50) : IQuery<PagedAuditLogResponse>;

public sealed record PagedAuditLogResponse(
    IReadOnlyList<AuditLogResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record AuditLogResponse(
    Guid Id,
    Guid StatementId,
    Guid UserId,
    string UserName,
    Guid? DownloadTokenId,
    string Action,
    string? IpAddress,
    string? UserAgent,
    DateTime OccurredAt,
    string? AdditionalData);
