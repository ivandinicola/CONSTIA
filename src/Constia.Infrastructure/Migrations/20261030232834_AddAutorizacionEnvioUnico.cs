using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Constia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAutorizacionEnvioUnico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutorizacionEnvioUnico",
                columns: table => new
                {
                    EnsayoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperacionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    CreadaEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumidaEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResultadoEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IdentidadDestinatario = table.Column<byte[]>(type: "binary(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutorizacionEnvioUnico", x => x.EnsayoId);
                    table.CheckConstraint("CK_AutorizacionEnvio_Estado", "([Estado] = 0 AND [ConsumidaEnUtc] IS NULL AND [ResultadoEnUtc] IS NULL) OR ([Estado] = 1 AND [ConsumidaEnUtc] IS NOT NULL AND [ResultadoEnUtc] IS NULL) OR ([Estado] IN (2,3) AND [ConsumidaEnUtc] IS NOT NULL AND [ResultadoEnUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_AutorizacionEnvio_Fechas", "DATEPART(TZOFFSET,[CreadaEnUtc]) = 0 AND ([ConsumidaEnUtc] IS NULL OR (DATEPART(TZOFFSET,[ConsumidaEnUtc]) = 0 AND [ConsumidaEnUtc] >= [CreadaEnUtc])) AND ([ResultadoEnUtc] IS NULL OR (DATEPART(TZOFFSET,[ResultadoEnUtc]) = 0 AND [ResultadoEnUtc] >= [ConsumidaEnUtc]))");
                    table.CheckConstraint("CK_AutorizacionEnvio_Identidad", "DATALENGTH([IdentidadDestinatario]) = 32");
                    table.CheckConstraint("CK_AutorizacionEnvio_Ids", "[EnsayoId] <> '00000000-0000-0000-0000-000000000000' AND [OperacionId] <> '00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutorizacionEnvioUnico_OperacionId",
                table: "AutorizacionEnvioUnico",
                column: "OperacionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF @@TRANCOUNT = 0 OR XACT_STATE() <> 1
                    THROW 51001, 'Authorization rollback requires an active committable transaction.', 1;

                IF EXISTS (SELECT 1 FROM [AutorizacionEnvioUnico] WITH (TABLOCKX, HOLDLOCK))
                    THROW 51000, 'Cannot discard authorization history.', 1;
                """);

            migrationBuilder.DropTable(
                name: "AutorizacionEnvioUnico");
        }
    }
}
