namespace Application.Admin.AuditLogs;

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
