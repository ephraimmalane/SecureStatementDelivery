using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "public");

        migrationBuilder.CreateTable(
            name: "roles",
            schema: "public",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
            },
            constraints: table => table.PrimaryKey("pk_roles", x => x.id));

        migrationBuilder.InsertData(
            schema: "public",
            table: "roles",
            columns: ["id", "name"],
            values: new object[] { 1, "Admin" });

        migrationBuilder.InsertData(
            schema: "public",
            table: "roles",
            columns: ["id", "name"],
            values: new object[] { 2, "Customer" });

        migrationBuilder.CreateTable(
            name: "users",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                password_hash = table.Column<string>(type: "text", nullable: false),
                role_id = table.Column<int>(type: "integer", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_users", x => x.id);
                table.ForeignKey(
                    name: "fk_users_roles_role_id",
                    column: x => x.role_id,
                    principalSchema: "public",
                    principalTable: "roles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "refresh_tokens",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                is_revoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_refresh_tokens", x => x.id);
                table.ForeignKey(
                    name: "fk_refresh_tokens_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "statements",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                uploaded_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                storage_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, defaultValue: ""),
                status = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                revoked_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                revoked_by_admin_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_statements", x => x.id);
                table.ForeignKey(
                    name: "fk_statements_users_customer_id",
                    column: x => x.customer_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_statements_users_uploaded_by_admin_id",
                    column: x => x.uploaded_by_admin_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "download_tokens",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                statement_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                is_used = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                is_single_use = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_download_tokens", x => x.id);
                table.ForeignKey(
                    name: "fk_download_tokens_statements_statement_id",
                    column: x => x.statement_id,
                    principalSchema: "public",
                    principalTable: "statements",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_download_tokens_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "download_audit_logs",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                statement_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                download_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                action = table.Column<int>(type: "integer", nullable: false),
                ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                additional_data = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_download_audit_logs", x => x.id);
                table.ForeignKey(
                    name: "fk_download_audit_logs_statements_statement_id",
                    column: x => x.statement_id,
                    principalSchema: "public",
                    principalTable: "statements",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_download_audit_logs_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_download_audit_logs_download_tokens_download_token_id",
                    column: x => x.download_token_id,
                    principalSchema: "public",
                    principalTable: "download_tokens",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex("ix_roles_name", "roles", "name", schema: "public", unique: true);
        migrationBuilder.CreateIndex("ix_users_email", "users", "email", schema: "public", unique: true);
        migrationBuilder.CreateIndex("ix_users_role_id", "users", "role_id", schema: "public");
        migrationBuilder.CreateIndex("ix_refresh_tokens_user_id", "refresh_tokens", "user_id", schema: "public");
        migrationBuilder.CreateIndex("ix_refresh_tokens_token_hash", "refresh_tokens", "token_hash", schema: "public");
        migrationBuilder.CreateIndex("ix_statements_customer_id", "statements", "customer_id", schema: "public");
        migrationBuilder.CreateIndex("ix_statements_uploaded_by_admin_id", "statements", "uploaded_by_admin_id", schema: "public");
        migrationBuilder.CreateIndex("ix_statements_period", "statements", "period", schema: "public");
        migrationBuilder.CreateIndex("ix_statements_status", "statements", "status", schema: "public");
        migrationBuilder.CreateIndex("ix_statements_customer_period", "statements", ["customer_id", "period"], schema: "public");
        migrationBuilder.CreateIndex("ix_download_tokens_token_hash", "download_tokens", "token_hash", schema: "public", unique: true);
        migrationBuilder.CreateIndex("ix_download_tokens_statement_id", "download_tokens", "statement_id", schema: "public");
        migrationBuilder.CreateIndex("ix_download_tokens_user_id", "download_tokens", "user_id", schema: "public");
        migrationBuilder.CreateIndex("ix_download_tokens_expires_at", "download_tokens", "expires_at", schema: "public");
        migrationBuilder.CreateIndex("ix_audit_logs_statement_id", "download_audit_logs", "statement_id", schema: "public");
        migrationBuilder.CreateIndex("ix_audit_logs_user_id", "download_audit_logs", "user_id", schema: "public");
        migrationBuilder.CreateIndex("ix_audit_logs_occurred_at", "download_audit_logs", "occurred_at", schema: "public");
        migrationBuilder.CreateIndex("ix_audit_logs_action", "download_audit_logs", "action", schema: "public");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "download_audit_logs", schema: "public");
        migrationBuilder.DropTable(name: "download_tokens", schema: "public");
        migrationBuilder.DropTable(name: "statements", schema: "public");
        migrationBuilder.DropTable(name: "refresh_tokens", schema: "public");
        migrationBuilder.DropTable(name: "users", schema: "public");
        migrationBuilder.DropTable(name: "roles", schema: "public");
    }
}
