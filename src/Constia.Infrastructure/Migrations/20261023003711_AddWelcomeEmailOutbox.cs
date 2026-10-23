using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Constia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWelcomeEmailOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ControlBienvenida",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    VersionEsquema = table.Column<int>(type: "int", nullable: false),
                    ProtocoloEscritor = table.Column<int>(type: "int", nullable: false),
                    CapturaHabilitada = table.Column<bool>(type: "bit", nullable: false),
                    EpocaCaptura = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InicioCapturaEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinCapturaEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EnvioHabilitado = table.Column<bool>(type: "bit", nullable: false),
                    PausaProveedor = table.Column<bool>(type: "bit", nullable: false),
                    MotivoPausa = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlBienvenida", x => x.Id);
                    table.CheckConstraint("CK_ControlBienvenida_Epoca", "([EpocaCaptura] IS NULL AND [CapturaHabilitada]=0 AND [InicioCapturaEnUtc] IS NULL AND [FinCapturaEnUtc] IS NULL) OR ([EpocaCaptura] IS NOT NULL AND [EpocaCaptura]<>'00000000-0000-0000-0000-000000000000' AND [InicioCapturaEnUtc] IS NOT NULL AND DATEPART(TZOFFSET,[InicioCapturaEnUtc])=0 AND (([CapturaHabilitada]=1 AND [FinCapturaEnUtc] IS NULL) OR ([CapturaHabilitada]=0 AND [FinCapturaEnUtc] IS NOT NULL AND DATEPART(TZOFFSET,[FinCapturaEnUtc])=0 AND [FinCapturaEnUtc]>=[InicioCapturaEnUtc])))");
                    table.CheckConstraint("CK_ControlBienvenida_Identidad", "[Id]=1 AND [VersionEsquema]=1 AND [ProtocoloEscritor]=1");
                    table.CheckConstraint("CK_ControlBienvenida_Pausa", "([PausaProveedor]=0 AND [MotivoPausa] IS NULL) OR ([PausaProveedor]=1 AND [MotivoPausa] IS NOT NULL AND [MotivoPausa]>=0)");
                });

            migrationBuilder.CreateTable(
                name: "EventoBienvenida",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EpocaCaptura = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<byte>(type: "tinyint", nullable: false),
                    ConfirmadoEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CapturadoEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiraEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Estado = table.Column<byte>(type: "tinyint", nullable: false),
                    MotivoFinal = table.Column<byte>(type: "tinyint", nullable: false),
                    Reservas = table.Column<int>(type: "int", nullable: false),
                    HuboIncertidumbre = table.Column<bool>(type: "bit", nullable: false),
                    ProximaEjecucionEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PrimeraReservaEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    HorizonteHttpEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseGeneracion = table.Column<long>(type: "bigint", nullable: false),
                    LeaseHastaUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UltimoLeaseHastaUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Destinatario = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlantillaVersion = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    PerfilProveedorVersion = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    PayloadUtf8 = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    PayloadSha256 = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    IdProveedor = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: true),
                    FinalizadoEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DatosDepuradosEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventoBienvenida", x => x.Id);
                    table.CheckConstraint("CK_Bienvenida_Coherencia", "([Estado]<>1 OR ([Reservas]=0 AND [HuboIncertidumbre]=0)) AND ([Estado] NOT IN (3,4) OR [Reservas]>0) AND ([Reservas]=0 OR [PayloadUtf8] IS NOT NULL OR [DatosDepuradosEnUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_Bienvenida_Depuracion", "[DatosDepuradosEnUtc] IS NULL OR ([Estado] BETWEEN 6 AND 8 AND [Destinatario] IS NULL AND [PayloadUtf8] IS NULL AND [PayloadSha256] IS NULL AND DATEPART(TZOFFSET,[DatosDepuradosEnUtc])=0 AND [DatosDepuradosEnUtc]>=[FinalizadoEnUtc])");
                    table.CheckConstraint("CK_Bienvenida_Estado", "[Estado] BETWEEN 1 AND 8 AND [MotivoFinal] BETWEEN 0 AND 7 AND [Reservas] BETWEEN 0 AND 3 AND [LeaseGeneracion]>=0");
                    table.CheckConstraint("CK_Bienvenida_Fechas", "DATEPART(TZOFFSET,[ConfirmadoEnUtc])=0 AND DATEPART(TZOFFSET,[CapturadoEnUtc])=0 AND DATEPART(TZOFFSET,[ExpiraEnUtc])=0 AND [ExpiraEnUtc]=DATEADD(hour,24,[CapturadoEnUtc])");
                    table.CheckConstraint("CK_Bienvenida_FechasOpcionales", "([ProximaEjecucionEnUtc] IS NULL OR DATEPART(TZOFFSET,[ProximaEjecucionEnUtc])=0) AND ([PrimeraReservaEnUtc] IS NULL OR DATEPART(TZOFFSET,[PrimeraReservaEnUtc])=0) AND ([HorizonteHttpEnUtc] IS NULL OR DATEPART(TZOFFSET,[HorizonteHttpEnUtc])=0) AND ([LeaseHastaUtc] IS NULL OR DATEPART(TZOFFSET,[LeaseHastaUtc])=0) AND ([UltimoLeaseHastaUtc] IS NULL OR DATEPART(TZOFFSET,[UltimoLeaseHastaUtc])=0)");
                    table.CheckConstraint("CK_Bienvenida_Final", "([Estado] BETWEEN 1 AND 5 AND [FinalizadoEnUtc] IS NULL AND [DatosDepuradosEnUtc] IS NULL AND [Destinatario] IS NOT NULL AND [MotivoFinal]=0) OR ([Estado] BETWEEN 6 AND 8 AND [FinalizadoEnUtc] IS NOT NULL AND DATEPART(TZOFFSET,[FinalizadoEnUtc])=0 AND [FinalizadoEnUtc]>=[CapturadoEnUtc] AND [ProximaEjecucionEnUtc] IS NULL AND [LeaseId] IS NULL AND ([Estado]<>7 OR ([HuboIncertidumbre]=0 AND [MotivoFinal]<>0)) AND ([Estado]<>8 OR ([HuboIncertidumbre]=1 AND [MotivoFinal]<>0)) AND ([Estado]<>6 OR ([Reservas]>0 AND [MotivoFinal]=0)))");
                    table.CheckConstraint("CK_Bienvenida_Identidad", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [EpocaCaptura] <> '00000000-0000-0000-0000-000000000000' AND [Tipo] = 1");
                    table.CheckConstraint("CK_Bienvenida_Lease", "([Estado] IN (2,3) AND [LeaseId] IS NOT NULL AND [LeaseId]<>'00000000-0000-0000-0000-000000000000' AND [LeaseHastaUtc] IS NOT NULL AND [LeaseGeneracion]>0 AND [UltimoLeaseHastaUtc] IS NOT NULL AND [UltimoLeaseHastaUtc]>=[LeaseHastaUtc]) OR ([Estado] NOT IN (2,3) AND [LeaseId] IS NULL AND [LeaseHastaUtc] IS NULL)");
                    table.CheckConstraint("CK_Bienvenida_Payload", "([PayloadUtf8] IS NULL AND [PayloadSha256] IS NULL) OR ([PayloadUtf8] IS NOT NULL AND DATALENGTH([PayloadUtf8]) BETWEEN 1 AND 16384 AND [PayloadSha256] IS NOT NULL AND DATALENGTH([PayloadSha256])=32 AND [PerfilProveedorVersion] IS NOT NULL)");
                    table.CheckConstraint("CK_Bienvenida_Reserva", "([Reservas]=0 AND [PrimeraReservaEnUtc] IS NULL AND [HorizonteHttpEnUtc] IS NULL) OR ([Reservas]>0 AND [PrimeraReservaEnUtc] IS NOT NULL AND [HorizonteHttpEnUtc] IS NOT NULL AND [PrimeraReservaEnUtc]>=[CapturadoEnUtc] AND [HorizonteHttpEnUtc]=CASE WHEN DATEADD(hour,23,[PrimeraReservaEnUtc])<[ExpiraEnUtc] THEN DATEADD(hour,23,[PrimeraReservaEnUtc]) ELSE [ExpiraEnUtc] END)");
                    table.ForeignKey(
                        name: "FK_EventoBienvenida_Usuario_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntentoBienvenida",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<byte>(type: "tinyint", nullable: false),
                    ReservadoEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PropietarioLeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseGeneracion = table.Column<long>(type: "bigint", nullable: false),
                    Estado = table.Column<byte>(type: "tinyint", nullable: false),
                    HttpIniciadoConfirmado = table.Column<bool>(type: "bit", nullable: true),
                    MotivoEntrega = table.Column<int>(type: "int", nullable: true),
                    HttpStatus = table.Column<short>(type: "smallint", nullable: true),
                    FinalizadoEnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntentoBienvenida", x => x.Id);
                    table.CheckConstraint("CK_IntentoBienvenida_Estado", "[Estado] BETWEEN 1 AND 6 AND (([Estado]=1 AND [FinalizadoEnUtc] IS NULL AND [HttpIniciadoConfirmado] IS NULL AND [MotivoEntrega] IS NULL AND [HttpStatus] IS NULL) OR ([Estado]<>1 AND [FinalizadoEnUtc] IS NOT NULL)) AND ([Estado]<>2 OR ([HttpIniciadoConfirmado] IS NOT NULL AND [HttpIniciadoConfirmado]=1)) AND ([Estado]<>5 OR ([HttpIniciadoConfirmado] IS NOT NULL AND [HttpIniciadoConfirmado]=0))");
                    table.CheckConstraint("CK_IntentoBienvenida_Fechas", "DATEPART(TZOFFSET,[ReservadoEnUtc])=0 AND ([FinalizadoEnUtc] IS NULL OR (DATEPART(TZOFFSET,[FinalizadoEnUtc])=0 AND [FinalizadoEnUtc]>=[ReservadoEnUtc]))");
                    table.CheckConstraint("CK_IntentoBienvenida_Identidad", "[Id]<>'00000000-0000-0000-0000-000000000000' AND [Ordinal] BETWEEN 1 AND 3 AND [LeaseGeneracion]>0 AND [PropietarioLeaseId]<>'00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_IntentoBienvenida_Resultado", "([HttpStatus] IS NULL OR [HttpStatus] BETWEEN 100 AND 599) AND ([MotivoEntrega] IS NULL OR [MotivoEntrega]>=0)");
                    table.ForeignKey(
                        name: "FK_IntentoBienvenida_EventoBienvenida_EventoId",
                        column: x => x.EventoId,
                        principalTable: "EventoBienvenida",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ControlBienvenida",
                columns: new[] { "Id", "CapturaHabilitada", "EnvioHabilitado", "EpocaCaptura", "FinCapturaEnUtc", "InicioCapturaEnUtc", "MotivoPausa", "PausaProveedor", "ProtocoloEscritor", "VersionEsquema" },
                values: new object[] { 1, false, false, null, null, null, null, false, 1, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_EventoBienvenida_Estado_ProximaEjecucionEnUtc_Id",
                table: "EventoBienvenida",
                columns: new[] { "Estado", "ProximaEjecucionEnUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_EventoBienvenida_UsuarioId",
                table: "EventoBienvenida",
                column: "UsuarioId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntentoBienvenida_ReservadoEnUtc_EventoId",
                table: "IntentoBienvenida",
                columns: new[] { "ReservadoEnUtc", "EventoId" });

            migrationBuilder.CreateIndex(
                name: "UX_IntentoBienvenida_EventoId_Ordinal",
                table: "IntentoBienvenida",
                columns: new[] { "EventoId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // El bloqueo cubre comprobación y DROP dentro de la transacción de EF.
            migrationBuilder.Sql("""
                DECLARE @eventos bigint,@intentos bigint,@controles bigint;
                SELECT @eventos=COUNT_BIG(*) FROM dbo.EventoBienvenida WITH (TABLOCKX,HOLDLOCK);
                SELECT @intentos=COUNT_BIG(*) FROM dbo.IntentoBienvenida WITH (TABLOCKX,HOLDLOCK);
                SELECT @controles=COUNT_BIG(*) FROM dbo.ControlBienvenida WITH (TABLOCKX,HOLDLOCK);
                IF @eventos<>0 OR @intentos<>0 OR @controles<>1 OR EXISTS (
                    SELECT 1 FROM dbo.ControlBienvenida WHERE Id<>1 OR VersionEsquema<>1 OR ProtocoloEscritor<>1
                    OR CapturaHabilitada<>0 OR EnvioHabilitado<>0 OR PausaProveedor<>0
                    OR EpocaCaptura IS NOT NULL OR InicioCapturaEnUtc IS NOT NULL
                    OR FinCapturaEnUtc IS NOT NULL OR MotivoPausa IS NOT NULL)
                    THROW 51040, 'Rollback de bienvenida bloqueado: existen eventos o evidencia de activacion.', 1;
                """);

            migrationBuilder.DropTable(
                name: "ControlBienvenida");

            migrationBuilder.DropTable(
                name: "IntentoBienvenida");

            migrationBuilder.DropTable(
                name: "EventoBienvenida");
        }
    }
}
