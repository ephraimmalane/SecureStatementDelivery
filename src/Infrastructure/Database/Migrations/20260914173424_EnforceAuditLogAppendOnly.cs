using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class EnforceAuditLogAppendOnly : Migration
    {
        // Make the audit trail append-only at the database, not just by convention. A BEFORE
        // UPDATE/DELETE trigger raises on any mutation and — unlike GRANT/REVOKE, which the table
        // owner bypasses — applies to every role including the application's. The app only ever
        // INSERTs audit rows, so this never interferes with normal operation.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.audit_logs_prevent_mutation()
                RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_logs is append-only: % is not permitted', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER audit_logs_no_update_delete
                BEFORE UPDATE OR DELETE ON public.audit_logs
                FOR EACH ROW EXECUTE FUNCTION public.audit_logs_prevent_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS audit_logs_no_update_delete ON public.audit_logs;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS public.audit_logs_prevent_mutation();");
        }
    }
}
