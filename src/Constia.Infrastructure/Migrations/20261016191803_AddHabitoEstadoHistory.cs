using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Constia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHabitoEstadoHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InicializacionEstadoHabito",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    CutoverUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SnapshotCapturedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpectedLegacyHabitCount = table.Column<int>(type: "int", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InicializacionEstadoHabito", x => x.Id);
                    table.CheckConstraint("CK_InicializacionEstadoHabito_CompletionRequiresSnapshot", "[CompletedAtUtc] IS NULL OR [SnapshotCapturedAtUtc] IS NOT NULL");
                    table.CheckConstraint("CK_InicializacionEstadoHabito_Singleton", "[Id] = 1");
                    table.CheckConstraint("CK_InicializacionEstadoHabito_SnapshotCount", "([SnapshotCapturedAtUtc] IS NULL AND [ExpectedLegacyHabitCount] IS NULL) OR ([SnapshotCapturedAtUtc] IS NOT NULL AND [ExpectedLegacyHabitCount] IS NOT NULL AND [ExpectedLegacyHabitCount] >= 0)");
                });

            migrationBuilder.CreateTable(
                name: "PeriodoEstadoHabito",
                columns: table => new
                {
                    HabitoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VigenteDesde = table.Column<DateOnly>(type: "date", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    VigenteHasta = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodoEstadoHabito", x => new { x.HabitoId, x.VigenteDesde });
                    table.CheckConstraint("CK_PeriodoEstadoHabito_Vigencia", "[VigenteHasta] IS NULL OR [VigenteHasta] > [VigenteDesde]");
                    table.ForeignKey(
                        name: "FK_PeriodoEstadoHabito_Habito_HabitoId",
                        column: x => x.HabitoId,
                        principalTable: "Habito",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SnapshotEstadoHabito",
                columns: table => new
                {
                    HabitoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FechaCorteLocal = table.Column<DateOnly>(type: "date", nullable: false),
                    FechaInicio = table.Column<DateOnly>(type: "date", nullable: false),
                    EstadoAlCorte = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnapshotEstadoHabito", x => x.HabitoId);
                    table.CheckConstraint("CK_SnapshotEstadoHabito_Estado", "[EstadoAlCorte] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_SnapshotEstadoHabito_Habito_HabitoId",
                        column: x => x.HabitoId,
                        principalTable: "Habito",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PeriodoEstadoHabito_HabitoId",
                table: "PeriodoEstadoHabito",
                column: "HabitoId",
                unique: true,
                filter: "[VigenteHasta] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InicializacionEstadoHabito");

            migrationBuilder.DropTable(
                name: "PeriodoEstadoHabito");

            migrationBuilder.DropTable(
                name: "SnapshotEstadoHabito");
        }
    }
}
