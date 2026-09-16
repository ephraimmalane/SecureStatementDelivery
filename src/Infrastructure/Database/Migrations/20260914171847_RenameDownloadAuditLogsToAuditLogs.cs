using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class RenameDownloadAuditLogsToAuditLogs : Migration
    {
        // Rename the audit table in place rather than drop/recreate: this is an append-only audit
        // trail and its rows must be preserved. Renames the table, its primary-key constraint and all
        // four indexes so the resulting schema is identical to a fresh create under the new name.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "download_audit_logs",
                schema: "public",
                newName: "audit_logs",
                newSchema: "public");

            migrationBuilder.Sql(
                "ALTER TABLE public.audit_logs RENAME CONSTRAINT pk_download_audit_logs TO pk_audit_logs;");

            migrationBuilder.RenameIndex(
                name: "ix_download_audit_logs_action",
                schema: "public",
                table: "audit_logs",
                newName: "ix_audit_logs_action");

            migrationBuilder.RenameIndex(
                name: "ix_download_audit_logs_occurred_at",
                schema: "public",
                table: "audit_logs",
                newName: "ix_audit_logs_occurred_at");

            migrationBuilder.RenameIndex(
                name: "ix_download_audit_logs_statement_id",
                schema: "public",
                table: "audit_logs",
                newName: "ix_audit_logs_statement_id");

            migrationBuilder.RenameIndex(
                name: "ix_download_audit_logs_user_id",
                schema: "public",
                table: "audit_logs",
                newName: "ix_audit_logs_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ix_audit_logs_action",
                schema: "public",
                table: "audit_logs",
                newName: "ix_download_audit_logs_action");

            migrationBuilder.RenameIndex(
                name: "ix_audit_logs_occurred_at",
                schema: "public",
                table: "audit_logs",
                newName: "ix_download_audit_logs_occurred_at");

            migrationBuilder.RenameIndex(
                name: "ix_audit_logs_statement_id",
                schema: "public",
                table: "audit_logs",
                newName: "ix_download_audit_logs_statement_id");

            migrationBuilder.RenameIndex(
                name: "ix_audit_logs_user_id",
                schema: "public",
                table: "audit_logs",
                newName: "ix_download_audit_logs_user_id");

            migrationBuilder.Sql(
                "ALTER TABLE public.audit_logs RENAME CONSTRAINT pk_audit_logs TO pk_download_audit_logs;");

            migrationBuilder.RenameTable(
                name: "audit_logs",
                schema: "public",
                newName: "download_audit_logs",
                newSchema: "public");
        }
    }
}
