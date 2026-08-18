using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddStatementContentHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "content_hash",
                schema: "public",
                table: "statements",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_statements_customer_id_period_content_hash",
                schema: "public",
                table: "statements",
                columns: new[] { "customer_id", "period", "content_hash" },
                unique: true,
                filter: "content_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_statements_customer_id_period_content_hash",
                schema: "public",
                table: "statements");

            migrationBuilder.DropColumn(
                name: "content_hash",
                schema: "public",
                table: "statements");
        }
    }
}
