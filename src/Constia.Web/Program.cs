using Constia.Web.Components;
using Constia.Web.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

const string SessionId = "constia-session-id";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOptions<ApiOptions>()
    .BindConfiguration(ApiOptions.SectionName)
    .Validate(options => string.IsNullOrWhiteSpace(options.BaseUrl) ||
        (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) &&
         (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
         string.IsNullOrEmpty(uri.UserInfo) &&
         string.IsNullOrEmpty(uri.Query) &&
         string.IsNullOrEmpty(uri.Fragment)),
        "Api:BaseUrl debe ser una URL HTTP(S) absoluta, sin credenciales, consulta ni fragmento.")
    .ValidateOnStart();

builder.Services.AddHttpClient(ApiOptions.ClientName, (services, client) =>
{
    var options = services.GetRequiredService<IOptions<ApiOptions>>().Value;
    if (string.IsNullOrWhiteSpace(options.BaseUrl))
    {
        throw new InvalidOperationException(
            "Configurá Api:BaseUrl antes de consumir Constia.API.");
    }

    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
});

// shortcut: sesiones de un solo proceso; usar almacenamiento y claves compartidas antes de desplegar réplicas.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<Constia.Web.Habitos.CumplirHabito>();
builder.Services.AddSingleton<Constia.Web.Habitos.GestionHabitos>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Constia.Web.Habitos.AltaHabito>();
builder.Services.AddSingleton<Constia.Web.Habitos.ConsultarCalendario>();
builder.Services.AddSingleton<Constia.Web.Habitos.ConsultarHistorialHabito>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.Cookie.Name = "__Host-Constia.Session";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.SlidingExpiration = false;
        options.Events.OnSigningIn = context =>
        {
            var expiresAt = context.Properties.ExpiresUtc
                ?? throw new InvalidOperationException("La sesión necesita un vencimiento absoluto.");
            var id = Guid.NewGuid().ToString("N");
            context.Properties.SetString(SessionId, id);
            context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>().Set(id, true, expiresAt);
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = context =>
        {
            var sessions = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            if (context.Properties.GetString(SessionId) is not { } id || !sessions.TryGetValue(id, out _))
                context.RejectPrincipal();

            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost("/logout", async (HttpContext http, IAntiforgery antiforgery, IMemoryCache sessions) =>
{
    if (!await antiforgery.IsRequestValidAsync(http))
        return Results.BadRequest();

    var session = await http.AuthenticateAsync();
    if (session.Properties?.GetString(SessionId) is { } id)
        sessions.Remove(id);

    await http.SignOutAsync();
    return Results.LocalRedirect("/login");
}).RequireAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
