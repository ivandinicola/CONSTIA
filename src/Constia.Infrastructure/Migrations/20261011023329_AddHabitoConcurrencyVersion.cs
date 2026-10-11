using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Constia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHabitoConcurrencyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyVersion",
                table: "Habito",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("UPDATE [Habito] SET [ConcurrencyVersion] = 1;");

            migrationBuilder.AlterColumn<long>(
                name: "ConcurrencyVersion",
                table: "Habito",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                table: "Habito");
        }
    }
}
