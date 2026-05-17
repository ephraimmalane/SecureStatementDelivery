namespace Domain.AuditLogs;

public sealed class DownloadAuditLog
{
    private DownloadAuditLog() { }

    public Guid Id { get; private set; }
    public Guid StatementId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? DownloadTokenId { get; private set; }
    public AuditAction Action { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public string? AdditionalData { get; private set; }

    public static DownloadAuditLog Create(
        Guid statementId,
        Guid userId,
        AuditAction action,
        Guid? downloadTokenId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? additionalData = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            StatementId = statementId,
            UserId = userId,
            DownloadTokenId = downloadTokenId,
            Action = action,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            OccurredAt = DateTime.UtcNow,
            AdditionalData = additionalData
        };
}
