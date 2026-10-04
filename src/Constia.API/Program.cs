using Constia.Application.Usuarios;
using Constia.Infrastructure;
using Constia.Infrastructure.Security;
using Constia.Infrastructure.Usuarios;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<ConstiaDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IUsuarioPasswordHasher, AspNetCorePasswordHasher>();
builder.Services.AddScoped<RegistrarUsuario>();
builder.Services.AddScoped<AutenticarUsuario>();

builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
