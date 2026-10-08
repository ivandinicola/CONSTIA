using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Constia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUsuarioTimeZoneId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Usuario",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [Usuario] SET [TimeZoneId] = N'Etc/UTC' WHERE [TimeZoneId] IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "TimeZoneId",
                table: "Usuario",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Usuario");
        }
    }
}
