using Constia.Domain;
using Constia.Application.Usuarios;

namespace Constia.Tests;

public sealed class VerificacionCorreoEstadoTests
{
    [Fact]
    public void Alta_NacePendienteSinFecha()
    {
        var u = new Usuario("Ana", "ana@example.invalid", "hash", "Etc/UTC");
        var propiedad = typeof(Usuario).GetProperty("EstadoVerificacionCorreo");
        Assert.NotNull(propiedad);
        Assert.Equal(1, Convert.ToInt32(propiedad.GetValue(u)));
        Assert.Null(typeof(Usuario).GetProperty("EmailVerificadoEnUtc")!.GetValue(u));
    }

    [Fact]
    public void Confirmar_EstableceFechaUtcYNoCambiaIdentidadNiHash()
    {
        var u = new Usuario("Ana", "ana@example.invalid", "hash", "Etc/UTC");
        var id = u.Id;
        var metodo = typeof(Usuario).GetMethod("ConfirmarCorreo");
        Assert.NotNull(metodo);
        var now = DateTimeOffset.Parse("2026-10-21T00:00:00Z");
        metodo.Invoke(u, [now]);
        Assert.Equal(2, Convert.ToInt32(typeof(Usuario).GetProperty("EstadoVerificacionCorreo")!.GetValue(u)));
        Assert.Equal(now, typeof(Usuario).GetProperty("EmailVerificadoEnUtc")!.GetValue(u));
        Assert.Equal(id, u.Id);
        Assert.Equal("hash", u.PasswordHash);
        Assert.Equal("Etc/UTC", u.TimeZoneId);
    }

    [Fact]
    public async Task LoginPendiente_ConPasswordCorrecta_NoEmiteJwt()
    {
        var u = new Usuario("Ana", "ana@example.invalid", "hash", "Etc/UTC");
        var tokens = new Tokens();
        var login = new AutenticarUsuario(new Usuarios(u), new Hasher(), tokens);
        var result = await login.EjecutarAsync(u.Email, "historica");
        Assert.NotNull(result);
        Assert.Equal("VerificacionRequerida", result.GetType().GetProperty("Estado")?.GetValue(result)?.ToString());
        Assert.Equal(0, tokens.Cantidad);
    }

    private sealed class Tokens : IAccessTokenService
    {
        public int Cantidad;
        public AccessToken Emitir(Guid id, string nombre, string email)
        {
            Cantidad++;
            return new("ficticio", DateTimeOffset.MaxValue);
        }
    }
    private sealed class Hasher : IUsuarioPasswordHasher
    {
        public string HashPassword(string p) => throw new NotSupportedException();
        public bool VerifyPassword(string hash, string p) => true;
    }
    private sealed class Usuarios(Usuario u) : IUsuarioRepository
    {
        public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Usuario?>(u);
        public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken ct) => Task.FromResult<Usuario?>(u);
        public Task<bool> ExistePorEmailAsync(string email, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> IntentarAgregarAsync(Usuario u, CancellationToken ct) => throw new NotSupportedException();
        public Task GuardarCambiosAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
