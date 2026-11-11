using Constia.Application.Habitos;

namespace Constia.Tests;

public sealed class CalcularRachasTests
{
    private static readonly DateOnly Lunes = new(2026, 11, 2);

    [Fact]
    public void Individual_EncadenaLunesMiercolesViernesYElLunesSiguiente()
    {
        var resultado = CalcularRachas.Individual(Lunes.AddDays(8), activo: true,
        [
            new(Lunes, true, true),
            new(Lunes.AddDays(1), false, true), // Registro conservado fuera de programación.
            new(Lunes.AddDays(2), true, true),
            new(Lunes.AddDays(4), true, true),
            new(Lunes.AddDays(7), true, true),
            new(Lunes.AddDays(9), true, true) // Fecha posterior al corte.
        ]);

        Assert.Equal(Disponible(4), resultado.Actual);
        Assert.Equal(Disponible(4), resultado.Mejor);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(5, 4)]
    [InlineData(6, 5)]
    [InlineData(7, 5)]
    [InlineData(10, 7)]
    [InlineData(int.MaxValue, 1_503_238_553)]
    public void General_AplicaElMinimoRedondeadoHaciaArriba(int programados, int minimo)
    {
        var corte = Lunes.AddDays(2);
        var alcanza = CalcularRachas.General(corte, [new(Lunes, programados, minimo)]);
        var noAlcanza = CalcularRachas.General(corte, [new(Lunes, programados, minimo - 1)]);

        Assert.Equal(Disponible(1), alcanza.Actual);
        Assert.Equal(Disponible(1), alcanza.Mejor);
        Assert.Equal(Disponible(0), noAlcanza.Actual);
        Assert.Equal(Disponible(0), noAlcanza.Mejor);
    }

    [Fact]
    public void General_DiaNeutroConservaLaSecuenciaYUnFalloCerradoLaCorta()
    {
        var resultado = CalcularRachas.General(Lunes.AddDays(5),
        [
            new(Lunes, 4, 3),
            new(Lunes.AddDays(1), 0, 0),
            new(Lunes.AddDays(2), 7, 5),
            new(Lunes.AddDays(3), 3, 2),
            new(Lunes.AddDays(4), 2, 2)
        ]);

        Assert.Equal(Disponible(1), resultado.Actual);
        Assert.Equal(Disponible(2), resultado.Mejor);
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 3)]
    public void HoyYAyer_SumanAlCumplirseYLaFaltaCortaAlVencer(bool ayer, bool hoy, int actual)
    {
        var corte = Lunes.AddDays(2);
        DiaRachaIndividual[] individual =
        [
            new(Lunes, true, true),
            new(Lunes.AddDays(1), true, ayer),
            new(corte, true, hoy)
        ];
        DiaRachaGeneral[] general =
        [
            new(Lunes, 4, 3),
            new(Lunes.AddDays(1), 4, ayer ? 3 : 2),
            new(corte, 4, hoy ? 3 : 2)
        ];

        var esperado = new ResultadoRachas(Disponible(actual), Disponible(actual));
        Assert.Equal(esperado, CalcularRachas.Individual(corte, activo: true, individual));
        Assert.Equal(esperado, CalcularRachas.General(corte, general));

        var actualManana = ayer ? actual : hoy ? 1 : 0;
        var despuesDelVencimiento = new ResultadoRachas(Disponible(actualManana), Disponible(ayer ? actual : 1));
        Assert.Equal(despuesDelVencimiento, CalcularRachas.Individual(corte.AddDays(1), activo: true, individual));
        Assert.Equal(despuesDelVencimiento, CalcularRachas.General(corte.AddDays(1), general));
    }

    [Fact]
    public void Individual_InactivoConservaLaMejorHistorica()
    {
        var resultado = CalcularRachas.Individual(Lunes.AddDays(4), activo: false,
        [
            new(Lunes, true, true),
            new(Lunes.AddDays(1), true, true),
            new(Lunes.AddDays(2), false, true)
        ]);

        Assert.Equal(new MetricaRacha(EstadoMetricaRacha.NoAplicable, null), resultado.Actual);
        Assert.Equal(Disponible(2), resultado.Mejor);
    }

    [Fact]
    public void FalloConocidoPermiteConocerActualAunqueLaMejorSeaDesconocida()
    {
        var corte = Lunes.AddDays(5);
        var esperado = new ResultadoRachas(Disponible(1), NoDisponible());
        Assert.Equal(esperado, CalcularRachas.Individual(corte, activo: true,
        [
            new(Lunes, true, true),
            new(Lunes.AddDays(1), null, true),
            new(Lunes.AddDays(2), true, false),
            new(Lunes.AddDays(3), true, true)
        ]));
        Assert.Equal(esperado, CalcularRachas.General(corte,
        [
            new(Lunes, 1, 1),
            new(Lunes.AddDays(1), 0, 0, DesconocidosConRegistro: 1),
            new(Lunes.AddDays(2), 1, 0),
            new(Lunes.AddDays(3), 1, 1)
        ]));
    }

    [Fact]
    public void MejorConocidaAunqueLaActualSeaDesconocida()
    {
        var resultado = CalcularRachas.Individual(Lunes.AddDays(5), activo: true,
        [
            new(Lunes, true, true),
            new(Lunes.AddDays(1), true, true),
            new(Lunes.AddDays(2), true, false),
            new(Lunes.AddDays(3), null, true)
        ]);

        Assert.Equal(NoDisponible(), resultado.Actual);
        Assert.Equal(Disponible(2), resultado.Mejor);
    }

    [Fact]
    public void General_ElUmbralPuedeSerSeguroAunqueExistanOportunidadesDesconocidas()
    {
        var resultado = CalcularRachas.General(Lunes.AddDays(3),
        [
            new(Lunes, 4, 4, DesconocidosSinRegistro: 1),
            new(Lunes.AddDays(1), 4, 0, DesconocidosConRegistro: 1)
        ]);

        Assert.Equal(Disponible(0), resultado.Actual);
        Assert.Equal(Disponible(1), resultado.Mejor);
    }

    [Fact]
    public void Individual_DesconocidoSinRegistroNoPuedeSumarYSoloPuedeCortarAlCerrar()
    {
        DiaRachaIndividual[] dias = [new(Lunes, true, true), new(Lunes.AddDays(1), null, false)];

        Assert.Equal(new ResultadoRachas(Disponible(1), Disponible(1)),
            CalcularRachas.Individual(Lunes.AddDays(2), activo: true, dias));
        Assert.Equal(new ResultadoRachas(NoDisponible(), Disponible(1)),
            CalcularRachas.Individual(Lunes.AddDays(3), activo: true, dias));
    }

    [Fact]
    public void SinOportunidades_DevuelveCeroConocido()
    {
        var esperado = new ResultadoRachas(Disponible(0), Disponible(0));
        Assert.Equal(esperado, CalcularRachas.General(Lunes, []));
        Assert.Equal(esperado, CalcularRachas.Individual(Lunes, activo: true, [new(Lunes, false, true)]));
    }

    [Fact]
    public void CalculaUnaSecuenciaDeSesentaDias()
    {
        var corte = Lunes.AddDays(59);
        var dias = Enumerable.Range(0, 60).Select(i => Lunes.AddDays(i)).ToArray();
        var esperado = new ResultadoRachas(Disponible(60), Disponible(60));

        Assert.Equal(esperado, CalcularRachas.Individual(corte, activo: true, dias.Select(f => new DiaRachaIndividual(f, true, true))));
        Assert.Equal(esperado, CalcularRachas.General(corte, dias.Select(f => new DiaRachaGeneral(f, 10, 7))));
    }

    private static MetricaRacha Disponible(int valor) => new(EstadoMetricaRacha.Disponible, valor);
    private static MetricaRacha NoDisponible() => new(EstadoMetricaRacha.NoDisponible, null);
}
