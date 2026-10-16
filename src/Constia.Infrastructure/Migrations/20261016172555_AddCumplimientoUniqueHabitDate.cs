using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Constia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCumplimientoUniqueHabitDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM [Cumplimiento]
                    GROUP BY [HabitoId], [Fecha]
                    HAVING COUNT_BIG(*) > 1
                )
                BEGIN
                    ;THROW 51005, 'Cannot enforce unique habit completion dates because duplicate rows already exist. Review the data before retrying this migration.', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Cumplimiento_HabitoId",
                table: "Cumplimiento");

            migrationBuilder.CreateIndex(
                name: "UX_Cumplimiento_HabitoId_Fecha",
                table: "Cumplimiento",
                columns: new[] { "HabitoId", "Fecha" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Cumplimiento_HabitoId_Fecha",
                table: "Cumplimiento");

            migrationBuilder.CreateIndex(
                name: "IX_Cumplimiento_HabitoId",
                table: "Cumplimiento",
                column: "HabitoId");
        }
    }
}
