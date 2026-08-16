using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

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
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
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
                    is_password_protected = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
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
                });

            migrationBuilder.CreateIndex(
                name: "ix_download_audit_logs_action",
                schema: "public",
                table: "download_audit_logs",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "ix_download_audit_logs_occurred_at",
                schema: "public",
                table: "download_audit_logs",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_download_audit_logs_statement_id",
                schema: "public",
                table: "download_audit_logs",
                column: "statement_id");

            migrationBuilder.CreateIndex(
                name: "ix_download_audit_logs_user_id",
                schema: "public",
                table: "download_audit_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_download_tokens_expires_at",
                schema: "public",
                table: "download_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_download_tokens_statement_id",
                schema: "public",
                table: "download_tokens",
                column: "statement_id");

            migrationBuilder.CreateIndex(
                name: "ix_download_tokens_token_hash",
                schema: "public",
                table: "download_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_download_tokens_user_id",
                schema: "public",
                table: "download_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_statements_customer_id",
                schema: "public",
                table: "statements",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_statements_customer_id_period",
                schema: "public",
                table: "statements",
                columns: new[] { "customer_id", "period" });

            migrationBuilder.CreateIndex(
                name: "ix_statements_period",
                schema: "public",
                table: "statements",
                column: "period");

            migrationBuilder.CreateIndex(
                name: "ix_statements_status",
                schema: "public",
                table: "statements",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_statements_uploaded_by_admin_id",
                schema: "public",
                table: "statements",
                column: "uploaded_by_admin_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "public",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "download_audit_logs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "download_tokens",
                schema: "public");

            migrationBuilder.DropTable(
                name: "statements",
                schema: "public");

            migrationBuilder.DropTable(
                name: "users",
                schema: "public");
        }
    }
}
