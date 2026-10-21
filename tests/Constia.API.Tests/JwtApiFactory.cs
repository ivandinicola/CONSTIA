using System.Collections.Concurrent;
using Constia.Application.Cumplimientos;
using Constia.Application.Habitos;
using Constia.Application.Usuarios;
using Constia.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
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

    public Usuario Usuario { get; } = new("Test User", "test@example.invalid", "not-a-real-password-hash", "Etc/UTC");

    public ConcurrentDictionary<Guid, Usuario> UsuariosPersistidos { get; } = new();
    internal object CorreoLock { get; } = new();
    internal ConcurrentDictionary<Guid, DesafioVerificacionCorreo> DesafiosCorreo { get; } = new();
    internal ConcurrentQueue<(Guid Id,string Email,string Token)> CorreosDePrueba { get; } = new();
    internal bool EsquemaCorreoDisponible { get; set; } = true;
    internal bool FallarEntregaCorreo { get; set; }

    private int _cantidadDeCambiosGuardados;
    private int _cantidadDeHabitosGuardados;
    private int _cantidadDeCumplimientosGuardados;

    public int CantidadDeCambiosGuardados => Volatile.Read(ref _cantidadDeCambiosGuardados);

    public int CantidadDeHabitosGuardados => Volatile.Read(ref _cantidadDeHabitosGuardados);
    public int CantidadDeCumplimientosGuardados => Volatile.Read(ref _cantidadDeCumplimientosGuardados);

    public ConcurrentDictionary<(Guid HabitId, DateOnly Fecha), Cumplimiento> CumplimientosPersistidos { get; } = new();

    public ConcurrentQueue<Habito> HabitosPersistidos { get; } = new();

    public HashSet<Guid> CategoriasNoDisponibles { get; } = [];

    public bool SnapshotCapturado { get; set; } = true;

    public bool HistorialCompleto { get; set; } = true;

    public JwtApiFactory()
    {
        Usuario.ConfirmarCorreo(DateTimeOffset.Parse("2026-10-10T00:00:00Z"));
        UsuariosPersistidos[Usuario.Id] = Usuario;
    }

    public void ReemplazarHabitos(IEnumerable<Habito> habitos)
    {
        HabitosPersistidos.Clear();
        foreach (var habito in habitos)
        {
            HabitosPersistidos.Enqueue(habito);
        }
    }

    public void LimpiarCumplimientosPersistidos()
    {
        CumplimientosPersistidos.Clear();
        Interlocked.Exchange(ref _cantidadDeCumplimientosGuardados, 0);
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
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["VerificacionCorreo:PublicRegistrationEnabled"] = "true",
            ["VerificacionCorreo:PublicUrl"] = "https://constia.example"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IVerificacionCorreoRepository>();
            services.AddScoped<IVerificacionCorreoRepository>(sp => new CorreoRepositorioFalso(this,
                sp.GetRequiredService<IUsuarioPasswordHasher>(),sp.GetRequiredService<TimeProvider>()));
            services.RemoveAll<IEntregaCorreoVerificacion>();
            services.AddScoped<IEntregaCorreoVerificacion>(_ => new EntregaCorreoFalsa(this));
            services.RemoveAll<IUsuarioRepository>();
            services.AddScoped<IUsuarioRepository>(_ => new FakeUsuarioRepository(UsuariosPersistidos, RegistrarCambioGuardado));
            services.RemoveAll<IUsuarioPasswordHasher>();
            services.AddScoped<IUsuarioPasswordHasher, FakeUsuarioPasswordHasher>();
            services.RemoveAll<ICumplimientoRepository>();
            services.AddScoped<ICumplimientoRepository>(_ => new FakeCumplimientoRepository(
                CumplimientosPersistidos,
                RegistrarCumplimientoGuardado));
            services.RemoveAll<IHabitoRepository>();
            services.AddScoped<IHabitoRepository>(_ => new FakeHabitoRepository(
                HabitosPersistidos,
                RegistrarHabitoGuardado));
            services.RemoveAll<ICategoriaHabitoReadRepository>();
            services.AddScoped<ICategoriaHabitoReadRepository>(_ => new FakeCategoriaHabitoReadRepository(this));
            services.RemoveAll<IEstadoHabitoReadinessRepository>();
            services.AddScoped<IEstadoHabitoReadinessRepository>(_ => new FakeEstadoHabitoReadinessRepository(this));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new TimeProviderFijo(
                DateTimeOffset.Parse("2026-10-10T02:30:00+00:00")));
        });
    }

    private void RegistrarCambioGuardado()
    {
        Interlocked.Increment(ref _cantidadDeCambiosGuardados);
    }

    private void RegistrarHabitoGuardado()
    {
        Interlocked.Increment(ref _cantidadDeHabitosGuardados);
    }

    private void RegistrarCumplimientoGuardado()
    {
        Interlocked.Increment(ref _cantidadDeCumplimientosGuardados);
    }

    private sealed class FakeCumplimientoRepository(
        ConcurrentDictionary<(Guid HabitId, DateOnly Fecha), Cumplimiento> cumplimientos,
        Action registrarGuardado) : ICumplimientoRepository
    {
        public Task<bool> ExistePorHabitoYFechaAsync(
            Guid habitId,
            DateOnly fecha,
            CancellationToken cancellationToken) =>
            Task.FromResult(cumplimientos.ContainsKey((habitId, fecha)));

        public Task AgregarAsync(Cumplimiento cumplimiento, CancellationToken cancellationToken)
        {
            var key = (cumplimiento.Habito.Id, cumplimiento.Fecha);
            if (!cumplimientos.TryAdd(key, cumplimiento))
            {
                throw new InvalidOperationException("A completion already exists for the habit and date.");
            }

            registrarGuardado();
            return Task.CompletedTask;
        }
    }
    private sealed class FakeUsuarioRepository(
        ConcurrentDictionary<Guid, Usuario> usuarios,
        Action registrarCambioGuardado) : IUsuarioRepository
    {
        public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken cancellationToken)
        {
            usuarios.TryGetValue(id, out var usuario);
            return Task.FromResult(usuario);
        }

        public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken cancellationToken)
        {
            return Task.FromResult<Usuario?>(usuarios.Values.SingleOrDefault(usuario => usuario.Email == email));
        }

        public Task<bool> ExistePorEmailAsync(string email, CancellationToken cancellationToken)
        {
            return Task.FromResult(usuarios.Values.Any(usuario => usuario.Email == email));
        }

        public Task<bool> IntentarAgregarAsync(Usuario usuario, CancellationToken cancellationToken)
        {
            if (usuarios.Values.Any(existente => existente.Email == usuario.Email))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(usuarios.TryAdd(usuario.Id, usuario));
        }

        public Task GuardarCambiosAsync(CancellationToken cancellationToken)
        {
            registrarCambioGuardado();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUsuarioPasswordHasher : IUsuarioPasswordHasher
    {
        public string HashPassword(string password)
        {
            return $"test-hash:{password}";
        }

        public bool VerifyPassword(string passwordHash, string password)
        {
            return passwordHash == "not-a-real-password-hash" && password == TestPassword;
        }
    }

    private sealed class FakeCategoriaHabitoReadRepository(JwtApiFactory factory) : ICategoriaHabitoReadRepository
    {
        public Task<bool> ExisteAsync(Guid id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CatalogoCategoriasHabito.EsOficial(id) && !factory.CategoriasNoDisponibles.Contains(id));
        }

        public Task<IReadOnlyList<CategoriaHabitoListado>> ListarAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<CategoriaHabitoListado> categorias = CatalogoCategoriasHabito.Todas
                .OrderBy(categoria => categoria.Orden)
                .Select(categoria => new CategoriaHabitoListado(categoria.Id, categoria.Nombre, categoria.Orden))
                .ToArray();
            return Task.FromResult(categorias);
        }
    }

    private sealed class FakeEstadoHabitoReadinessRepository(JwtApiFactory factory)
        : IEstadoHabitoReadinessRepository
    {
        public Task<bool> SnapshotCapturadoAsync(CancellationToken cancellationToken) =>
            Task.FromResult(factory.SnapshotCapturado);

        public Task<bool> HistorialCompletoAsync(CancellationToken cancellationToken) =>
            Task.FromResult(factory.HistorialCompleto);
    }

    private sealed class FakeHabitoRepository(
        ConcurrentQueue<Habito> habitos,
        Action registrarGuardado) : IHabitoRepository
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

        public Task<IReadOnlyList<Habito>> ListarInactivosPorUsuarioAsync(
            Guid usuarioId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<Habito> resultados = habitos
                .Where(habito =>
                    habito.Usuario.Id == usuarioId &&
                    habito.Estado == EstadoHabito.Inactivo)
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

        public Task<Habito?> BuscarParaModificarPorIdYUsuarioAsync(
            Guid habitId,
            Guid usuarioId,
            CancellationToken cancellationToken)
        {
            var resultado = habitos.SingleOrDefault(habito =>
                habito.Id == habitId && habito.Usuario.Id == usuarioId);

            return Task.FromResult(resultado);
        }

        public Task GuardarCambiosAsync(Habito habito, CancellationToken cancellationToken)
        {
            if (!habitos.Contains(habito))
            {
                throw new InvalidOperationException("The habit must be part of the fake repository.");
            }

            registrarGuardado();
            return Task.CompletedTask;
        }
    }

    private sealed class TimeProviderFijo(DateTimeOffset instanteUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instanteUtc;
    }
}
