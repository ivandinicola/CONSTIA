using Constia.Application.Usuarios;
using Constia.Application.Habitos;
using Constia.API.Authentication;
using Constia.Infrastructure;
using Constia.Infrastructure.Habitos;
using Constia.Infrastructure.Security;
using Constia.Infrastructure.Usuarios;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found.");

var jwtSettings = builder.Configuration
    .GetSection(JwtSettings.SectionName)
    .Get<JwtSettings>()
    ?? throw new InvalidOperationException("JWT settings were not configured.");
jwtSettings.Validate();

builder.Services.AddDbContext<ConstiaDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IHabitoRepository, HabitoRepository>();
builder.Services.AddScoped<IUsuarioActual, HttpContextUsuarioActual>();
builder.Services.AddScoped<IUsuarioPasswordHasher, AspNetCorePasswordHasher>();
builder.Services.AddScoped<IAccessTokenService, JwtAccessTokenService>();
builder.Services.AddScoped<RegistrarUsuario>();
builder.Services.AddScoped<AutenticarUsuario>();
builder.Services.AddScoped<CrearHabito>();
builder.Services.AddScoped<ListarHabitosActivos>();
builder.Services.AddScoped<ObtenerHabitoPorId>();

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey));
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
    });

builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
