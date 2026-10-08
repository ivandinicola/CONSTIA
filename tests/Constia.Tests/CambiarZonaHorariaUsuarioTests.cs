using Constia.Application.Temporal;
using Constia.Application.Usuarios;
using Constia.Domain;

namespace Constia.Tests;

public sealed class CambiarZonaHorariaUsuarioTests
{
    [Fact]
    public async Task EjecutarAsync_CambiaSoloLaZonaDelUsuarioSolicitadoYPersiste()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hash", "Etc/UTC");
        var otroUsuario = new Usuario("Bea", "bea@example.com", "hash", "Etc/UTC");
        var repositorio = new FakeUsuarioRepository(usuario);
        var casoDeUso = new CambiarZonaHorariaUsuario(repositorio, new ZonaHorariaIana());

        var resultado = await casoDeUso.EjecutarAsync(usuario.Id, "Asia/Tokyo");

        Assert.True(resultado);
        Assert.Equal("Asia/Tokyo", usuario.TimeZoneId);
        Assert.Equal("Etc/UTC", otroUsuario.TimeZoneId);
        Assert.Equal(1, repositorio.CantidadDeGuardados);
    }

    [Fact]
    public async Task EjecutarAsync_ConZonaInvalida_NoBuscaNiGuarda()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hash", "Etc/UTC");
        var repositorio = new FakeUsuarioRepository(usuario);
        var casoDeUso = new CambiarZonaHorariaUsuario(repositorio, new ZonaHorariaIana());

        await Assert.ThrowsAsync<ZonaHorariaIanaInvalidaException>(() =>
            casoDeUso.EjecutarAsync(usuario.Id, "Eastern Standard Time"));

        Assert.Equal(0, repositorio.CantidadDeBusquedas);
        Assert.Equal(0, repositorio.CantidadDeGuardados);
        Assert.Equal("Etc/UTC", usuario.TimeZoneId);
    }

    [Fact]
    public async Task EjecutarAsync_ConZonaMayorAlLimite_NoBuscaNiGuarda()
    {
        var usuario = new Usuario("Ana", "ana@example.com", "hash", "Etc/UTC");
        var repositorio = new FakeUsuarioRepository(usuario);
        var casoDeUso = new CambiarZonaHorariaUsuario(repositorio, new ZonaHorariaIana());

        await Assert.ThrowsAsync<ZonaHorariaIanaInvalidaException>(() =>
            casoDeUso.EjecutarAsync(
                usuario.Id,
                new string('x', ZonaHorariaIana.LongitudMaximaIdentificador + 1)));

        Assert.Equal(0, repositorio.CantidadDeBusquedas);
        Assert.Equal(0, repositorio.CantidadDeGuardados);
        Assert.Equal("Etc/UTC", usuario.TimeZoneId);
    }

    [Fact]
    public async Task EjecutarAsync_SiElUsuarioNoExiste_DevuelveFalseYSinGuardar()
    {
        var repositorio = new FakeUsuarioRepository(null);
        var casoDeUso = new CambiarZonaHorariaUsuario(repositorio, new ZonaHorariaIana());

        var resultado = await casoDeUso.EjecutarAsync(Guid.NewGuid(), "Asia/Tokyo");

        Assert.False(resultado);
        Assert.Equal(0, repositorio.CantidadDeGuardados);
    }

    private sealed class FakeUsuarioRepository(Usuario? usuario) : IUsuarioRepository
    {
        public int CantidadDeBusquedas { get; private set; }

        public int CantidadDeGuardados { get; private set; }

        public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken cancellationToken)
        {
            CantidadDeBusquedas++;
            return Task.FromResult<Usuario?>(usuario?.Id == id ? usuario : null);
        }

        public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistePorEmailAsync(string email, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> IntentarAgregarAsync(Usuario usuario, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task GuardarCambiosAsync(CancellationToken cancellationToken)
        {
            CantidadDeGuardados++;
            return Task.CompletedTask;
        }
    }
}
