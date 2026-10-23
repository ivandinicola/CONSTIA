using Constia.API.Authentication;
using Constia.Application.Cumplimientos;
using Constia.Application.Habitos;
using Constia.Application.Usuarios;
using Constia.Application.Temporal;
using Constia.Infrastructure;
using Constia.Infrastructure.Habitos;
using Constia.Infrastructure.Cumplimientos;
using Constia.Infrastructure.Security;
using Constia.Infrastructure.Usuarios;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;

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
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IZonaHorariaIana, ZonaHorariaIana>();
builder.Services.AddSingleton<IFechaLocalUsuario, FechaLocalUsuario>();

builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IHabitoRepository, HabitoRepository>();
builder.Services.AddScoped<ICategoriaHabitoReadRepository, CategoriaHabitoReadRepository>();
builder.Services.AddScoped<ICumplimientoRepository, CumplimientoRepository>();
builder.Services.AddScoped<IEstadoHabitoReadinessRepository, EstadoHabitoReadinessRepository>();
builder.Services.AddScoped<IUsuarioActual, HttpContextUsuarioActual>();
builder.Services.AddScoped<IUsuarioPasswordHasher, AspNetCorePasswordHasher>();
builder.Services.AddScoped<IAccessTokenService, JwtAccessTokenService>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>().GetSection("VerificacionCorreo").Get<OpcionesVerificacionCorreo>() ?? new());
builder.Services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>().GetSection("VerificacionCorreo:Limites").Get<PoliticaVerificacionCorreo>() ?? new());
builder.Services.AddScoped<IVerificacionCorreoRepository, VerificacionCorreoRepository>();
builder.Services.AddSingleton<IGeneradorTokenVerificacion, GeneradorTokenVerificacion>();
Constia.API.Configuration.VerificacionCorreoC2ServiceCollectionExtensions.AddVerificacionCorreoC2(builder.Services);
builder.Services.AddScoped<IActivacionCorreo, ActivacionCorreo>();
builder.Services.AddScoped<ServicioVerificacionCorreo>();
builder.Services.AddScoped<IRegistroUsuarioCorreo>(sp => sp.GetRequiredService<ServicioVerificacionCorreo>());
builder.Services.AddRateLimiter(options =>
{
    var correoPolicy = builder.Configuration.GetSection("VerificacionCorreo:Limites").Get<PoliticaVerificacionCorreo>() ?? new();
    options.RejectionStatusCode = 429;
    options.AddPolicy("correo", context => RateLimitPartition.Get(
        context.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
        _ => new LimiteVentanaMovilCorreo(
            correoPolicy.EsValida ? correoPolicy.SolicitudesPorIp : 10,
            TimeSpan.FromMinutes(correoPolicy.EsValida ? correoPolicy.VentanaSolicitudesMinutos : 10),
            context.RequestServices.GetRequiredService<TimeProvider>())));
    options.OnRejected = async (context, ct) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = 429, Title = "Demasiadas solicitudes.",
            Extensions = { ["code"] = "rate_limited" }
        }, cancellationToken: ct);
    };
});
builder.Services.AddScoped<RegistrarUsuario>();
builder.Services.AddScoped<CambiarZonaHorariaUsuario>();
builder.Services.AddScoped<AutenticarUsuario>();
builder.Services.AddScoped<CrearHabito>();
builder.Services.AddScoped<ListarHabitosActivos>();
builder.Services.AddScoped<ListarCategoriasHabito>();
builder.Services.AddScoped<ListarIconosHabito>();
builder.Services.AddScoped<ListarHabitosInactivos>();
builder.Services.AddScoped<ObtenerHabitoPorId>();
builder.Services.AddScoped<EditarHabito>();
builder.Services.AddScoped<DesactivarHabito>();
builder.Services.AddScoped<RegistrarCumplimiento>();

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
Constia.API.Configuration.ResendC1ServiceCollectionExtensions.AddResendC1(builder.Services, builder.Configuration);

Constia.API.Configuration.BienvenidaC3AServiceCollectionExtensions.AddBienvenidaC3A(builder.Services, builder.Configuration);

Constia.API.Configuration.BienvenidaC3BServiceCollectionExtensions.AddBienvenidaC3B(builder.Services, builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
