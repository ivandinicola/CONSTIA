using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Constia.Web.Habitos;
using Microsoft.Extensions.Caching.Memory;

namespace Constia.Web.Tests;

public sealed class AltaHabitoTests
{
    private static readonly Guid Categoria = Guid.Parse("82c0b8e6-0b9d-4e18-9bf8-000000000005");
    private static readonly DatosAlta Datos = new("  Leer  ", Categoria, "billete", [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Monday]);
    private static readonly CategoriaAlta[] Categorias = Enumerable.Range(1,10).Select(i => new CategoriaAlta(Guid.Parse($"82c0b8e6-0b9d-4e18-9bf8-{i:D12}"), $"Categoría {i}", i)).ToArray();
    private static readonly string[] Iconos = ["libro","fuerza","agua","loto","manzana","luna","cafe","corredor","corazon","cerebro","sol","huella","brote","portatil","musica","billete","estiramientos","higiene-bucal","medicacion","diario","hogar","planificacion","cuidado-personal","caminata","idiomas","desconexion-digital","vinculos","bicicleta"];
    private static HttpResponseMessage Json<T>(T data, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(data) };
    private static HabitoGestion Creado(DatosAlta d) => new(Guid.NewGuid(), d.Nombre!, null, new(2026,11,8,0,5,0,TimeSpan.Zero), new(2026,11,7), 0, d.DiasProgramados.Distinct().ToArray(), d.CategoriaId, d.IconoId);

    [Fact]
    public async Task Nuevo_SinSelecciones_AisladoPorSesionYUsuario()
    {
        using var s = new Escenario(); var b = await s.Nuevo();
        Assert.Equal(1,b.Paso); Assert.Null(b.Datos.CategoriaId); Assert.Null(b.Datos.IconoId); Assert.Empty(b.Datos.DiasProgramados);
        Assert.Null(s.Service.Leer(s.User,"otra",b.Id)); Assert.Null(s.Service.Leer(Guid.NewGuid(),s.Session,b.Id));
        Assert.Null(s.Service.Leer(s.User,s.Session,Guid.NewGuid())); Assert.Equal(2,s.Handler.Get);
    }
    [Fact]
    public async Task Recorrido_VolverConservaDatos_SoloCrearEnviaJSONContrato()
    {
        using var s = new Escenario(); var b = await s.Resumen();
        Assert.Equal(4,b.Paso); Assert.Equal(0,s.Handler.Post);
        b = (await s.Actuar(b,"volver"))!; Assert.Equal(3,b.Paso);
        Assert.Equal("  Leer  ",b.Datos.Nombre); Assert.Equal("billete",b.Datos.IconoId); Assert.Equal(Categoria,b.Datos.CategoriaId);
        b = (await s.Actuar(b,"continuar",Datos))!;
        s.Handler.Responder = async r => { var json = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
            Assert.Equal(new[]{"categoriaId","diasProgramados","iconoId","nombre"},json.RootElement.EnumerateObject().Select(p=>p.Name).Order().ToArray());
            Assert.Equal("  Leer  ",json.RootElement.GetProperty("nombre").GetString());
            Assert.Equal(new[]{1,0},json.RootElement.GetProperty("diasProgramados").EnumerateArray().Select(v=>v.GetInt32()).ToArray());
            return Json(Creado(Datos),HttpStatusCode.Created); };
        b = (await s.Actuar(b,"crear"))!;
        Assert.Equal("confirmado",b.Estado); Assert.Equal(new DateOnly(2026,11,7),b.Resultado!.StartDate); Assert.Equal(1,s.Handler.Post);
        var recargado = s.Service.Leer(s.User,s.Session,b.Id)!;
        await s.Actuar(recargado,"crear"); Assert.Equal(1,s.Handler.Post);
        Assert.Equal(b.Resultado,recargado.Resultado);
    }
    [Theory]
    [InlineData("nombre")] [InlineData("categoria")] [InlineData("icono")] [InlineData("dias")] [InlineData("invalido")]
    public async Task Validaciones_NoEnvianAlta(string caso)
    {
        using var s = new Escenario(); var b = await s.Nuevo();
        var d = caso switch { "nombre"=>Datos with {Nombre=" \t"}, "categoria"=>Datos with {CategoriaId=null}, "icono"=>Datos with {IconoId="BILLETE"}, "dias"=>Datos with {DiasProgramados=[]}, _=>Datos with {DiasProgramados=[(DayOfWeek)9]} };
        b=(await s.Actuar(b,"continuar",d))!;
        if(caso is "icono" or "dias" or "invalido") b=(await s.Actuar(b,"continuar",d))!;
        if(caso is "dias" or "invalido") b=(await s.Actuar(b,"continuar",d))!;
        Assert.NotNull(b.Error); Assert.NotEqual(4,b.Paso); Assert.Equal(0,s.Handler.Post);
    }
    [Theory]
    [InlineData(16, "billete")]
    [InlineData(28, "caminata")]
    public async Task CatalogoCompleto_PermiteAltaConUnaOpcionOfrecida(int cantidad, string id)
    {
        using var s = new Escenario();
        s.Handler.Catalogo = r => r.RequestUri!.AbsolutePath.EndsWith("categorias")
            ? Json(Categorias)
            : Json(Iconos.Take(cantidad).Select((icono, i) => new IconoAlta(icono, icono, i + 1)).Reverse().ToArray());
        var b = await s.Nuevo();
        Assert.NotNull(b.Catalogos);
        Assert.Equal(Iconos.Take(cantidad), b.Catalogos.Iconos.Select(i => i.Id));

        var datos = Datos with { IconoId = id };
        s.Handler.Responder = async r =>
        {
            using var json = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
            Assert.Equal(id, json.RootElement.GetProperty("iconoId").GetString());
            return Json(Creado(datos), HttpStatusCode.Created);
        };
        b = (await s.Actuar(b, "continuar", datos))!;
        if (cantidad == 16)
        {
            b = (await s.Actuar(b, "continuar", datos with { IconoId = "caminata" }))!;
            Assert.Equal(2, b.Paso);
            Assert.Equal("iconoId", b.Campo);
            Assert.Equal(0, s.Handler.Post);
        }
        b = (await s.Actuar(b, "continuar", datos))!;
        b = (await s.Actuar(b, "continuar", datos))!;
        b = (await s.Actuar(b, "crear"))!;
        Assert.Equal("confirmado", b.Estado);
        Assert.Equal(id, b.Resultado!.IconoId);
        Assert.Equal(1, s.Handler.Post);
    }

    [Theory]
    [InlineData("ausente")]
    [InlineData("parcial")]
    [InlineData("sustitucion")]
    [InlineData("intercambio")]
    [InlineData("duplicado")]
    public async Task CatalogoDeIconosIncompletoOAlterado_BloqueaAlta(string caso)
    {
        using var s = new Escenario();
        var iconos = Iconos.Take(caso == "ausente" ? 15 : caso == "parcial" ? 27 : 16).ToArray();
        if (caso == "sustitucion") iconos[15] = "caminata";
        if (caso == "intercambio") (iconos[0], iconos[1]) = (iconos[1], iconos[0]);
        if (caso == "duplicado") iconos[15] = iconos[0];
        s.Handler.Catalogo = r => r.RequestUri!.AbsolutePath.EndsWith("categorias")
            ? Json(Categorias)
            : Json(iconos.Select((icono, i) => new IconoAlta(icono, icono, i + 1)).ToArray());
        var b = await s.Nuevo();
        Assert.Null(b.Catalogos);
        Assert.NotNull(b.Error);
        Assert.Equal(0, s.Handler.Post);
    }

    [Fact]
    public async Task CatalogoFallido_BloqueaYReintentoConservaNombre()
    {
        using var s=new Escenario(); s.Handler.FallarCatalogo=true; var b=await s.Nuevo();
        Assert.Null(b.Catalogos); Assert.NotNull(b.Error);
        b=(await s.Actuar(b,"continuar",Datos))!; Assert.Equal(1,b.Paso); Assert.Equal(0,s.Handler.Post);
        s.Handler.FallarCatalogo=false; b=(await s.Actuar(b,"catalogos"))!;
        Assert.NotNull(b.Catalogos); Assert.Equal("  Leer  ",b.Datos.Nombre);
    }
    [Theory]
    [InlineData(401)] [InlineData(400)] [InlineData(503)]
    public async Task RechazoConocido_ConservaDatosSinConfirmar(int status)
    {
        using var s=new Escenario(); var b=await s.Resumen();
        s.Handler.Responder=_=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        b=(await s.Actuar(b,"crear"))!;
        Assert.Equal("editable",b.Estado); Assert.Null(b.Resultado); Assert.NotNull(b.Error);
        Assert.Equal(status==401,b.LoginRequerido); Assert.Equal("  Leer  ",b.Datos.Nombre);
    }
    [Theory]
    [InlineData("perdida")] [InlineData("500")] [InlineData("json")] [InlineData("contradictoria")]
    public async Task ResultadoIncierto_NoReenviaNiBuscaCoincidencias(string caso)
    {
        using var s=new Escenario(); var b=await s.Resumen();
        s.Handler.Responder=_ => caso switch {
            "perdida"=>throw new HttpRequestException(),
            "500"=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            "json"=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created){Content=new StringContent("{")}),
            _=>Task.FromResult(Json(Creado(Datos) with { IconoId="libro" },HttpStatusCode.Created)) };
        b=(await s.Actuar(b,"crear"))!; Assert.Equal("incierto",b.Estado); Assert.Null(b.Resultado);
        await s.Actuar(b,"crear"); await s.Actuar(b,"volver");
        Assert.Equal(1,s.Handler.Post); Assert.Equal(2,s.Handler.Get);
    }
    [Fact]
    public async Task Concurrencia_YFormularioObsoleto_UnSoloPost()
    {
        using var s=new Escenario(); var b=await s.Resumen(); var entered=new TaskCompletionSource(); var release=new TaskCompletionSource();
        s.Handler.Responder=async _=>{entered.SetResult(); await release.Task; return Json(Creado(Datos),HttpStatusCode.Created);};
        var first=s.Actuar(b,"crear"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var other=await s.Actuar(b,"crear"); Assert.Equal("enviando",other!.Estado);
        await s.Actuar(b,"volver"); release.SetResult(); var result=await first;
        Assert.Equal("confirmado",result!.Estado); await s.Actuar(b,"crear"); Assert.Equal(1,s.Handler.Post);
    }
    [Fact]
    public async Task RevisionObsoleta_NoPisaSeleccionPosterior()
    {
        using var s=new Escenario(); var b=await s.Nuevo(); var n=(await s.Actuar(b,"continuar",Datos))!;
        var old=await s.Actuar(b,"continuar",Datos with {Nombre="obsoleto"});
        Assert.Equal(n.Datos,old!.Datos); Assert.Equal(n.Paso,old.Paso); Assert.Equal(0,s.Handler.Post);
    }
    [Fact]
    public async Task RetencionLimitada_YVencidosNoSeReconstruyen()
    {
        using var s=new Escenario(); var b=await s.Nuevo();
        for(int i=0;i<7;i++) Assert.NotNull(await s.Service.IniciarAsync(s.Client,s.User,s.Session,s.Clock.GetUtcNow().AddHours(4),default));
        Assert.Null(await s.Service.IniciarAsync(s.Client,s.User,s.Session,s.Clock.GetUtcNow().AddHours(4),default));
        s.Clock.Now=s.Clock.Now.AddHours(2); Assert.Null(s.Service.Leer(s.User,s.Session,b.Id));
        Assert.Null(await s.Actuar(b,"crear")); Assert.Equal(0,s.Handler.Post);
    }
    [Fact]
    public async Task Volver_ConservaLaEleccionDelPasoActual()
    {
        using var s = new Escenario(); var b = await s.Nuevo();
        b=(await s.Actuar(b,"continuar",Datos))!;
        b=(await s.Actuar(b,"volver",Datos with {IconoId="agua"}))!;
        Assert.Equal(1,b.Paso); Assert.Equal("agua",b.Datos.IconoId);
        b=(await s.Actuar(b,"continuar",Datos))!; Assert.Equal("agua",b.Datos.IconoId);
        Assert.Equal(0,s.Handler.Post);
    }
    [Theory]
    [InlineData("faltante")] [InlineData("duplicado")] [InlineData("orden")] [InlineData("desconocido")] [InlineData("json")] [InlineData("401")]
    public async Task CatalogosAmbiguos_NoPermitenCrear(string caso)
    {
        using var s = new Escenario();
        s.Handler.Catalogo = r => {
            if (caso == "401") return new(HttpStatusCode.Unauthorized);
            if (caso == "json") return new(HttpStatusCode.OK) {Content=new StringContent("null")};
            if (r.RequestUri!.AbsolutePath.EndsWith("categorias"))
                return Json(caso switch { "faltante"=>Categorias[..9], "duplicado"=>Categorias.Select((c,i)=>i==9?Categorias[0]:c).ToArray(), "orden"=>Categorias.Select(c=>c with {Orden=1}).ToArray(), _=>Categorias });
            return Json(Iconos.Select((id,i)=>new IconoAlta(caso=="desconocido"&&i==0?"inventado":id,id,i+1)).ToArray());
        };
        var b=await s.Nuevo(); Assert.Null(b.Catalogos); Assert.NotNull(b.Error); Assert.Equal(caso=="401",b.LoginRequerido);
        b=(await s.Actuar(b,"continuar",Datos))!; Assert.Equal(1,b.Paso); Assert.Equal(0,s.Handler.Post);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IconoRetiradoDuranteAlta_RecargaYConservaBorradorHastaNuevoEnvioManual(bool fallaRecarga)
    {
        using var s = new Escenario();
        var datos = Datos with { IconoId = "caminata" };
        var b = await s.Resumen(datos);
        Assert.Equal(28, b.Catalogos!.Iconos.Length);

        s.Handler.Catalogo = r => r.RequestUri!.AbsolutePath.EndsWith("categorias")
            ? Json(Categorias)
            : fallaRecarga
                ? new(HttpStatusCode.ServiceUnavailable)
                : Json(Iconos.Take(16).Select((id, i) => new IconoAlta(id, id, i + 1)).ToArray());
        s.Handler.Responder = async r =>
        {
            using var json = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
            Assert.Equal("caminata", json.RootElement.GetProperty("iconoId").GetString());
            return Json(new { errors = new { iconoId = new[] { "Ícono no oficial" } } }, HttpStatusCode.BadRequest);
        };

        var anterior = b;
        b = (await s.Actuar(b, "crear"))!;
        Assert.Equal("editable", b.Estado);
        Assert.Equal(2, b.Paso);
        Assert.Equal(4, s.Handler.Get);
        Assert.Equal(1, s.Handler.Post);
        Assert.Equal(b, await s.Actuar(anterior, "crear"));
        Assert.Equal(1, s.Handler.Post);
        if (fallaRecarga)
        {
            Assert.Null(b.Catalogos);
            Assert.Contains("Volvé a intentar", b.Error);
            Assert.Equal("caminata", b.Datos.IconoId);
            fallaRecarga = false;
            b = (await s.Actuar(b, "catalogos"))!;
            Assert.Equal(6, s.Handler.Get);
        }

        Assert.Equal(Iconos.Take(16), b.Catalogos!.Iconos.Select(i => i.Id));
        Assert.Null(b.Datos.IconoId);
        Assert.Equal("iconoid", b.Campo);
        Assert.Contains("Elegí otro", b.Error);
        Assert.Equal(datos.Nombre, b.Datos.Nombre);
        Assert.Equal(datos.CategoriaId, b.Datos.CategoriaId);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Sunday }, b.Datos.DiasProgramados);
        Assert.Equal(1, s.Handler.Post);

        var nuevo = b.Datos with { IconoId = "billete" };
        s.Handler.Responder = async r =>
        {
            using var json = JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
            var cuerpo = json.RootElement;
            Assert.Equal("billete", cuerpo.GetProperty("iconoId").GetString());
            Assert.Equal(datos.Nombre, cuerpo.GetProperty("nombre").GetString());
            Assert.Equal(datos.CategoriaId, cuerpo.GetProperty("categoriaId").GetGuid());
            Assert.Equal(new[] { 1, 0 }, cuerpo.GetProperty("diasProgramados").EnumerateArray().Select(d => d.GetInt32()));
            return Json(Creado(nuevo), HttpStatusCode.Created);
        };
        b = (await s.Actuar(b, "continuar", nuevo))!;
        b = (await s.Actuar(b, "continuar", nuevo))!;
        Assert.Equal(1, s.Handler.Post);
        b = (await s.Actuar(b, "crear"))!;
        Assert.Equal("confirmado", b.Estado);
        Assert.Equal("billete", b.Resultado!.IconoId);
        Assert.Equal(2, s.Handler.Post);
    }

    [Theory]
    [InlineData("iconoId",2)] [InlineData("categoriaId",1)] [InlineData("diasProgramados",3)] [InlineData("Nombre",1)]
    public async Task ErrorDeCampoAPI_DevuelvePasoYConservaElecciones(string campo,int paso)
    {
        using var s=new Escenario(); var b=await s.Resumen();
        s.Handler.Responder=_=>Task.FromResult(Json(new{errors=new Dictionary<string,string[]>{{campo,["error sintético"]}}},HttpStatusCode.BadRequest));
        var r=(await s.Actuar(b,"crear"))!; Assert.Equal(paso,r.Paso); Assert.NotNull(r.Error); Assert.Equal(Datos.Nombre,r.Datos.Nombre);
        Assert.Equal("billete",r.Datos.IconoId); Assert.Equal(new[]{DayOfWeek.Monday,DayOfWeek.Sunday},r.Datos.DiasProgramados);
        Assert.Equal(campo == "iconoId" ? 4 : 2, s.Handler.Get);
        Assert.Contains("La API rechazó los datos", r.Error);
        Assert.Equal(1, s.Handler.Post);
    }
    [Fact]
    public async Task BorradorAjenoODeOtraSesion_NoSePuedeEnviar()
    {
        using var s=new Escenario(); var b=await s.Resumen();
        Assert.Null(await s.Service.AplicarAsync(s.Client,Guid.NewGuid(),s.Session,b.Id,b.Revision,"crear",null,default));
        Assert.Null(await s.Service.AplicarAsync(s.Client,s.User,"otra",b.Id,b.Revision,"crear",null,default));
        Assert.Equal(0,s.Handler.Post);
    }
    [Theory]
    [InlineData("null")] [InlineData("[]")] [InlineData("\"texto\"")]
    public async Task Rechazo400ConRaizNoObjeto_NoBloqueaComoEnviando(string body)
    {
        using var s=new Escenario(); var b=await s.Resumen();
        s.Handler.Responder=_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest){Content=new StringContent(body)});
        var r=(await s.Actuar(b,"crear"))!;
        Assert.Equal("editable",r.Estado); Assert.NotNull(r.Error); Assert.Null(r.Resultado);
        Assert.Equal("editable",s.Service.Leer(s.User,s.Session,b.Id)!.Estado);
    }
    private sealed class Reloj:TimeProvider { public DateTimeOffset Now=DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class Escenario:IDisposable
    {
        public readonly MemoryCache Cache=new(new MemoryCacheOptions()); public readonly Reloj Clock=new(); public readonly Guid User=Guid.NewGuid(); public readonly string Session=Guid.NewGuid().ToString("N");
        public readonly Handler Handler=new(); public readonly HttpClient Client; public readonly AltaHabito Service;
        public Escenario(){Client=new(Handler){BaseAddress=new("https://api.example.invalid/")};Service=new(Cache,Clock);}
        public async Task<EstadoAlta> Nuevo()=>Assert.IsType<EstadoAlta>(await Service.IniciarAsync(Client,User,Session,Clock.GetUtcNow().AddHours(4),default));
        public Task<EstadoAlta?> Actuar(EstadoAlta b,string action,DatosAlta? d=null)=>Service.AplicarAsync(Client,User,Session,b.Id,b.Revision,action,d,default);
        public async Task<EstadoAlta> Resumen(DatosAlta? datos = null){var b=await Nuevo(); for(int i=0;i<3;i++)b=(await Actuar(b,"continuar",datos ?? Datos))!; Assert.Equal(4,b.Paso);return b;}
        public void Dispose(){Client.Dispose();Cache.Dispose();}
    }
    private sealed class Handler:HttpMessageHandler
    {
        public Func<HttpRequestMessage,HttpResponseMessage>? Catalogo; public int Get,Post; public bool FallarCatalogo; public Func<HttpRequestMessage,Task<HttpResponseMessage>> Responder=_=>throw new InvalidOperationException("POST no esperado");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {
            if(r.Method==HttpMethod.Post){Interlocked.Increment(ref Post);return Responder(r);}
            Interlocked.Increment(ref Get);
            if(Catalogo is not null)return Task.FromResult(Catalogo(r));
            if(FallarCatalogo)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("categorias")?Json(Categorias):Json(Iconos.Select((id,i)=>new IconoAlta(id,id,i+1)).ToArray()));
        }
    }
}
