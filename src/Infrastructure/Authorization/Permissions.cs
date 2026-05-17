namespace Infrastructure.Authorization;

public static class Permissions
{
    public const string StatementsUpload = "Statements.Upload";
    public const string StatementsReadAny = "Statements.ReadAny";
    public const string StatementsReadOwn = "Statements.ReadOwn";
    public const string StatementsRevoke = "Statements.Revoke";
    public const string StatementsDownload = "Statements.Download";
    public const string AdminAuditLogs = "Admin.AuditLogs";
    public const string AdminUsers = "Admin.Users";
}
