using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Constia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHabitCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoriaId",
                table: "Habito",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CategoriaHabito",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoriaHabito", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "CategoriaHabito",
                columns: new[] { "Id", "Nombre", "Orden" },
                values: new object[,]
                {
                    { new Guid("82c0b8e6-0b9d-4e18-9bf8-000000000001"), "Salud y movimiento", 1 },
                    { new Guid("82c0b8e6-0b9d-4e18-9bf8-000000000002"), "Alimentación e hidratación", 2 },
                    { new Guid("82c0b8e6-0b9d-4e18-9bf8-000000000003"), "Descanso y sueño", 3 },
                    { new Guid("82c0b8e6-0b9d-4e18-9bf8-000000000004"), "Bienestar emocional", 4 },
                    { new Guid("82c0b8e6-0b9d-4e18-9bf8-000000000005"), "Aprendizaje y crecimiento", 5 },
                    { new Guid("82c0b8e6-0b9d-4e18-9bf8-000000000006"), "Productividad y organización", 6 },
                    { new Guid("82c0b8e6-0b9d-4e18-9bf8-000000000007"), "Relaciones y vínculos", 7 },
                    { new Guid("82c0b8e6-0b9d-4e18-9bf8-000000000008"), "Finanzas personales", 8 },
                    { new Guid("82c0b8e6-0b9d-4e18-9bf8-000000000009"), "Creatividad y ocio", 9 },
                    { new Guid("82c0b8e6-0b9d-4e18-9bf8-000000000010"), "Hogar y vida cotidiana", 10 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Habito_CategoriaId",
                table: "Habito",
                column: "CategoriaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Habito_CategoriaHabito_CategoriaId",
                table: "Habito",
                column: "CategoriaId",
                principalTable: "CategoriaHabito",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Habito] WITH (TABLOCKX, HOLDLOCK) WHERE [CategoriaId] IS NOT NULL)
                BEGIN
                    THROW 51020, N'CAT-02: no se puede revertir mientras existan hábitos con categoría asignada. Revise las asignaciones antes del rollback.', 1;
                END;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Habito_CategoriaHabito_CategoriaId",
                table: "Habito");

            migrationBuilder.DropTable(
                name: "CategoriaHabito");

            migrationBuilder.DropIndex(
                name: "IX_Habito_CategoriaId",
                table: "Habito");

            migrationBuilder.DropColumn(
                name: "CategoriaId",
                table: "Habito");
        }
    }
}
