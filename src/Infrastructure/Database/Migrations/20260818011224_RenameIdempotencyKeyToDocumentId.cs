using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class RenameIdempotencyKeyToDocumentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_statements_idempotency_key",
                schema: "public",
                table: "statements");

            migrationBuilder.RenameColumn(
                name: "idempotency_key",
                schema: "public",
                table: "statements",
                newName: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_statements_customer_id_document_id",
                schema: "public",
                table: "statements",
                columns: new[] { "customer_id", "document_id" },
                unique: true,
                filter: "document_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_statements_customer_id_document_id",
                schema: "public",
                table: "statements");

            migrationBuilder.RenameColumn(
                name: "document_id",
                schema: "public",
                table: "statements",
                newName: "idempotency_key");

            migrationBuilder.CreateIndex(
                name: "ix_statements_idempotency_key",
                schema: "public",
                table: "statements",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }
    }
}
