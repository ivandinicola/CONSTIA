using Constia.Domain;

namespace Constia.Tests;

public class HabitoTests
{
    [Fact]
    public void Constructor_WithValidValues_CreatesActiveHabitoWithUniqueIdentifier()
    {
        var usuario = CrearUsuario();
        var antesDeCrear = DateTimeOffset.UtcNow;

        var habito = new Habito(
            usuario,
            "Leer",
            "Leer un libro",
            new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);

        var despuesDeCrear = DateTimeOffset.UtcNow;
        var otroHabito = new Habito(usuario, "Caminar", null, new DateOnly(2026, 10, 5), [DayOfWeek.Friday]);

        Assert.NotEqual(Guid.Empty, habito.Id);
        Assert.NotEqual(habito.Id, otroHabito.Id);
        Assert.Same(usuario, habito.Usuario);
        Assert.Equal("Leer", habito.Nombre);
        Assert.Equal("Leer un libro", habito.Descripcion);
        Assert.InRange(habito.FechaCreacion, antesDeCrear, despuesDeCrear);
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
            CrearUsuario(), nombre, null, new DateOnly(2026, 10, 5), [DayOfWeek.Monday]));
    }

    [Fact]
    public void Constructor_WithNullUsuario_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new Habito(
            null!, "Leer", null, new DateOnly(2026, 10, 5), [DayOfWeek.Monday]));
    }

    [Fact]
    public void Constructor_WithNoScheduledDays_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Habito(
            CrearUsuario(), "Leer", null, new DateOnly(2026, 10, 5), []));
    }

    [Fact]
    public void Constructor_WithInvalidScheduledDay_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Habito(
            CrearUsuario(), "Leer", null, new DateOnly(2026, 10, 5), [(DayOfWeek)7]));
    }

    [Fact]
    public void Constructor_WithRepeatedScheduledDays_StoresEachDayOnce()
    {
        var habito = new Habito(
            CrearUsuario(), "Leer", null, new DateOnly(2026, 10, 5), [DayOfWeek.Monday, DayOfWeek.Monday]);

        Assert.Equal([DayOfWeek.Monday], habito.DiasProgramados.Select(dia => dia.Dia));
    }

    [Fact]
    public void Desactivar_SetsInactiveAndPreservesHabitData()
    {
        var usuario = CrearUsuario();
        var habito = new Habito(
            usuario,
            "Leer",
            "Leer un libro",
            new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);

        habito.Desactivar();

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
        var habito = new Habito(
            usuario,
            "Leer",
            "Un capítulo",
            new DateOnly(2026, 10, 5),
            [DayOfWeek.Monday, DayOfWeek.Wednesday]);
        var id = habito.Id;
        var fechaCreacion = habito.FechaCreacion;
        var fechaInicio = habito.FechaInicio;

        habito.Desactivar();
        habito.Desactivar();

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
        var habito = new Habito(
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
        var habito = new Habito(
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
        habito.Desactivar();

        habito.ActualizarConfiguracion("Nombre editado", "Descripción", [DayOfWeek.Monday, DayOfWeek.Wednesday]);

        Assert.Equal("Nombre editado", habito.Nombre);
        Assert.Equal(EstadoHabito.Inactivo, habito.Estado);
    }

    private static Usuario CrearUsuario() => new("Ana", "ana@example.com", "hashed-value", "Etc/UTC");

    private static Habito CrearHabitoParaActualizacion() => new(
        CrearUsuario(),
        "Nombre original",
        "Descripción original",
        new DateOnly(2026, 10, 5),
        [DayOfWeek.Monday, DayOfWeek.Wednesday]);

    private static (Guid Id, Usuario Usuario, string Nombre, string? Descripcion, DateTimeOffset CreatedAt,
        DateOnly StartDate, EstadoHabito Estado, DayOfWeek[] Dias) CapturarEstado(Habito habito) =>
        (habito.Id, habito.Usuario, habito.Nombre, habito.Descripcion, habito.FechaCreacion,
            habito.FechaInicio, habito.Estado, habito.DiasProgramados.Select(dia => dia.Dia).ToArray());

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
}
