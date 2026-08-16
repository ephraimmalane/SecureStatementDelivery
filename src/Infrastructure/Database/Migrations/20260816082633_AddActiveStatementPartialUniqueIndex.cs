using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveStatementPartialUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_statements_customer_id_period",
                schema: "public",
                table: "statements");

            migrationBuilder.CreateIndex(
                name: "ix_statements_customer_id_period_active",
                schema: "public",
                table: "statements",
                columns: new[] { "customer_id", "period" },
                unique: true,
                filter: "status <> 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_statements_customer_id_period_active",
                schema: "public",
                table: "statements");

            migrationBuilder.CreateIndex(
                name: "ix_statements_customer_id_period",
                schema: "public",
                table: "statements",
                columns: new[] { "customer_id", "period" });
        }
    }
}
