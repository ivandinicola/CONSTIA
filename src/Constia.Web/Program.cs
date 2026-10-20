using Constia.Web.Components;
using Constia.Web.Configuration;
using Microsoft.Extensions.Options;

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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
