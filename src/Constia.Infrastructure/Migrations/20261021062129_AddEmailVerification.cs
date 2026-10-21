using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Constia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Corte bajo la transacción de esta migración. El runbook debe detener TODAS las APIs antiguas.
            migrationBuilder.Sql("""
                DECLARE @filas bigint;
                SELECT @filas = COUNT_BIG(*) FROM [Usuario] WITH (TABLOCKX, HOLDLOCK);
                """);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailVerificadoEnUtc",
                table: "Usuario",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "EstadoVerificacionCorreo",
                table: "Usuario",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<byte[]>(
                name: "VersionCorreo",
                table: "Usuario",
                type: "rowversion",
                rowVersion: true,
                nullable: true);


            migrationBuilder.Sql("""
                UPDATE [Usuario] SET [EstadoVerificacionCorreo] = 0;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_Usuario_ProtegeVerificacionCorreo]
                ON [Usuario] AFTER INSERT, UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.[Id] = i.[Id]
                        WHERE d.[Id] IS NULL AND i.[EstadoVerificacionCorreo] <> 1
                    )
                        THROW 51031, N'Las cuentas nuevas deben nacer pendientes.', 1;
                    IF EXISTS (
                        SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
                        WHERE (i.[EstadoVerificacionCorreo] <> d.[EstadoVerificacionCorreo]
                            AND NOT (d.[EstadoVerificacionCorreo] = 1 AND i.[EstadoVerificacionCorreo] = 2))
                        OR (d.[EstadoVerificacionCorreo] = 2 AND
                            (i.[EmailVerificadoEnUtc] IS NULL OR i.[EmailVerificadoEnUtc] <> d.[EmailVerificadoEnUtc]))
                    )
                        THROW 51032, N'Transición de verificación no permitida.', 1;
                END;
                """);

            migrationBuilder.CreateTable(
                name: "DesafioVerificacionCorreo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailDestino = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Proposito = table.Column<byte>(type: "tinyint", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    EmitidoEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiraEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumidoEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    InvalidadoEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VentanaIntentosEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IntentosConfirmacion = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesafioVerificacionCorreo", x => x.Id);
                    table.CheckConstraint("CK_DesafioCorreo_Consumo", "[ConsumidoEnUtc] IS NULL OR ([InvalidadoEnUtc] IS NULL AND DATEPART(TZOFFSET,[ConsumidoEnUtc]) = 0 AND [ConsumidoEnUtc] >= [EmitidoEnUtc] AND [ConsumidoEnUtc] < [ExpiraEnUtc])");
                    table.CheckConstraint("CK_DesafioCorreo_Fechas", "DATEPART(TZOFFSET,[EmitidoEnUtc]) = 0 AND DATEPART(TZOFFSET,[ExpiraEnUtc]) = 0 AND [ExpiraEnUtc] = DATEADD(hour,24,[EmitidoEnUtc])");
                    table.CheckConstraint("CK_DesafioCorreo_Hash", "DATALENGTH([TokenHash]) = 32");
                    table.CheckConstraint("CK_DesafioCorreo_Intentos", "[IntentosConfirmacion] >= 0 AND (([IntentosConfirmacion] = 0 AND [VentanaIntentosEnUtc] IS NULL) OR ([IntentosConfirmacion] > 0 AND [VentanaIntentosEnUtc] IS NOT NULL AND DATEPART(TZOFFSET,[VentanaIntentosEnUtc]) = 0))");
                    table.CheckConstraint("CK_DesafioCorreo_Invalidacion", "[InvalidadoEnUtc] IS NULL OR (DATEPART(TZOFFSET,[InvalidadoEnUtc]) = 0 AND [InvalidadoEnUtc] >= [EmitidoEnUtc])");
                    table.CheckConstraint("CK_DesafioCorreo_Proposito", "[Proposito] = 1");
                    table.ForeignKey(
                        name: "FK_DesafioVerificacionCorreo_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PresupuestoEmisionCorreo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    TotalEmisiones = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PresupuestoEmisionCorreo", x => x.Id);
                    table.CheckConstraint("CK_PresupuestoCorreo", "[Id] = 1 AND [TotalEmisiones] >= 0");
                });

            migrationBuilder.InsertData(
                table: "PresupuestoEmisionCorreo",
                columns: new[] { "Id", "TotalEmisiones" },
                values: new object[] { 1, 0L });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Usuario_VerificacionCorreo",
                table: "Usuario",
                sql: "([EstadoVerificacionCorreo] IN (0,1) AND [EmailVerificadoEnUtc] IS NULL) OR ([EstadoVerificacionCorreo] = 2 AND [EmailVerificadoEnUtc] IS NOT NULL AND DATEPART(TZOFFSET,[EmailVerificadoEnUtc]) = 0)");

            migrationBuilder.CreateIndex(
                name: "IX_DesafioVerificacionCorreo_EmitidoEnUtc",
                table: "DesafioVerificacionCorreo",
                column: "EmitidoEnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DesafioVerificacionCorreo_TokenHash",
                table: "DesafioVerificacionCorreo",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DesafioVerificacionCorreo_UsuarioId_Proposito",
                table: "DesafioVerificacionCorreo",
                columns: new[] { "UsuarioId", "Proposito" },
                unique: true,
                filter: "[ConsumidoEnUtc] IS NULL AND [InvalidadoEnUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DesafioVerificacionCorreo_UsuarioId_Proposito_EmitidoEnUtc",
                table: "DesafioVerificacionCorreo",
                columns: new[] { "UsuarioId", "Proposito", "EmitidoEnUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @filas bigint;
                SELECT @filas = COUNT_BIG(*) FROM [Usuario] WITH (TABLOCKX, HOLDLOCK);
                SELECT @filas = COUNT_BIG(*) FROM [DesafioVerificacionCorreo] WITH (TABLOCKX, HOLDLOCK);
                SELECT @filas = COUNT_BIG(*) FROM [PresupuestoEmisionCorreo] WITH (TABLOCKX, HOLDLOCK);
                IF EXISTS (SELECT 1 FROM [Usuario] WHERE [EstadoVerificacionCorreo] <> 0)
                    OR EXISTS (SELECT 1 FROM [DesafioVerificacionCorreo])
                    OR EXISTS (SELECT 1 FROM [PresupuestoEmisionCorreo] WHERE [TotalEmisiones] <> 0)
                    THROW 51030, N'No se puede retirar la protección de verificación de cuentas nuevas o desafíos.', 1;
                DROP TRIGGER [TR_Usuario_ProtegeVerificacionCorreo];
                """);
            migrationBuilder.DropTable(
                name: "DesafioVerificacionCorreo");

            migrationBuilder.DropTable(
                name: "PresupuestoEmisionCorreo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Usuario_VerificacionCorreo",
                table: "Usuario");

            migrationBuilder.DropColumn(
                name: "EmailVerificadoEnUtc",
                table: "Usuario");

            migrationBuilder.DropColumn(
                name: "EstadoVerificacionCorreo",
                table: "Usuario");

            migrationBuilder.DropColumn(
                name: "VersionCorreo",
                table: "Usuario");
        }
    }
}
