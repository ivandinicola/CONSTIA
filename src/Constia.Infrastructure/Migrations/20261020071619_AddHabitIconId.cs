using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Constia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHabitIconId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IconoId",
                table: "Habito",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Habito] WITH (TABLOCKX, HOLDLOCK) WHERE [IconoId] IS NOT NULL)
                BEGIN
                    THROW 51021, N'HAB-CAT-01: no se puede revertir mientras existan hábitos con ícono asignado. Revise las asignaciones antes del rollback.', 1;
                END;
                """);

            migrationBuilder.DropColumn(
                name: "IconoId",
                table: "Habito");
        }
    }
}
