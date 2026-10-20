using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Constia.Application.Usuarios;
using Constia.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Constia.API.Tests;

public sealed class HabitoMetadataHttpTests
{
    [Theory]
    [InlineData("activos", false, false)]
    [InlineData("activos", true, true)]
    [InlineData("inactivos", false, false)]
    [InlineData("inactivos", true, true)]
    [InlineData("detalle", false, false)]
    [InlineData("detalle", true, true)]
    [InlineData("detalle", true, false)]
    [InlineData("detalle", false, true)]
    [InlineData("editar", false, false)]
    [InlineData("editar", true, true)]
    public async Task LecturasYEdicion_ConservanMetadatosMaterializadosYContrato(
        string operacion, bool conCategoria, bool conIcono)
    {
        using var factory = new JwtApiFactory();
        Guid? categoriaId = conCategoria ? CatalogoCategoriasHabito.Todas[4].Id : null;
        string? iconoId = conIcono ? "libro" : null;
        var habito = HabitoMetadataMaterialization.Crear(factory.Usuario, categoriaId, iconoId);
        if (operacion == "inactivos") habito.Desactivar(new DateOnly(2026, 10, 2));
        factory.ReemplazarHabitos([habito]);
        using var client = ClienteAutenticado(factory);
        var creacion = habito.FechaCreacion;
        var programaciones = habito.Programaciones.ToArray();
        var periodos = habito.PeriodosEstado.ToArray();

        var response = operacion == "editar"
            ? await client.PutAsJsonAsync($"/api/habitos/{habito.Id:D}", new
            {
                nombre = habito.Nombre, descripcion = habito.Descripcion,
                diasProgramados = new[] { (int)DayOfWeek.Monday }
            })
            : await client.GetAsync(operacion switch
            {
                "activos" => "/api/habitos",
                "inactivos" => "/api/habitos/inactivos",
                _ => $"/api/habitos/{habito.Id:D}"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = operacion is "activos" or "inactivos" ? json.RootElement.EnumerateArray().Single() : json.RootElement;
        VerificarContrato(body, habito, categoriaId, iconoId);
        Assert.Equal(creacion, habito.FechaCreacion);
        Assert.Equal(programaciones, habito.Programaciones);
        Assert.Equal(periodos, habito.PeriodosEstado);
    }

    [Fact]
    public async Task Post_CalculaFechaLocalYExigeMetadatosOficiales_Devuelve201()
    {
        using var factory = new JwtApiFactory();
        using var client = ClienteAutenticado(factory);
        var response = await client.PostAsJsonAsync("/api/habitos", new
        {
            nombre = "Leer", descripcion = "Historia",
            categoriaId = CatalogoCategoriasHabito.Todas[4].Id, iconoId = "libro",
            diasProgramados = new[] { (int)DayOfWeek.Monday }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var habito = Assert.Single(factory.HabitosPersistidos);
        Assert.Equal(new DateOnly(2026, 10, 10), habito.FechaInicio);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        VerificarContrato(json.RootElement, habito, CatalogoCategoriasHabito.Todas[4].Id, "libro");
    }

    internal static HttpClient ClienteAutenticado(JwtApiFactory factory, Usuario? usuario = null)
    {
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var user = usuario ?? factory.Usuario;
        var token = scope.ServiceProvider.GetRequiredService<IAccessTokenService>().Emitir(user.Id, user.Nombre, user.Email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return client;
    }

    internal static void VerificarContrato(JsonElement body, Habito habito, Guid? categoriaId, string? iconoId)
    {
        Assert.Equal(new[] { "categoriaId", "createdAt", "descripcion", "diasProgramados", "estado", "iconoId", "id", "nombre", "startDate" },
            body.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(habito.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(habito.Nombre, body.GetProperty("nombre").GetString());
        Assert.Equal(habito.Descripcion, body.GetProperty("descripcion").GetString());
        Assert.Equal(habito.FechaCreacion, body.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(habito.FechaInicio.ToString("yyyy-MM-dd"), body.GetProperty("startDate").GetString());
        Assert.Equal((int)habito.Estado, body.GetProperty("estado").GetInt32());
        Assert.Equal(habito.DiasProgramados.Select(d => (int)d.Dia).Order(),
            body.GetProperty("diasProgramados").EnumerateArray().Select(d => d.GetInt32()).Order());
        if (categoriaId is null) Assert.Equal(JsonValueKind.Null, body.GetProperty("categoriaId").ValueKind);
        else Assert.Equal(categoriaId, body.GetProperty("categoriaId").GetGuid());
        if (iconoId is null) Assert.Equal(JsonValueKind.Null, body.GetProperty("iconoId").ValueKind);
        else Assert.Equal(iconoId, body.GetProperty("iconoId").GetString());
    }
}
