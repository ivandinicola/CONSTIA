using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using Constia.Application.Correo;
using Constia.Application.Usuarios;
using Constia.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Constia.API.Tests;

public sealed class LoginReenvioCamposHttpTests
{
    public static IEnumerable<object[]> ObligatoriosLogin() =>
        from campo in new[] { "email", "password" }
        from particion in new[] { "omitido", "null", "vacio", "blanco" }
        select new object[] { campo, particion };

    [Theory]
    [MemberData(nameof(ObligatoriosLogin))]
    public Task Login_ObligatorioInvalido_400SinEfectos(string campo, string particion) =>
        Rechazo(false, campo, particion);

    [Theory]
    [InlineData("omitido")]
    [InlineData("null")]
    [InlineData("vacio")]
    [InlineData("blanco")]
    public Task Reenvio_ObligatorioInvalido_400SinEfectos(string particion) =>
        Rechazo(true, "email", particion);

    [Fact]
    public Task Reenvio_Email255_400SinEfectos() => Rechazo(true, "email", "255");

    private static async Task Rechazo(bool reenvio, string campo, string particion)
    {
        using var f = new JwtApiFactory();
        var canal = new CanalObservado();
        var jwt = new TokensObservados();
        using var host = f.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<ICanalVerificacionCorreo>(); s.AddSingleton<ICanalVerificacionCorreo>(canal);
            s.RemoveAll<IAccessTokenService>(); s.AddSingleton<IAccessTokenService>(jwt);
            s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(new Reloj());
        }));
        using var client = host.CreateClient();
        var antes = Snapshot(f);
        var datos = new Dictionary<string, string?> { ["email"] = f.Usuario.Email };
        if (!reenvio) datos["password"] = JwtApiFactory.TestPassword;
        if (particion == "omitido") datos.Remove(campo);
        else datos[campo] = particion switch { "null" => null, "vacio" => "", "blanco" => "   ", _ => Email(255) };
        using var response = await client.PostAsJsonAsync(reenvio ? "/api/auth/email/reenviar" : "/api/auth/login", datos);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var p = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(p); Assert.Equal(400, p.Status);
        Assert.Equal(campo == "email" ? "Email" : "Password", Assert.Single(p.Errors).Key);
        Assert.NotEmpty(Assert.Single(p.Errors).Value);
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync());
        await Drenar(canal);
        Assert.Equal(antes, Snapshot(f));
        Assert.Equal(0, f.CantidadDeUsuariosGuardados); Assert.Equal(0, f.ReenviosGuardados);
        Assert.Equal(0, jwt.Cantidad);
        Assert.Equal(0, canal.Reservas); Assert.Equal(0, canal.Publicaciones); Assert.Equal(0, canal.Trabajos);
        Assert.Empty(f.CorreosDePrueba);
    }

    [Fact]
    public async Task Reenvio_Email254_202ReemplazaDesafioYEntregaFalsa()
    {
        using var f = new JwtApiFactory(); var canal = new CanalObservado();
        var user = new Usuario("Pendiente", Email(254), "hash-historico", "Etc/UTC");
        var anterior = new DesafioVerificacionCorreo(user.Id, user.Email, new byte[32], Reloj.Ahora.AddMinutes(-2));
        f.UsuariosPersistidos[user.Id] = user; f.DesafiosCorreo[anterior.Id] = anterior;
        var usuarios = JsonSerializer.Serialize(f.UsuariosPersistidos.OrderBy(x => x.Key));
        using var host = f.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<ICanalVerificacionCorreo>(); s.AddSingleton<ICanalVerificacionCorreo>(canal);
            s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(new Reloj());
        }));
        using var client = host.CreateClient();
        using var r = await client.PostAsJsonAsync("/api/auth/email/reenviar", new { email = user.Email });
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        using var body = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal("mensaje", Assert.Single(body.RootElement.EnumerateObject()).Name);
        Assert.Equal("Si corresponde, recibirás un correo con los próximos pasos.", body.RootElement.GetProperty("mensaje").GetString());
        await Drenar(canal);
        Assert.Equal(usuarios, JsonSerializer.Serialize(f.UsuariosPersistidos.OrderBy(x => x.Key)));
        Assert.Equal(0, f.CantidadDeUsuariosGuardados); Assert.Equal(1, f.ReenviosGuardados);
        Assert.Equal(1, canal.Reservas); Assert.Equal(1, canal.Publicaciones); Assert.Equal(1, canal.Trabajos);
        Assert.Equal(Reloj.Ahora, anterior.InvalidadoEnUtc);
        var nuevo = Assert.Single(f.DesafiosCorreo.Values, d => d.Id != anterior.Id);
        Assert.Equal(user.Id, nuevo.UsuarioId); Assert.Equal(user.Email, nuevo.EmailDestino);
        Assert.Equal(Reloj.Ahora, nuevo.EmitidoEnUtc); Assert.Null(nuevo.ConsumidoEnUtc);
        var correo = Assert.Single(f.CorreosDePrueba);
        Assert.Equal(nuevo.Id, correo.Id); Assert.Equal(user.Email, correo.Email);
    }

    private static string Snapshot(JwtApiFactory f) => JsonSerializer.Serialize(new
    {
        usuarios = f.UsuariosPersistidos.OrderBy(x => x.Key), desafios = f.DesafiosCorreo.OrderBy(x => x.Key)
    });
    private static string Email(int longitud)
    {
        var value = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', longitud - 201) + ".invalid";
        Assert.Equal(longitud, value.Length); Assert.True(MailAddress.TryCreate(value, out var parsed));
        Assert.Equal(value, parsed.Address); Assert.Empty(parsed.DisplayName);
        Assert.All(value.Split('@')[1].Split('.'), x => Assert.InRange(x.Length, 1, 63));
        return value;
    }
    private static async Task Drenar(ICanalVerificacionCorreo c)
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await c.EsperarVacioAsync(ct.Token);
    }
    private sealed class Reloj : TimeProvider
    {
        public static readonly DateTimeOffset Ahora = DateTimeOffset.Parse("2026-11-27T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Ahora;
    }
    private sealed class TokensObservados : IAccessTokenService
    {
        public int Cantidad;
        public AccessToken Emitir(Guid id, string name, string email)
        { Interlocked.Increment(ref Cantidad); return new("synthetic", Reloj.Ahora.AddMinutes(10)); }
    }
    private sealed class CanalObservado : ICanalCorreoDePrueba
    {
        private readonly CanalCorreoFalso _inner = new();
        public int Reservas, Publicaciones, Trabajos;
        public bool Disponible => _inner.Disponible;
        public IReservaTrabajoCorreo? Reservar()
        { Interlocked.Increment(ref Reservas); var r = _inner.Reservar(); return r is null ? null : new Reserva(this, r); }
        public void Iniciar() => _inner.Iniciar();
        public void CerrarAdmision() => _inner.CerrarAdmision();
        public void Fallar() => _inner.Fallar();
        public Task EsperarVacioAsync(CancellationToken ct) => _inner.EsperarVacioAsync(ct);
        public async ValueTask<TrabajoVerificacionCorreo?> LeerAsync(CancellationToken ct)
        { var t = await _inner.LeerAsync(ct); if (t is not null) Interlocked.Increment(ref Trabajos); return t; }
        private sealed class Reserva(CanalObservado owner, IReservaTrabajoCorreo inner) : IReservaTrabajoCorreo
        {
            public void Publicar(TrabajoVerificacionCorreo t) { Interlocked.Increment(ref owner.Publicaciones); inner.Publicar(t); }
            public void Dispose() => inner.Dispose();
        }
    }
}
