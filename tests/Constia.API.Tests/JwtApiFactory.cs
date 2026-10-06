using System.Security.Cryptography;
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

    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public Usuario Usuario { get; } = new("Test User", "test@example.invalid", "not-a-real-password-hash");

    private readonly Dictionary<string, string?> _previousEnvironmentValues = [];

    public JwtApiFactory()
    {
        SetEnvironmentValue("ConnectionStrings__DefaultConnection", "Server=unused;Database=unused;Trusted_Connection=True");
        SetEnvironmentValue("JwtSettings__SigningKey", SigningKey);
        SetEnvironmentValue("JwtSettings__Issuer", TestIssuer);
        SetEnvironmentValue("JwtSettings__Audience", TestAudience);
        SetEnvironmentValue("JwtSettings__ExpirationMinutes", "10");
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
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var (key, value) in _previousEnvironmentValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        base.Dispose(disposing);
    }

    private void SetEnvironmentValue(string key, string value)
    {
        _previousEnvironmentValues[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
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
}
