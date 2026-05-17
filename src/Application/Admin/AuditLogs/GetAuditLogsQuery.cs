using Application.Abstractions.Messaging;

namespace Application.Admin.AuditLogs;

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
