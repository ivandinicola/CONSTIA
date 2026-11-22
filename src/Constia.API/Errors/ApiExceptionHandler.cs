using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Constia.API.Errors;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError;
        var endpoint = context.Features.Get<IExceptionHandlerFeature>()?.Endpoint as RouteEndpoint;
        logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Warning, new EventId(3003, "ApiError"),
            "Solicitud fallida {TraceId} {Method} {Route} {ExceptionType} {StatusCode}",
            context.TraceIdentifier, context.Request.Method, endpoint?.RoutePattern.RawText,
            exception.GetType().FullName, status);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = status == StatusCodes.Status500InternalServerError
                ? "No pudimos completar la operación."
                : "La solicitud no es válida.",
            Extensions = { ["traceId"] = context.TraceIdentifier }
        };
        if (status == StatusCodes.Status500InternalServerError)
            problem.Extensions["code"] = "internal_error";

        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            await context.Response.WriteAsJsonAsync(problem, options: null,
                contentType: "application/problem+json", cancellationToken: cancellationToken);
        }
        catch (Exception)
        {
            // No volver a la página de desarrollo si falla la escritura del error seguro.
            context.Abort();
        }
        return true;
    }
}
