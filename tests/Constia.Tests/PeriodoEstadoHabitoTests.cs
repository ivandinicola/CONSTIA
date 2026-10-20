using Constia.Domain;

namespace Constia.Tests;

public sealed class PeriodoEstadoHabitoTests
{
    private static readonly DateOnly FechaInicio = new(2026, 10, 1);

    [Fact]
    public void Constructor_InicializaPeriodoActivoDesdeFechaInicioYAntesEsDesconocido()
    {
        var habit = CrearHabito();

        Assert.Equal(EstadoHabito.Activo, habit.ObtenerEstadoPara(FechaInicio));
        Assert.Null(habit.ObtenerEstadoPara(FechaInicio.AddDays(-1)));
        var period = Assert.Single(habit.PeriodosEstado);
        Assert.Equal(FechaInicio, period.VigenteDesde);
        Assert.Null(period.VigenteHasta);
        Assert.Equal(EstadoHabito.Activo, habit.Estado);
    }

    [Fact]
    public void Desactivar_DespuesDeInicio_CierraPeriodoAnteriorConLimiteSemiabierto()
    {
        var habit = CrearHabito();
        var effectiveDate = FechaInicio.AddDays(4);

        var changed = habit.Desactivar(effectiveDate);

        Assert.True(changed);
        Assert.Equal(EstadoHabito.Activo, habit.ObtenerEstadoPara(effectiveDate.AddDays(-1)));
        Assert.Equal(EstadoHabito.Inactivo, habit.ObtenerEstadoPara(effectiveDate));
        Assert.Equal(EstadoHabito.Inactivo, habit.Estado);
        var periods = habit.PeriodosEstado.OrderBy(period => period.VigenteDesde).ToArray();
        Assert.Equal(2, periods.Length);
        Assert.Equal(effectiveDate, periods[0].VigenteHasta);
        Assert.Equal(effectiveDate, periods[1].VigenteDesde);
        Assert.Null(periods[1].VigenteHasta);
    }

    [Fact]
    public void Desactivar_ElMismoDiaDelPeriodoAbierto_ConsolidaSinIntervaloVacio()
    {
        var habit = CrearHabito();

        var changed = habit.Desactivar(FechaInicio);

        Assert.True(changed);
        var period = Assert.Single(habit.PeriodosEstado);
        Assert.Equal(FechaInicio, period.VigenteDesde);
        Assert.Null(period.VigenteHasta);
        Assert.Equal(EstadoHabito.Inactivo, period.Estado);
        Assert.Equal(EstadoHabito.Inactivo, habit.ObtenerEstadoPara(FechaInicio));
    }

    [Fact]
    public void Desactivar_AntesDeFechaInicio_ActualizaEstadoInicialSinCrearPeriodoVacio()
    {
        var habit = CrearHabito();

        habit.Desactivar(FechaInicio.AddDays(-2));

        var period = Assert.Single(habit.PeriodosEstado);
        Assert.Equal(FechaInicio, period.VigenteDesde);
        Assert.Null(period.VigenteHasta);
        Assert.Equal(EstadoHabito.Inactivo, period.Estado);
        Assert.Null(habit.ObtenerEstadoPara(FechaInicio.AddDays(-1)));
        Assert.Equal(EstadoHabito.Inactivo, habit.ObtenerEstadoPara(FechaInicio));
    }

    [Fact]
    public void Desactivar_DosVecesMismoDia_NoCreaVersionesAdicionales()
    {
        var habit = CrearHabito();
        var date = FechaInicio.AddDays(2);

        Assert.True(habit.Desactivar(date));
        Assert.False(habit.Desactivar(date));

        Assert.Equal(2, habit.PeriodosEstado.Count);
        Assert.Equal(EstadoHabito.Inactivo, habit.Estado);
    }

    [Fact]
    public void Desactivar_ConFechaAnteriorAlPeriodoAbierto_LanzaConflictoSinMutarHistorial()
    {
        var habit = CrearHabito();
        var date = FechaInicio.AddDays(4);
        habit.Desactivar(date);
        var before = Capturar(habit);

        Assert.Throws<ConflictoVigenciaEstadoHabitoException>(() => habit.Desactivar(date.AddDays(-1)));

        Assert.Equal(before, Capturar(habit));
    }

    [Fact]
    public void ActualizarConfiguracionVersionada_NoModificaHistorialDeEstado()
    {
        var habit = CrearHabito();
        var statePeriod = Assert.Single(habit.PeriodosEstado);

        habit.ActualizarConfiguracionVersionada(
            habit.Nombre, habit.Descripcion, [DayOfWeek.Friday], FechaInicio.AddDays(3));

        Assert.Same(statePeriod, Assert.Single(habit.PeriodosEstado));
        Assert.Equal(EstadoHabito.Activo, statePeriod.Estado);
        Assert.Equal(FechaInicio.AddDays(3), habit.ObtenerProgramacionPara(FechaInicio.AddDays(3))!.VigenteDesde);
    }

    private static Habito CrearHabito() => HabitoHistorico.Crear(
        new Usuario("Ana", "ana@example.com", "hashed-value", "Etc/UTC"),
        "Leer", null, FechaInicio, [DayOfWeek.Monday]);

    private static string Capturar(Habito habit) => string.Join(";", habit.PeriodosEstado
        .OrderBy(period => period.VigenteDesde)
        .Select(period => $"{period.Estado}:{period.VigenteDesde}:{period.VigenteHasta}"));
}
