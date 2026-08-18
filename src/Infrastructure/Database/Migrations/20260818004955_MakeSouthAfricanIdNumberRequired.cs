using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class MakeSouthAfricanIdNumberRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fail loudly rather than silently backfilling existing NULLs with an (invalid,
            // unencrypted) empty string. Every customer must have a real SA ID on file before this
            // column becomes NOT NULL; if any NULLs remain, abort so an operator can backfill first.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM public.users WHERE south_african_id_number IS NULL) THEN
                        RAISE EXCEPTION
                            'Cannot make south_african_id_number NOT NULL: % row(s) still have a NULL value. Backfill valid SA ID numbers before applying this migration.',
                            (SELECT COUNT(*) FROM public.users WHERE south_african_id_number IS NULL);
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "south_african_id_number",
                schema: "public",
                table: "users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "south_african_id_number",
                schema: "public",
                table: "users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);
        }
    }
}
