using Constia.Application.Habitos;

namespace Constia.Tests;

public sealed class ListarCategoriasHabitoTests
{
    [Fact]
    public async Task EjecutarAsync_DevuelveLaLecturaPersistidaYPropagaElToken()
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<CategoriaHabitoListado> esperado =
        [
            new(Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000005"), "Aprendizaje y crecimiento", 5)
        ];
        CancellationToken? tokenRecibido = null;
        var llamadas = 0;
        var repositorio = new FakeCategoriaReadRepository(token =>
        {
            tokenRecibido = token;
            llamadas++;
            return Task.FromResult(esperado);
        });
        var casoDeUso = new ListarCategoriasHabito(repositorio);

        var resultado = await casoDeUso.EjecutarAsync(cancellation.Token);

        Assert.Equal(esperado, resultado);
        Assert.Single(resultado);
        Assert.Equal(cancellation.Token, tokenRecibido);
        Assert.Equal(1, llamadas);
    }

    [Fact]
    public async Task EjecutarAsync_SinRegistros_NoSustituyePorElCatalogoEstatico()
    {
        var casoDeUso = new ListarCategoriasHabito(
            new FakeCategoriaReadRepository(_ => Task.FromResult<IReadOnlyList<CategoriaHabitoListado>>([])));

        var resultado = await casoDeUso.EjecutarAsync();

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task EjecutarAsync_ErrorDePersistencia_PropagaElErrorSinFallback()
    {
        var error = new InvalidOperationException("No se pudo consultar la tabla de categorías.");
        var casoDeUso = new ListarCategoriasHabito(new FakeCategoriaReadRepository(
            _ => Task.FromException<IReadOnlyList<CategoriaHabitoListado>>(error)));

        var resultado = await Assert.ThrowsAsync<InvalidOperationException>(() => casoDeUso.EjecutarAsync());

        Assert.Same(error, resultado);
    }

    [Fact]
    public async Task EjecutarAsync_Cancelado_ConservaLaCancelacionDelRepositorio()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var casoDeUso = new ListarCategoriasHabito(new FakeCategoriaReadRepository(
            token => Task.FromCanceled<IReadOnlyList<CategoriaHabitoListado>>(token)));

        var resultado = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => casoDeUso.EjecutarAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, resultado.CancellationToken);
    }

    private sealed class FakeCategoriaReadRepository(
        Func<CancellationToken, Task<IReadOnlyList<CategoriaHabitoListado>>> listar) : ICategoriaHabitoReadRepository
    {
        public Task<IReadOnlyList<CategoriaHabitoListado>> ListarAsync(CancellationToken cancellationToken) =>
            listar(cancellationToken);
    }
}
