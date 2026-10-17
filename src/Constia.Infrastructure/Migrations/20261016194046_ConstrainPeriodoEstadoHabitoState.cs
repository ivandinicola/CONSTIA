using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Constia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConstrainPeriodoEstadoHabitoState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_PeriodoEstadoHabito_Estado",
                table: "PeriodoEstadoHabito",
                sql: "[Estado] IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PeriodoEstadoHabito_Estado",
                table: "PeriodoEstadoHabito");
        }
    }
}
