using System.Collections.Concurrent;
using Constia.Application.Habitos;
using Constia.Application.Usuarios;
using Constia.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Constia.API.Tests;

public sealed class JwtApiFactory : WebApplicationFactory<Program>
{
    public const string TestIssuer = "constia-tests";
    public const string TestAudience = "constia-api-tests";
    public const string TestPassword = "fictitious-test-password";
    private const string TestSigningKey = "constia-test-signing-key-not-a-secret-32-bytes";

    public string SigningKey => TestSigningKey;

    public Usuario Usuario { get; } = new("Test User", "test@example.invalid", "not-a-real-password-hash");

    public ConcurrentQueue<Habito> HabitosPersistidos { get; } = new();

    public void ReemplazarHabitos(IEnumerable<Habito> habitos)
    {
        HabitosPersistidos.Clear();
        foreach (var habito in habitos)
        {
            HabitosPersistidos.Enqueue(habito);
        }
    }

    static JwtApiFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Server=unused;Database=unused;Trusted_Connection=True");
        Environment.SetEnvironmentVariable("JwtSettings__SigningKey", TestSigningKey);
        Environment.SetEnvironmentVariable("JwtSettings__Issuer", TestIssuer);
        Environment.SetEnvironmentVariable("JwtSettings__Audience", TestAudience);
        Environment.SetEnvironmentVariable("JwtSettings__ExpirationMinutes", "10");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUsuarioRepository>();
            services.AddScoped<IUsuarioRepository>(_ => new FakeUsuarioRepository(Usuario));
            services.RemoveAll<IUsuarioPasswordHasher>();
            services.AddScoped<IUsuarioPasswordHasher, FakeUsuarioPasswordHasher>();
            services.RemoveAll<IHabitoRepository>();
            services.AddScoped<IHabitoRepository>(_ => new FakeHabitoRepository(HabitosPersistidos));
        });
    }

    private sealed class FakeUsuarioRepository(Usuario usuario) : IUsuarioRepository
    {
        public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult<Usuario?>(usuario.Id == id ? usuario : null);
        }

        public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken cancellationToken)
        {
            return Task.FromResult<Usuario?>(usuario.Email == email ? usuario : null);
        }

        public Task<bool> ExistePorEmailAsync(string email, CancellationToken cancellationToken)
        {
            return Task.FromResult(usuario.Email == email);
        }

        public Task<bool> IntentarAgregarAsync(Usuario usuario, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeUsuarioPasswordHasher : IUsuarioPasswordHasher
    {
        public string HashPassword(string password)
        {
            throw new NotSupportedException();
        }

        public bool VerifyPassword(string passwordHash, string password)
        {
            return passwordHash == "not-a-real-password-hash" && password == TestPassword;
        }
    }

    private sealed class FakeHabitoRepository(ConcurrentQueue<Habito> habitos) : IHabitoRepository
    {
        public Task AgregarAsync(Habito habito, CancellationToken cancellationToken)
        {
            habitos.Enqueue(habito);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Habito>> ListarActivosPorUsuarioAsync(
            Guid usuarioId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<Habito> resultados = habitos
                .Where(habito =>
                    habito.Usuario.Id == usuarioId &&
                    habito.Estado == EstadoHabito.Activo)
                .OrderBy(habito => habito.FechaCreacion)
                .ThenBy(habito => habito.Id)
                .ToArray();

            return Task.FromResult(resultados);
        }

        public Task<Habito?> BuscarPorIdYUsuarioAsync(
            Guid habitId,
            Guid usuarioId,
            CancellationToken cancellationToken)
        {
            var resultado = habitos.SingleOrDefault(habito =>
                habito.Id == habitId && habito.Usuario.Id == usuarioId);

            return Task.FromResult(resultado);
        }

        public Task<Habito?> BuscarParaEditarPorIdYUsuarioAsync(
            Guid habitId,
            Guid usuarioId,
            CancellationToken cancellationToken)
        {
            var resultado = habitos.SingleOrDefault(habito =>
                habito.Id == habitId && habito.Usuario.Id == usuarioId);

            return Task.FromResult(resultado);
        }

        public Task GuardarCambiosAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
