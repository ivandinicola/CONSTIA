using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Constia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VersionarProgramacionHabitos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProgramacionHabito",
                columns: table => new
                {
                    VigenteDesde = table.Column<DateOnly>(type: "date", nullable: false),
                    HabitoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VigenteHasta = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramacionHabito", x => new { x.HabitoId, x.VigenteDesde });
                    table.CheckConstraint("CK_ProgramacionHabito_Vigencia", "[VigenteHasta] IS NULL OR [VigenteHasta] > [VigenteDesde]");
                    table.ForeignKey(
                        name: "FK_ProgramacionHabito_Habito_HabitoId",
                        column: x => x.HabitoId,
                        principalTable: "Habito",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProgramacionHabitoDia",
                columns: table => new
                {
                    Dia = table.Column<int>(type: "int", nullable: false),
                    HabitoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VigenteDesde = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramacionHabitoDia", x => new { x.HabitoId, x.VigenteDesde, x.Dia });
                    table.ForeignKey(
                        name: "FK_ProgramacionHabitoDia_ProgramacionHabito_HabitoId_VigenteDesde",
                        columns: x => new { x.HabitoId, x.VigenteDesde },
                        principalTable: "ProgramacionHabito",
                        principalColumns: new[] { "HabitoId", "VigenteDesde" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProgramacionHabito_HabitoId",
                table: "ProgramacionHabito",
                column: "HabitoId",
                unique: true,
                filter: "[VigenteHasta] IS NULL");

            migrationBuilder.Sql(
                """
                IF EXISTS
                (
                    SELECT h.[Id]
                    FROM [Habito] AS h
                    LEFT JOIN [HabitoDiaProgramado] AS d ON d.[HabitoId] = h.[Id]
                    GROUP BY h.[Id]
                    HAVING COUNT(d.[Dia]) = 0
                )
                BEGIN
                    THROW 51000, N'No se puede versionar un hábito sin días programados.', 1;
                END;

                INSERT INTO [ProgramacionHabito] ([HabitoId], [VigenteDesde], [VigenteHasta])
                SELECT h.[Id], h.[StartDate], NULL
                FROM [Habito] AS h;

                INSERT INTO [ProgramacionHabitoDia] ([HabitoId], [VigenteDesde], [Dia])
                SELECT d.[HabitoId], h.[StartDate], d.[Dia]
                FROM [HabitoDiaProgramado] AS d
                INNER JOIN [Habito] AS h ON h.[Id] = d.[HabitoId];

                IF (SELECT COUNT_BIG(*) FROM [ProgramacionHabito])
                    <> (SELECT COUNT_BIG(*) FROM [Habito])
                BEGIN
                    THROW 51001, N'La cantidad de versiones iniciales no coincide con la cantidad de hábitos.', 1;
                END;

                IF (SELECT COUNT_BIG(*) FROM [ProgramacionHabitoDia])
                    <> (SELECT COUNT_BIG(*) FROM [HabitoDiaProgramado])
                BEGIN
                    THROW 51002, N'La cantidad de días copiados no coincide con la tabla anterior.', 1;
                END;

                IF EXISTS
                (
                    SELECT [HabitoId], [VigenteDesde], [Dia]
                    FROM [ProgramacionHabitoDia]
                    EXCEPT
                    SELECT d.[HabitoId], h.[StartDate], d.[Dia]
                    FROM [HabitoDiaProgramado] AS d
                    INNER JOIN [Habito] AS h ON h.[Id] = d.[HabitoId]
                )
                OR EXISTS
                (
                    SELECT d.[HabitoId], h.[StartDate], d.[Dia]
                    FROM [HabitoDiaProgramado] AS d
                    INNER JOIN [Habito] AS h ON h.[Id] = d.[HabitoId]
                    EXCEPT
                    SELECT [HabitoId], [VigenteDesde], [Dia]
                    FROM [ProgramacionHabitoDia]
                )
                BEGIN
                    THROW 51003, N'La validación de los días programados copiados falló.', 1;
                END;

                IF EXISTS
                (
                    SELECT [HabitoId], [VigenteDesde]
                    FROM [ProgramacionHabito]
                    WHERE [VigenteHasta] IS NULL
                    EXCEPT
                    SELECT [Id], [StartDate]
                    FROM [Habito]
                )
                OR EXISTS
                (
                    SELECT [Id], [StartDate]
                    FROM [Habito]
                    EXCEPT
                    SELECT [HabitoId], [VigenteDesde]
                    FROM [ProgramacionHabito]
                    WHERE [VigenteHasta] IS NULL
                )
                BEGIN
                    THROW 51004, N'La validación de las versiones iniciales copiadas falló.', 1;
                END;

                DROP TABLE [HabitoDiaProgramado];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // TEMP-004 puede agregar versiones que la tabla histórica anterior no puede representar.
            // En ese caso se bloquea el rollback para evitar perder el historial silenciosamente.
            migrationBuilder.Sql(
                """
                IF EXISTS
                (
                    SELECT h.[Id]
                    FROM [Habito] AS h
                    LEFT JOIN [ProgramacionHabito] AS p ON p.[HabitoId] = h.[Id]
                    GROUP BY h.[Id]
                    HAVING COUNT(p.[VigenteDesde]) <> 1
                        OR SUM(CASE WHEN p.[VigenteHasta] IS NULL THEN 1 ELSE 0 END) <> 1
                )
                BEGIN
                    THROW 51005, N'No se puede revertir la programación: existen hábitos sin una única versión abierta.', 1;
                END;

                IF EXISTS
                (
                    SELECT p.[HabitoId], p.[VigenteDesde]
                    FROM [ProgramacionHabito] AS p
                    LEFT JOIN [ProgramacionHabitoDia] AS d
                        ON d.[HabitoId] = p.[HabitoId]
                        AND d.[VigenteDesde] = p.[VigenteDesde]
                    GROUP BY p.[HabitoId], p.[VigenteDesde]
                    HAVING COUNT(d.[Dia]) = 0
                )
                BEGIN
                    THROW 51006, N'No se puede revertir la programación: existe una versión sin días programados.', 1;
                END;
                """);

            migrationBuilder.CreateTable(
                name: "HabitoDiaProgramado",
                columns: table => new
                {
                    HabitoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Dia = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HabitoDiaProgramado", x => new { x.HabitoId, x.Dia });
                    table.ForeignKey(
                        name: "FK_HabitoDiaProgramado_Habito_HabitoId",
                        column: x => x.HabitoId,
                        principalTable: "Habito",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO [HabitoDiaProgramado] ([HabitoId], [Dia])
                SELECT d.[HabitoId], d.[Dia]
                FROM [ProgramacionHabitoDia] AS d
                INNER JOIN [ProgramacionHabito] AS p
                    ON p.[HabitoId] = d.[HabitoId]
                    AND p.[VigenteDesde] = d.[VigenteDesde]
                WHERE p.[VigenteHasta] IS NULL;
                """);

            migrationBuilder.DropTable(
                name: "ProgramacionHabitoDia");

            migrationBuilder.DropTable(
                name: "ProgramacionHabito");
        }
    }
}
