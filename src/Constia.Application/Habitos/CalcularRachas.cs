namespace Constia.Application.Habitos;

public static class CalcularRachas
{
    // Las series incluyen todas las oportunidades conocidas o posibles hasta el corte,
    // en orden ascendente y sin repetir fechas. Las fechas omitidas son neutras.
    public static ResultadoRachas Individual(
        DateOnly corte, bool activo, IEnumerable<DiaRachaIndividual> dias)
    {
        var conteos = dias.Select(dia => dia.EsOportunidad switch
        {
            true => new DiaRachaGeneral(dia.Fecha, 1, dia.TieneRegistro ? 1 : 0),
            false => new DiaRachaGeneral(dia.Fecha, 0, 0),
            null when dia.TieneRegistro => new DiaRachaGeneral(dia.Fecha, 0, 0, DesconocidosConRegistro: 1),
            null => new DiaRachaGeneral(dia.Fecha, 0, 0, DesconocidosSinRegistro: 1)
        });
        var resultado = General(corte, conteos);
        return activo
            ? resultado
            : resultado with { Actual = new(EstadoMetricaRacha.NoAplicable, null) };
    }

    public static ResultadoRachas General(DateOnly corte, IEnumerable<DiaRachaGeneral> dias)
    {
        var actualMinima = 0;
        var actualMaxima = 0;
        var mejorMinima = 0;
        var mejorMaxima = 0;
        DateOnly? anterior = null;

        foreach (var dia in dias)
        {
            if (anterior is { } fecha && dia.Fecha <= fecha)
                throw new ArgumentException("Las fechas deben estar en orden ascendente y sin duplicados.", nameof(dias));
            anterior = dia.Fecha;

            if (dia.Fecha > corte)
                continue;

            if (dia.Programados < 0 || dia.Cumplidos < 0 || dia.Cumplidos > dia.Programados ||
                dia.DesconocidosConRegistro < 0 || dia.DesconocidosSinRegistro < 0)
                throw new ArgumentException("Los conteos del día no son válidos.", nameof(dias));

            // Las oportunidades posibles con registro favorecen el umbral; las que no tienen registro lo dificultan.
            var programadosFavorables = (long)dia.Programados + dia.DesconocidosConRegistro;
            var cumplidosFavorables = (long)dia.Cumplidos + dia.DesconocidosConRegistro;
            var programadosDesfavorables = (long)dia.Programados + dia.DesconocidosSinRegistro;
            var puedeSumar = programadosFavorables > 0 && 10 * cumplidosFavorables >= 7 * programadosFavorables;
            var puedeFallar = 10L * dia.Cumplidos < 7 * programadosDesfavorables;
            var ventanaCerrada = corte.DayNumber - dia.Fecha.DayNumber > 1;
            var puedeCortar = ventanaCerrada && puedeFallar;
            var puedeMantener = dia.Programados == 0 || (!ventanaCerrada && puedeFallar);

            // Para ambos resultados, cortar <= mantener <= sumar. Los extremos bastan para saber si el valor es único.
            actualMinima = puedeCortar ? 0 : puedeMantener ? actualMinima : actualMinima + 1;
            actualMaxima = puedeSumar ? actualMaxima + 1 : puedeMantener ? actualMaxima : 0;
            mejorMinima = Math.Max(mejorMinima, actualMinima);
            mejorMaxima = Math.Max(mejorMaxima, actualMaxima);
        }

        return new(Resolver(actualMinima, actualMaxima), Resolver(mejorMinima, mejorMaxima));
    }

    private static MetricaRacha Resolver(int minimo, int maximo) =>
        minimo == maximo
            ? new(EstadoMetricaRacha.Disponible, minimo)
            : new(EstadoMetricaRacha.NoDisponible, null);
}

// null representa una oportunidad posible cuyo estado histórico se desconoce.
public readonly record struct DiaRachaIndividual(DateOnly Fecha, bool? EsOportunidad, bool TieneRegistro);

// Programados y Cumplidos cuentan solo oportunidades conocidas.
public readonly record struct DiaRachaGeneral(
    DateOnly Fecha, int Programados, int Cumplidos,
    int DesconocidosConRegistro = 0, int DesconocidosSinRegistro = 0);

public readonly record struct ResultadoRachas(MetricaRacha Actual, MetricaRacha Mejor);
public readonly record struct MetricaRacha(EstadoMetricaRacha Estado, int? Valor);

public enum EstadoMetricaRacha
{
    NoDisponible,
    Disponible,
    NoAplicable
}
