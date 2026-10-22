using System.Net;
using System.Net.Http.Json;
using Constia.Application.Correo;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Constia.API.Tests;
public sealed class ReenvioCorreoPrivacidadTests
{
    [Fact]
    public async Task ProveedorBloqueado_NoRetrasa202DePendienteAusenteOVerificado()
    {
        using var f=new JwtApiFactory();var transporte=new TransporteBloqueado();
        using var host=f.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{
            s.RemoveAll<ITransporteCorreoVerificacion>();s.AddSingleton<ITransporteCorreoVerificacion>(transporte);
        }));
        using var c=host.CreateClient();
        var alta=await c.PostAsJsonAsync("/api/usuarios",new{nombre="Ana",email="pending@example.invalid",
            password="ABCDE1.@",timeZoneId="Etc/UTC"});
        Assert.Equal(HttpStatusCode.Created,alta.StatusCode);
        await transporte.Iniciado.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var respuestas=new List<HttpResponseMessage>();
        try
        {
            foreach(var email in new[]{"pending@example.invalid","ausente@example.invalid",f.Usuario.Email})
            {
                var respuesta=await c.PostAsJsonAsync("/api/auth/email/reenviar",new{email}).WaitAsync(TimeSpan.FromSeconds(5));
                respuestas.Add(respuesta);
                Assert.Equal(HttpStatusCode.Accepted,respuesta.StatusCode);
            }
            var cuerpos=await Task.WhenAll(respuestas.Select(r=>r.Content.ReadAsStringAsync()));
            Assert.Single(cuerpos.Distinct());
            var llena=await c.PostAsJsonAsync("/api/auth/email/reenviar",new{email="otro@example.invalid"});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,llena.StatusCode);
            Assert.Single(f.DesafiosCorreo);
        }
        finally { transporte.Liberar.TrySetResult(); }
        await host.Services.GetRequiredService<ICanalVerificacionCorreo>().EsperarVacioAsync(default);
        Assert.Single(f.DesafiosCorreo);
    }
    [Fact]
    public void SolicitudesYTrabajos_NoRevelanDestinatario()
    {
        var r=new Constia.API.Contracts.ReenviarCorreoRequest{Email="privado@example.invalid"};
        var t=new TrabajoVerificacionCorreo(DateTimeOffset.UtcNow,0,emailReenvio:r.Email);
        Assert.DoesNotContain("privado",r.ToString());
        Assert.DoesNotContain("privado",t.ToString());
    }
    internal sealed class TransporteBloqueado : ITransporteCorreoVerificacion
    {
        public TaskCompletionSource Iniciado=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Liberar=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ResultadoEntregaCorreo> EnviarAsync(MensajeVerificacionCorreo mensaje,CancellationToken ct)
        {
            Iniciado.TrySetResult();await Liberar.Task.WaitAsync(ct);
            return new(EstadoEntregaCorreo.AceptadaPorProveedor,MotivoEntregaCorreo.Ninguno,"synthetic",intentoHttpIniciado:true);
        }
    }
}
