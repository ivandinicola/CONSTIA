using Constia.Domain;

namespace Constia.Tests;

public class HabitoTests
{
    [Fact]
    public void Constructor_WithValidValues_CreatesActiveHabitoWithUniqueIdentifier()
    {
        var usuario = CrearUsuario();
        var instante = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

        var habito = new Habito(
            usuario,
            "Leer",
            "Leer un libro", new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 10, 5), CatalogoCategoriasHabito.Todas[4].Id, "libro",
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);

        var otroHabito = new Habito(usuario, "Caminar", null, new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero), new DateOnly(2026, 10, 5), CatalogoCategoriasHabito.Todas[4].Id, "libro", [DayOfWeek.Friday]);

        Assert.NotEqual(Guid.Empty, habito.Id);
        Assert.NotEqual(habito.Id, otroHabito.Id);
        Assert.Same(usuario, habito.Usuario);
        Assert.Equal("Leer", habito.Nombre);
        Assert.Equal("Leer un libro", habito.Descripcion);
        Assert.Equal(instante, habito.FechaCreacion);
        Assert.Equal(new DateOnly(2026, 10, 5), habito.FechaInicio);
        Assert.Equal(EstadoHabito.Activo, habito.Estado);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], habito.DiasProgramados.Select(dia => dia.Dia));
        var programacionInicial = Assert.Single(habito.Programaciones);
        Assert.Equal(habito.FechaInicio, programacionInicial.VigenteDesde);
        Assert.Null(programacionInicial.VigenteHasta);
        Assert.All(
            programacionInicial.DiasProgramados,
            dia => Assert.Contains(habito.DiasProgramados, actual => ReferenceEquals(dia, actual)));
        Assert.Null(otroHabito.Descripcion);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_WithEmptyOrWhitespaceName_ThrowsArgumentException(string nombre)
    {
        Assert.Throws<ArgumentException>(() => new Habito(
            CrearUsuario(), nombre, null, new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero), new DateOnly(2026, 10, 5), CatalogoCategoriasHabito.Todas[4].Id, "libro", [DayOfWeek.Monday]));
    }

    [Fact]
    public void Constructor_WithNullUsuario_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new Habito(
            null!, "Leer", null, new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero), new DateOnly(2026, 10, 5), CatalogoCategoriasHabito.Todas[4].Id, "libro", [DayOfWeek.Monday]));
    }

    [Fact]
    public void Constructor_WithNoScheduledDays_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Habito(
            CrearUsuario(), "Leer", null, new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero), new DateOnly(2026, 10, 5), CatalogoCategoriasHabito.Todas[4].Id, "libro", []));
    }

    [Fact]
    public void Constructor_WithInvalidScheduledDay_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Habito(
            CrearUsuario(), "Leer", null, new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero), new DateOnly(2026, 10, 5), CatalogoCategoriasHabito.Todas[4].Id, "libro", [(DayOfWeek)7]));
    }

    [Fact]
    public void Constructor_WithRepeatedScheduledDays_StoresEachDayOnce()
    {
        var habito = new Habito(
            CrearUsuario(), "Leer", null, new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero), new DateOnly(2026, 10, 5), CatalogoCategoriasHabito.Todas[4].Id, "libro", [DayOfWeek.Monday, DayOfWeek.Monday]);

        Assert.Equal([DayOfWeek.Monday], habito.DiasProgramados.Select(dia => dia.Dia));
    }

    [Fact]
    public void Desactivar_SetsInactiveAndPreservesHabitData()
    {
        var usuario = CrearUsuario();
        var habito = HabitoHistorico.Crear(
            usuario,
            "Leer",
            "Leer un libro",
            new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);

        habito.Desactivar(habito.FechaInicio);

        Assert.Equal(EstadoHabito.Inactivo, habito.Estado);
        Assert.Same(usuario, habito.Usuario);
        Assert.Equal("Leer", habito.Nombre);
        Assert.Equal("Leer un libro", habito.Descripcion);
        Assert.Equal(new DateOnly(2026, 10, 5), habito.FechaInicio);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], habito.DiasProgramados.Select(dia => dia.Dia));
    }

    [Fact]
    public void Desactivar_Repetidamente_MantieneEstadoInactivoYSusDatos()
    {
        var usuario = CrearUsuario();
        var habito = HabitoHistorico.Crear(
            usuario,
            "Leer",
            "Un capítulo",
            new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);
        var id = habito.Id;
        var fechaCreacion = habito.FechaCreacion;
        var fechaInicio = habito.FechaInicio;

        habito.Desactivar(habito.FechaInicio);
        habito.Desactivar(habito.FechaInicio);

        Assert.Equal(EstadoHabito.Inactivo, habito.Estado);
        Assert.Equal(id, habito.Id);
        Assert.Same(usuario, habito.Usuario);
        Assert.Equal("Leer", habito.Nombre);
        Assert.Equal("Un capítulo", habito.Descripcion);
        Assert.Equal(fechaCreacion, habito.FechaCreacion);
        Assert.Equal(fechaInicio, habito.FechaInicio);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], habito.DiasProgramados.Select(dia => dia.Dia));
    }

    [Fact]
    public void ActualizarConfiguracion_ActualizaCamposEditablesYSinCambiarProgramacion()
    {
        var usuario = CrearUsuario();
        var habito = HabitoHistorico.Crear(
            usuario,
            "Leer",
            "Descripción anterior",
            new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);
        var createdAt = habito.FechaCreacion;
        var startDate = habito.FechaInicio;
        var id = habito.Id;
        var programacionInicial = Assert.Single(habito.Programaciones);

        habito.ActualizarConfiguracion(
            "Caminar",
            "Descripción nueva",
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);

        Assert.Equal(id, habito.Id);
        Assert.Same(usuario, habito.Usuario);
        Assert.Equal("Caminar", habito.Nombre);
        Assert.Equal("Descripción nueva", habito.Descripcion);
        Assert.Equal(createdAt, habito.FechaCreacion);
        Assert.Equal(startDate, habito.FechaInicio);
        Assert.Equal(EstadoHabito.Activo, habito.Estado);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], habito.DiasProgramados.Select(dia => dia.Dia));
        Assert.Same(programacionInicial, Assert.Single(habito.Programaciones));
    }

    [Fact]
    public void ActualizarConfiguracion_PermiteEliminarDescripcion()
    {
        var habito = HabitoHistorico.Crear(
            CrearUsuario(),
            "Leer",
            "Descripción anterior",
            new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday]);

        habito.ActualizarConfiguracion("Leer", null, [DayOfWeek.Monday]);

        Assert.Null(habito.Descripcion);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ActualizarConfiguracion_ConNombreInvalido_LanzaSinModificarAgregado(string nombre)
    {
        var habito = CrearHabitoParaActualizacion();
        var estadoAnterior = CapturarEstado(habito);

        Assert.Throws<ArgumentException>(() =>
            habito.ActualizarConfiguracion(nombre, "Descripción nueva", [DayOfWeek.Friday]));

        AssertEstadoIgual(estadoAnterior, habito);
    }

    [Fact]
    public void ActualizarConfiguracion_ConDiasInvalidos_LanzaSinMutacionesParciales()
    {
        var habito = CrearHabitoParaActualizacion();
        var estadoAnterior = CapturarEstado(habito);

        Assert.Throws<ArgumentException>(() =>
            habito.ActualizarConfiguracion("Nombre nuevo", null, [(DayOfWeek)7]));

        AssertEstadoIgual(estadoAnterior, habito);
    }

    [Fact]
    public void ActualizarConfiguracion_ConDiasDiferentes_LanzaYPreservaProgramacion()
    {
        var habito = CrearHabitoParaActualizacion();
        var estadoAnterior = CapturarEstado(habito);

        Assert.Throws<ArgumentException>(() =>
            habito.ActualizarConfiguracion("Nombre nuevo", "Descripción nueva", [DayOfWeek.Friday]));

        AssertEstadoIgual(estadoAnterior, habito);
        Assert.Single(habito.Programaciones);
    }

    [Fact]
    public void ObtenerProgramacionPara_EnFechaInicioYDespues_DevuelveLaVersionInicial()
    {
        var habito = CrearHabitoParaActualizacion();

        var enFechaInicio = habito.ObtenerProgramacionPara(habito.FechaInicio);
        var despuesDeFechaInicio = habito.ObtenerProgramacionPara(habito.FechaInicio.AddDays(10));

        Assert.Same(Assert.Single(habito.Programaciones), enFechaInicio);
        Assert.Same(enFechaInicio, despuesDeFechaInicio);
    }

    [Fact]
    public void ObtenerProgramacionPara_AntesDeFechaInicio_DevuelveNull()
    {
        var habito = CrearHabitoParaActualizacion();

        var programacion = habito.ObtenerProgramacionPara(habito.FechaInicio.AddDays(-1));

        Assert.Null(programacion);
    }

    [Fact]
    public void ProgramacionHabito_AplicaIntervaloSemiabierto()
    {
        var vigenteDesde = new DateOnly(2026, 10, 5);
        var vigenteHasta = new DateOnly(2026, 10, 10);
        var programacion = new ProgramacionHabito(vigenteDesde, vigenteHasta, [DayOfWeek.Monday]);

        Assert.False(programacion.AplicaA(vigenteDesde.AddDays(-1)));
        Assert.True(programacion.AplicaA(vigenteDesde));
        Assert.True(programacion.AplicaA(vigenteHasta.AddDays(-1)));
        Assert.False(programacion.AplicaA(vigenteHasta));
    }

    [Fact]
    public void ProgramacionHabito_ConFinAnteriorOIgualAlInicio_LanzaArgumentException()
    {
        var inicio = new DateOnly(2026, 10, 5);

        Assert.Throws<ArgumentException>(() =>
            new ProgramacionHabito(inicio, inicio, [DayOfWeek.Monday]));
        Assert.Throws<ArgumentException>(() =>
            new ProgramacionHabito(inicio, inicio.AddDays(-1), [DayOfWeek.Monday]));
    }

    [Fact]
    public void ActualizarConfiguracion_SinDias_LanzaArgumentException()
    {
        var habito = CrearHabitoParaActualizacion();

        Assert.Throws<ArgumentException>(() =>
            habito.ActualizarConfiguracion("Nombre nuevo", null, []));
    }

    [Fact]
    public void ActualizarConfiguracion_ConMismosDiasRepetidosYEnOtroOrden_ActualizaDatosSinDuplicarDias()
    {
        var habito = CrearHabitoParaActualizacion();

        habito.ActualizarConfiguracion(
            "Nombre editado",
            "Descripción editada",
            [DayOfWeek.Wednesday, DayOfWeek.Monday, DayOfWeek.Monday]);

        Assert.Equal("Nombre editado", habito.Nombre);
        Assert.Equal("Descripción editada", habito.Descripcion);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], habito.DiasProgramados.Select(dia => dia.Dia));
    }

    [Fact]
    public void ActualizarConfiguracion_EnHabitoInactivo_NoLoReactiva()
    {
        var habito = CrearHabitoParaActualizacion();
        habito.Desactivar(habito.FechaInicio);

        habito.ActualizarConfiguracion("Nombre editado", "Descripción", [DayOfWeek.Monday, DayOfWeek.Wednesday]);

        Assert.Equal("Nombre editado", habito.Nombre);
        Assert.Equal(EstadoHabito.Inactivo, habito.Estado);
    }

    [Fact]
    public void ActualizarConfiguracionVersionada_AntesDeFechaInicio_ActualizaVersionInicial()
    {
        var habito = CrearHabitoParaActualizacion();
        var versionInicial = Assert.Single(habito.Programaciones);

        habito.ActualizarConfiguracionVersionada(
            habito.Nombre,
            habito.Descripcion,
            [DayOfWeek.Friday],
            habito.FechaInicio.AddDays(-1));

        Assert.Same(versionInicial, Assert.Single(habito.Programaciones));
        Assert.Equal(habito.FechaInicio, versionInicial.VigenteDesde);
        Assert.Null(versionInicial.VigenteHasta);
        Assert.Equal([DayOfWeek.Friday], habito.DiasProgramados.Select(dia => dia.Dia));
    }

    [Fact]
    public void ActualizarConfiguracionVersionada_EnFechaInicio_ActualizaVersionInicial()
    {
        var habito = CrearHabitoParaActualizacion();
        var versionInicial = Assert.Single(habito.Programaciones);

        habito.ActualizarConfiguracionVersionada(
            habito.Nombre,
            habito.Descripcion,
            [DayOfWeek.Friday],
            habito.FechaInicio);

        Assert.Same(versionInicial, Assert.Single(habito.Programaciones));
        Assert.Equal(habito.FechaInicio, versionInicial.VigenteDesde);
        Assert.Null(versionInicial.VigenteHasta);
        Assert.Equal([DayOfWeek.Friday], habito.DiasProgramados.Select(dia => dia.Dia));
    }

    [Fact]
    public void ActualizarConfiguracionVersionada_DespuesDeFechaInicio_CreaVersionConIntervaloSemiabierto()
    {
        var habito = CrearHabitoParaActualizacion();
        var versionInicial = Assert.Single(habito.Programaciones);
        var fechaCambio = habito.FechaInicio.AddDays(5);

        habito.ActualizarConfiguracionVersionada(
            habito.Nombre,
            habito.Descripcion,
            [DayOfWeek.Friday],
            fechaCambio);

        var versiones = habito.Programaciones.OrderBy(version => version.VigenteDesde).ToArray();
        Assert.Equal(2, versiones.Length);
        Assert.Same(versionInicial, versiones[0]);
        Assert.Equal(fechaCambio, versiones[0].VigenteHasta);
        Assert.Equal(fechaCambio, versiones[1].VigenteDesde);
        Assert.Null(versiones[1].VigenteHasta);
        Assert.Same(versiones[0], habito.ObtenerProgramacionPara(fechaCambio.AddDays(-1)));
        Assert.Same(versiones[1], habito.ObtenerProgramacionPara(fechaCambio));
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], versiones[0].DiasProgramados.Select(dia => dia.Dia));
        Assert.Equal([DayOfWeek.Friday], habito.DiasProgramados.Select(dia => dia.Dia));
    }

    [Fact]
    public void ActualizarConfiguracionVersionada_VariasVecesElMismoDia_ConsolidaLaUltimaProgramacion()
    {
        var habito = CrearHabitoParaActualizacion();
        var fechaCambio = habito.FechaInicio.AddDays(5);

        habito.ActualizarConfiguracionVersionada(
            habito.Nombre, habito.Descripcion, [DayOfWeek.Friday], fechaCambio);
        var versionDelDia = habito.ObtenerProgramacionPara(fechaCambio);
        habito.ActualizarConfiguracionVersionada(
            habito.Nombre, habito.Descripcion, [DayOfWeek.Sunday], fechaCambio);

        Assert.Equal(2, habito.Programaciones.Count);
        Assert.Same(versionDelDia, habito.ObtenerProgramacionPara(fechaCambio));
        Assert.Equal([DayOfWeek.Sunday], habito.DiasProgramados.Select(dia => dia.Dia));
        Assert.Equal(fechaCambio, habito.Programaciones.Single(version => version.VigenteHasta is not null).VigenteHasta);
    }

    [Fact]
    public void ActualizarConfiguracionVersionada_EnFechasPosteriores_MantieneHistorialContinuo()
    {
        var habito = CrearHabitoParaActualizacion();
        var primeraFecha = habito.FechaInicio.AddDays(5);
        var segundaFecha = primeraFecha.AddDays(4);
        var terceraFecha = segundaFecha.AddDays(1);

        habito.ActualizarConfiguracionVersionada(
            habito.Nombre, habito.Descripcion, [DayOfWeek.Friday], primeraFecha);
        habito.ActualizarConfiguracionVersionada(
            habito.Nombre, habito.Descripcion, [DayOfWeek.Sunday], segundaFecha);
        habito.ActualizarConfiguracionVersionada(
            habito.Nombre, habito.Descripcion, [DayOfWeek.Tuesday], terceraFecha);

        var versiones = habito.Programaciones.OrderBy(version => version.VigenteDesde).ToArray();
        Assert.Equal(4, versiones.Length);
        Assert.Equal(habito.FechaInicio, versiones[0].VigenteDesde);
        Assert.Equal(primeraFecha, versiones[0].VigenteHasta);
        Assert.Equal(primeraFecha, versiones[1].VigenteDesde);
        Assert.Equal(segundaFecha, versiones[1].VigenteHasta);
        Assert.Equal(segundaFecha, versiones[2].VigenteDesde);
        Assert.Equal(terceraFecha, versiones[2].VigenteHasta);
        Assert.Equal(terceraFecha, versiones[3].VigenteDesde);
        Assert.Null(versiones[3].VigenteHasta);

        for (var index = 0; index < versiones.Length - 1; index++)
        {
            Assert.Equal(versiones[index].VigenteHasta, versiones[index + 1].VigenteDesde);
        }

        Assert.Single(versiones, version => version.VigenteHasta is null);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], versiones[0].DiasProgramados.Select(dia => dia.Dia));
        Assert.Equal([DayOfWeek.Friday], versiones[1].DiasProgramados.Select(dia => dia.Dia));
        Assert.Equal([DayOfWeek.Sunday], versiones[2].DiasProgramados.Select(dia => dia.Dia));
        Assert.Equal([DayOfWeek.Tuesday], versiones[3].DiasProgramados.Select(dia => dia.Dia));
    }

    [Fact]
    public void ActualizarConfiguracionVersionada_MismosDiasEnOtroOrdenYRepetidos_NoCreaVersion()
    {
        var habito = CrearHabitoParaActualizacion();
        var estadoAntes = CapturarProgramaciones(habito);

        habito.ActualizarConfiguracionVersionada(
            "Nombre editado",
            "Descripción editada",
            [DayOfWeek.Wednesday, DayOfWeek.Monday, DayOfWeek.Monday],
            habito.FechaInicio.AddDays(3));

        AssertProgramacionesIguales(estadoAntes, CapturarProgramaciones(habito));
        Assert.Equal("Nombre editado", habito.Nombre);
        Assert.Equal("Descripción editada", habito.Descripcion);
    }

    [Fact]
    public void ActualizarConfiguracionVersionada_SinCambioEfectivo_NoCreaNiCierraVersion()
    {
        var habito = CrearHabitoParaActualizacion();
        var versionInicial = Assert.Single(habito.Programaciones);

        habito.ActualizarConfiguracionVersionada(
            habito.Nombre,
            habito.Descripcion,
            [DayOfWeek.Monday, DayOfWeek.Wednesday],
            habito.FechaInicio.AddDays(10));

        Assert.Same(versionInicial, Assert.Single(habito.Programaciones));
        Assert.Null(versionInicial.VigenteHasta);
    }

    [Fact]
    public void ActualizarConfiguracionVersionada_FechaAnteriorConMismosDias_ActualizaMetadatosYConservaTracking()
    {
        var habito = CrearHabitoParaActualizacion();
        var inicioVersionAbierta = habito.FechaInicio.AddDays(10);
        habito.ActualizarConfiguracionVersionada(
            habito.Nombre, habito.Descripcion, [DayOfWeek.Friday], inicioVersionAbierta);

        var versionesAntes = habito.Programaciones.OrderBy(version => version.VigenteDesde).ToArray();
        var versionAbierta = habito.ObtenerProgramacionPara(inicioVersionAbierta)!;
        var diasAntes = versionAbierta.DiasProgramados.ToDictionary(dia => dia.Dia);

        // No hay cambio de programación que aplicar; el request solo edita metadatos.
        habito.ActualizarConfiguracionVersionada(
            "Nombre editado",
            "Descripción editada",
            [DayOfWeek.Friday, DayOfWeek.Friday],
            inicioVersionAbierta.AddDays(-1));

        var versionesDespues = habito.Programaciones.OrderBy(version => version.VigenteDesde).ToArray();
        Assert.Equal("Nombre editado", habito.Nombre);
        Assert.Equal("Descripción editada", habito.Descripcion);
        Assert.Equal(versionesAntes.Length, versionesDespues.Length);
        for (var index = 0; index < versionesAntes.Length; index++)
        {
            Assert.Same(versionesAntes[index], versionesDespues[index]);
        }

        Assert.Same(versionAbierta, habito.ObtenerProgramacionPara(inicioVersionAbierta));
        Assert.Equal(inicioVersionAbierta, versionesDespues[0].VigenteHasta);
        Assert.Null(versionAbierta.VigenteHasta);
        Assert.Same(diasAntes[DayOfWeek.Friday], Assert.Single(versionAbierta.DiasProgramados));
    }

    [Theory]
    [MemberData(nameof(ProgramacionesInvalidas))]
    public void ActualizarConfiguracionVersionada_ConDiasInvalidos_NoMutaElAgregado(DayOfWeek[] dias)
    {
        var habito = CrearHabitoParaActualizacion();
        var estadoAntes = CapturarEstadoCompleto(habito);

        Assert.Throws<ArgumentException>(() => habito.ActualizarConfiguracionVersionada(
            "Nombre nuevo",
            "Descripción nueva",
            dias,
            habito.FechaInicio.AddDays(5)));

        AssertEstadoCompletoIgual(estadoAntes, CapturarEstadoCompleto(habito));
    }

    public static TheoryData<DayOfWeek[]> ProgramacionesInvalidas => new()
    {
        Array.Empty<DayOfWeek>(),
        new[] { (DayOfWeek)7 }
    };

    [Fact]
    public void ActualizarConfiguracionVersionada_FechaRetroactivaPorCambioDeZona_LanzaConflictoSinMutar()
    {
        var habito = CrearHabitoParaActualizacion();
        var inicioVersionAbierta = habito.FechaInicio.AddDays(10);
        habito.ActualizarConfiguracionVersionada(
            habito.Nombre, habito.Descripcion, [DayOfWeek.Friday], inicioVersionAbierta);
        var estadoAntes = CapturarEstadoCompleto(habito);

        Assert.Throws<ConflictoVigenciaProgramacionException>(() => habito.ActualizarConfiguracionVersionada(
            "Nombre no aplicado",
            "Descripción no aplicada",
            [DayOfWeek.Sunday],
            inicioVersionAbierta.AddDays(-1)));

        AssertEstadoCompletoIgual(estadoAntes, CapturarEstadoCompleto(habito));
    }

    [Fact]
    public void ActualizarConfiguracionVersionada_RetrocedeAntesDeFechaInicioConVersionInicialCerrada_LanzaConflictoSinMutar()
    {
        var fechaInicio = new DateOnly(2026, 10, 10);
        var habito = HabitoHistorico.Crear(CrearUsuario(), "Leer", null, fechaInicio, [DayOfWeek.Monday]);
        habito.ActualizarConfiguracionVersionada(
            habito.Nombre, habito.Descripcion, [DayOfWeek.Friday], fechaInicio.AddDays(2));
        var estadoAntes = CapturarEstadoCompleto(habito);

        Assert.Throws<ConflictoVigenciaProgramacionException>(() => habito.ActualizarConfiguracionVersionada(
            habito.Nombre,
            habito.Descripcion,
            [DayOfWeek.Sunday],
            fechaInicio.AddDays(-1)));

        AssertEstadoCompletoIgual(estadoAntes, CapturarEstadoCompleto(habito));
    }

    [Fact]
    public void ActualizarConfiguracionVersionada_EnHabitoInactivo_PermiteCambiarDiasYSigueInactivo()
    {
        var habito = CrearHabitoParaActualizacion();
        habito.Desactivar(habito.FechaInicio);

        habito.ActualizarConfiguracionVersionada(
            habito.Nombre,
            habito.Descripcion,
            [DayOfWeek.Friday],
            habito.FechaInicio.AddDays(5));

        Assert.Equal(EstadoHabito.Inactivo, habito.Estado);
        Assert.Equal([DayOfWeek.Friday], habito.DiasProgramados.Select(dia => dia.Dia));
        Assert.Equal(2, habito.Programaciones.Count);
    }

    [Fact]
    public void ActualizarConfiguracionVersionada_ConservaCumplimientosYVersionesCerradas()
    {
        var habito = CrearHabitoParaActualizacion();
        var cumplimiento = new Cumplimiento(habito, habito.FechaInicio.AddDays(1));
        var primeraFecha = habito.FechaInicio.AddDays(5);
        var segundaFecha = primeraFecha.AddDays(3);
        var versionInicial = Assert.Single(habito.Programaciones);

        habito.ActualizarConfiguracionVersionada(
            habito.Nombre, habito.Descripcion, [DayOfWeek.Friday], primeraFecha);
        var primeraVersionPosterior = habito.ObtenerProgramacionPara(primeraFecha)!;
        var diasVersionIniciales = versionInicial.DiasProgramados.Select(dia => dia.Dia).ToArray();
        var primeraFechaFin = versionInicial.VigenteHasta;
        habito.ActualizarConfiguracionVersionada(
            habito.Nombre, habito.Descripcion, [DayOfWeek.Sunday], segundaFecha);

        Assert.Equal(diasVersionIniciales, versionInicial.DiasProgramados.Select(dia => dia.Dia));
        Assert.Equal(primeraFechaFin, versionInicial.VigenteHasta);
        Assert.Equal([DayOfWeek.Friday], primeraVersionPosterior.DiasProgramados.Select(dia => dia.Dia));
        Assert.Equal(segundaFecha, primeraVersionPosterior.VigenteHasta);
        Assert.Same(habito, cumplimiento.Habito);
        Assert.Equal(habito.FechaInicio.AddDays(1), cumplimiento.Fecha);
    }

    [Fact]
    public void ActualizarConfiguracionVersionada_ValidaNombreAntesDeCambiarProgramacion()
    {
        var habito = CrearHabitoParaActualizacion();
        var estadoAntes = CapturarEstadoCompleto(habito);

        Assert.Throws<ArgumentException>(() => habito.ActualizarConfiguracionVersionada(
            " ",
            "Descripción nueva",
            [DayOfWeek.Friday],
            habito.FechaInicio.AddDays(5)));

        AssertEstadoCompletoIgual(estadoAntes, CapturarEstadoCompleto(habito));
    }

    private static Usuario CrearUsuario() => new("Ana", "ana@example.com", "hashed-value", "Etc/UTC");

    private static Habito CrearHabitoParaActualizacion() => HabitoHistorico.Crear(
        CrearUsuario(),
        "Nombre original",
        "Descripción original",
        new DateOnly(2026, 10, 5),
        [DayOfWeek.Monday, DayOfWeek.Wednesday]);

    private static (Guid Id, Usuario Usuario, string Nombre, string? Descripcion, DateTimeOffset CreatedAt,
        DateOnly StartDate, EstadoHabito Estado, DayOfWeek[] Dias) CapturarEstado(Habito habito) =>
        (habito.Id, habito.Usuario, habito.Nombre, habito.Descripcion, habito.FechaCreacion,
            habito.FechaInicio, habito.Estado, habito.DiasProgramados.Select(dia => dia.Dia).ToArray());

    private static ProgramacionCapturada[] CapturarProgramaciones(Habito habito) => habito.Programaciones
        .OrderBy(programacion => programacion.VigenteDesde)
        .Select(programacion => new ProgramacionCapturada(
            programacion.VigenteDesde,
            programacion.VigenteHasta,
            programacion.DiasProgramados.Select(dia => dia.Dia).ToArray()))
        .ToArray();

    private static EstadoHabitoCapturado CapturarEstadoCompleto(Habito habito) => new(
        habito.Id,
        habito.Usuario,
        habito.Nombre,
        habito.Descripcion,
        habito.FechaCreacion,
        habito.FechaInicio,
        habito.Estado,
        CapturarProgramaciones(habito));

    private static void AssertProgramacionesIguales(
        IReadOnlyList<ProgramacionCapturada> esperado,
        IReadOnlyList<ProgramacionCapturada> actual)
    {
        Assert.Equal(esperado.Count, actual.Count);
        for (var index = 0; index < esperado.Count; index++)
        {
            Assert.Equal(esperado[index].VigenteDesde, actual[index].VigenteDesde);
            Assert.Equal(esperado[index].VigenteHasta, actual[index].VigenteHasta);
            Assert.Equal(esperado[index].Dias, actual[index].Dias);
        }
    }

    private static void AssertEstadoCompletoIgual(EstadoHabitoCapturado esperado, EstadoHabitoCapturado actual)
    {
        Assert.Equal(esperado.Id, actual.Id);
        Assert.Same(esperado.Usuario, actual.Usuario);
        Assert.Equal(esperado.Nombre, actual.Nombre);
        Assert.Equal(esperado.Descripcion, actual.Descripcion);
        Assert.Equal(esperado.CreatedAt, actual.CreatedAt);
        Assert.Equal(esperado.StartDate, actual.StartDate);
        Assert.Equal(esperado.Estado, actual.Estado);
        AssertProgramacionesIguales(esperado.Programaciones, actual.Programaciones);
    }

    private static void AssertEstadoIgual(
        (Guid Id, Usuario Usuario, string Nombre, string? Descripcion, DateTimeOffset CreatedAt,
            DateOnly StartDate, EstadoHabito Estado, DayOfWeek[] Dias) esperado,
        Habito habito)
    {
        Assert.Equal(esperado.Id, habito.Id);
        Assert.Same(esperado.Usuario, habito.Usuario);
        Assert.Equal(esperado.Nombre, habito.Nombre);
        Assert.Equal(esperado.Descripcion, habito.Descripcion);
        Assert.Equal(esperado.CreatedAt, habito.FechaCreacion);
        Assert.Equal(esperado.StartDate, habito.FechaInicio);
        Assert.Equal(esperado.Estado, habito.Estado);
        Assert.Equal(esperado.Dias, habito.DiasProgramados.Select(dia => dia.Dia));
    }

    private sealed record ProgramacionCapturada(
        DateOnly VigenteDesde,
        DateOnly? VigenteHasta,
        DayOfWeek[] Dias);

    private sealed record EstadoHabitoCapturado(
        Guid Id,
        Usuario Usuario,
        string Nombre,
        string? Descripcion,
        DateTimeOffset CreatedAt,
        DateOnly StartDate,
        EstadoHabito Estado,
        ProgramacionCapturada[] Programaciones);
}
