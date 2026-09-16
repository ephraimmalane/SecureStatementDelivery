namespace Domain.AuditLogs;

public enum AuditAction
{
    StatementUploaded = 1,
    StatementRevoked = 2,
    DownloadLinkGenerated = 3,

    StatementDownloaded = 4,

    DownloadFailed = 5,
    UnauthorizedAccess = 6,
    DownloadDenied = 7,

    DownloadAuthorized = 8,

    StatementAvailableNotified = 9,

    WelcomeNotified = 10
}
