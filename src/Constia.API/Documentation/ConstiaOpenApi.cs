using System.Text.Json.Nodes;
using Constia.API.Contracts;
using Constia.API.Controllers;
using Constia.Application.Habitos;
using Constia.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Constia.API.Documentation;

internal static class ConstiaOpenApi
{
    public static IServiceCollection AddConstiaOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddSchemaTransformer(TransformSchema);
            options.AddOperationTransformer(TransformOperation);
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new()
                {
                    Title = "CONSTIA API", Version = "v1",
                    Description = "Documentación local de Development. Usar solo datos y JWT sintéticos. " +
                        "Try it out admite únicamente GET; esto no sustituye la autorización de la API. " +
                        "No hay persistencia de credenciales. Un 500 o una respuesta perdida tras guardar no demuestra rollback: no reenviar automáticamente. " +
                        "Los estados de protocolo conservan su HTTP sin detalles internos; una desconexión puede impedir recibir un cuerpo."
                };
                document.Components ??= new();
                document.Servers = [new OpenApiServer { Url = "/" }];
                document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["Bearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
                        Description = "JWT sintético de ensayo, solo en memoria de esta página. No copiar la sesión de Web."
                    }
                };
                return Task.CompletedTask;
            });
        });
        return services;
    }

    private static async Task TransformOperation(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var action = (ControllerActionDescriptor)context.Description.ActionDescriptor;
        var name = action.ActionName;
        var controller = action.ControllerTypeInfo.AsType();
        var metadata = action.EndpointMetadata;
        var bearer = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();
        operation.Security = bearer
            ? [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [] }]
            : [];
        operation.Summary = name;
        operation.Description = Description(controller, name);
        var problem = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), cancellationToken: ct);
        var validation = await context.GetOrCreateSchemaAsync(typeof(ValidationProblemDetails), cancellationToken: ct);

        // Complete existing response metadata rather than replacing business contracts.
        if (controller == typeof(UsuariosController) && name == nameof(UsuariosController.Registrar))
        {
            operation.Responses!["429"] = new OpenApiResponse();
            operation.Responses["503"] = new OpenApiResponse();
        }
        var expectedZoneError = operation.Responses!.ContainsKey("500") ||
            controller == typeof(HabitosController) && name == nameof(HabitosController.Crear);
        operation.Responses["500"] = Response(expectedZoneError
            ? "Zona persistida inválida (ProblemDetails previsto, sin code garantizado), o fallo inesperado seguro."
            : "Fallo inesperado seguro: título fijo, code internal_error y traceId del servidor. No garantiza cuerpo si la respuesta comenzó o falla el transporte.",
            expectedZoneError ? new OpenApiSchema { AnyOf = [problem, InternalError()] } : InternalError());
        ((OpenApiResponse)operation.Responses["500"]).Headers = new Dictionary<string, IOpenApiHeader>
        {
            ["Cache-Control"] = Header("no-store en el fallo inesperado; no se garantiza para la variante prevista de zona.")
        };
        foreach (var (status, responseMetadata) in operation.Responses)
        {
            var response = (OpenApiResponse)responseMetadata;
            if (status == "500") continue;
            switch (status)
            {
                case "400":
                    var validationOnly = controller == typeof(AuthController) ||
                        controller == typeof(VerificacionCorreoController) && name == nameof(VerificacionCorreoController.Reenviar);
                    var dateQuery = context.Description.HttpMethod == "GET";
                    response.Description = validationOnly ? "Validación por campo: errors contiene las claves de los campos no válidos."
                        : dateQuery ? "Fecha o rango civil inválido (ProblemDetails)."
                        : "Validación por campo (errors) o datos no válidos (ProblemDetails). No todos los errores tienen code.";
                    // VPD and PD overlap; anyOf is intentional only where both occur.
                    response.Content = Content(validationOnly ? validation : dateQuery ? problem : new OpenApiSchema { AnyOf = [validation, problem] });
                    break;
                case "401":
                    var challengeOnly = controller == typeof(HabitosController) &&
                        name is nameof(HabitosController.ListarIconos) or nameof(HabitosController.ListarCategorias);
                    response.Description = bearer
                        ? challengeOnly ? "Bearer ausente/inválido: challenge sin cuerpo."
                            : "Bearer ausente/inválido: challenge sin cuerpo; una acción puede devolver ProblemDetails al no identificar al usuario."
                        : "Credenciales de login inválidas (ProblemDetails); no es un challenge Bearer.";
                    response.Content = challengeOnly ? null : Content(problem);
                    if (bearer) response.Headers = new Dictionary<string, IOpenApiHeader>
                    { ["WWW-Authenticate"] = Header("Bearer en el challenge. No garantizado para un 401 producido dentro de la acción.") };
                    break;
                case "403": SetProblem(response, problem, "Correo pendiente de verificación: code email_verification_required."); break;
                case "404": SetProblem(response, problem, "Recurso ajeno o inexistente, sin distinción de existencia."); break;
                case "409": SetProblem(response, problem, name == nameof(HabitosController.RegistrarCumplimiento)
                    ? "El hábito no admite otro cumplimiento para esa fecha. No siempre es un duplicado ni acredita persistencia."
                    : controller == typeof(UsuariosController) ? "Email ya registrado." : "Conflicto de vigencia o edición concurrente; no reenviar automáticamente."); break;
                case "412": SetProblem(response, problem, "If-Match válido pero obsoleto, o concurrencia con precondición. Conservar borrador y consultar detalle antes de otro guardado explícito."); break;
                case "422": SetProblem(response, problem, controller == typeof(RachasController)
                    ? "code habit_status_history_unknown: ninguna métrica aplicable es determinable. No equivale a cero."
                    : name == nameof(HabitosController.ConsultarCalendario)
                        ? "code habit_status_history_unknown: afecta al rango completo, no identifica celdas concretas."
                        : "code habit_status_history_unknown: estado histórico desconocido para la consulta."); break;
                case "429":
                    response.Description = "code rate_limited. Limitación de solicitudes; no se garantiza Retry-After.";
                    response.Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = problem } };
                    break;
                case "503": SetProblem(response, problem,
                    controller == typeof(VerificacionCorreoController) || controller == typeof(UsuariosController) && name == nameof(UsuariosController.Registrar)
                        ? "code registration_unavailable: operación de correo/registro temporalmente no disponible."
                        : "code habit_status_history_not_initialized: historial global todavía no inicializado. No confundir con un 503 genérico de infraestructura."); break;
                case "204": response.Description = "Operación confirmada, sin cuerpo."; response.Content = null; break;
                case "201": response.Description = "Creado; JSON de la entidad confirmada, sin encabezado Location."; break;
            }
            if (status.StartsWith('2') && status != "204" && response.Content?.TryGetValue("application/json", out var json) == true)
                response.Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = json };
        }
        if (controller == typeof(VerificacionCorreoController) && name == nameof(VerificacionCorreoController.Confirmar))
            operation.Responses["400"].Description += " code email_verification_invalid cuando el desafío o sus datos no son válidos.";
        if (controller == typeof(VerificacionCorreoController) && name == nameof(VerificacionCorreoController.Reenviar))
            operation.Responses["202"] = Response("Solicitud aceptada; mensaje genérico, no confirma existencia de cuenta ni entrega de correo.",
                new OpenApiSchema { Type = JsonSchemaType.Object, Required = new HashSet<string> { "mensaje" },
                    Properties = new Dictionary<string, IOpenApiSchema> { ["mensaje"] = new OpenApiSchema { Type = JsonSchemaType.String } } }, "application/json");

        if (controller == typeof(RachasController))
            operation.Responses["200"].Content!["application/json"].Example = JsonNode.Parse(name == nameof(RachasController.General)
                ? """{"fechaCorte":"2026-10-06","actual":{"estado":"disponible","valor":3},"mejor":{"estado":"no_disponible","valor":null}}"""
                : """{"fechaCorte":"2026-10-06","actual":{"estado":"no_aplicable","valor":null},"mejor":{"estado":"disponible","valor":3}}""");

        if (controller == typeof(HabitosController) && name == nameof(HabitosController.ObtenerPorId))
        {
            ((OpenApiResponse)operation.Responses["200"]).Headers = new Dictionary<string, IOpenApiHeader>
            { ["ETag"] = Header("Versión fuerte canónica: entero positivo entre comillas, por ejemplo \"1\".", "^\"[1-9][0-9]*\"$") };
            operation.Responses["200"].Content!["application/json"].Example = JsonNode.Parse("""{"id":"00000000-0000-4000-8000-000000000001","nombre":"Paseo breve","descripcion":null,"createdAt":"2026-10-01T12:00:00Z","startDate":"2026-10-01","estado":0,"diasProgramados":[1,3,5],"categoriaId":null,"iconoId":null}""");
        }
        if (controller == typeof(HabitosController) && name == nameof(HabitosController.Editar))
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "If-Match", In = ParameterLocation.Header, Required = false,
                Description = "ETag del detalle. Opcional: omitir conserva el contrato anterior sin detectar formulario obsoleto. W/, *, listas, cero y formato no canónico producen 400. PUT no promete un nuevo ETag.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = "^\"[1-9][0-9]*\"$" }
            });
        }
        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
            if (parameter.In == ParameterLocation.Query && parameter.Name is "desde" or "hasta" or "fecha")
            {
                parameter.Required = true;
                parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" };
                parameter.Example = JsonValue.Create("2026-10-06");
                parameter.Description = parameter.Name == "fecha" ? "Fecha civil explícita yyyy-MM-dd."
                    : "Fecha civil yyyy-MM-dd. Extremos inclusivos, desde ≤ hasta y máximo 42 días; orden de salida ascendente.";
            }
        if (metadata.OfType<ResponseCacheAttribute>().Any(attribute => attribute.NoStore))
            foreach (var response in operation.Responses.Values.Cast<OpenApiResponse>())
            {
                response.Headers ??= new Dictionary<string, IOpenApiHeader>();
                response.Headers["Cache-Control"] = Header("no-store cuando se ejecuta el filtro MVC; un rechazo previo no garantiza este encabezado.");
            }
        if (controller == typeof(VerificacionCorreoController) && name == nameof(VerificacionCorreoController.Confirmar))
            foreach (var response in operation.Responses.Values.Cast<OpenApiResponse>())
            {
                response.Headers ??= new Dictionary<string, IOpenApiHeader>();
                response.Headers["Referrer-Policy"] = Header("no-referrer dentro de la acción. No garantizado si MVC o el limitador rechazan antes.");
            }
    }

    private static Task TransformSchema(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        var type = context.JsonTypeInfo.Type;
        if (type == typeof(DayOfWeek) || type == typeof(EstadoHabito))
        {
            schema.Type = JsonSchemaType.Integer;
            schema.Enum = Enum.GetValues(type).Cast<object>().Select(value => (JsonNode)JsonValue.Create(Convert.ToInt32(value))!).ToList();
            schema.Description = type == typeof(DayOfWeek) ? "0 domingo, 1 lunes, 2 martes, 3 miércoles, 4 jueves, 5 viernes, 6 sábado. No nombres string."
                : "0 Activo, 1 Inactivo.";
        }
        if (schema.Properties is not { } properties) return Task.CompletedTask;
        // The native serializer schema accepts numeric strings on input. These DTOs
        // are responses only: their counters/order are emitted as JSON integers.
        if (type == typeof(DiaCalendarioResponse) || type == typeof(IconoHabitoListado) || type == typeof(CategoriaHabitoListado))
            foreach (var number in properties.Values.OfType<OpenApiSchema>().Where(property => property.Format == "int32"))
            { number.Type = JsonSchemaType.Integer; number.Pattern = null; }
        if (type == typeof(CrearHabitoHttpRequest) || type == typeof(EditarHabitoHttpRequest))
        {
            var create = type == typeof(CrearHabitoHttpRequest);
            schema.Description = create
                ? "Propietario y startDate calculados por el servidor. Presencia de fechaInicio raíz prohibida, incluso null y variaciones de mayúsculas. Otras propiedades adicionales no otorgan autoridad."
                : "Omitir categoriaId/iconoId conserva la asignación, incluso legado null. Si se envían deben ser oficiales y no null/vacíos; claves repetidas se rechazan. El resto de campos se procesa según el PUT completo.";
            schema.Required ??= new HashSet<string>();
            if (create) { schema.Required.Add("categoriaId"); schema.Required.Add("iconoId"); }
            properties["categoriaId"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid", Description = "ID no vacío del catálogo de categorías; independiente del ícono." };
            properties["iconoId"] = new OpenApiSchema { Type = JsonSchemaType.String, Enum = CatalogoIconosHabito.Todos.Select(icon => (JsonNode)JsonValue.Create(icon.Id)!).ToList(), Description = "ID oficial, con comparación ordinal. Consultar el catálogo vigente." };
            if (properties["diasProgramados"] is OpenApiSchema days)
            {
                days.Type = JsonSchemaType.Array; days.MinItems = 1;
                days.Description = "Al menos un día válido. Duplicados se normalizan; no se rechazan por uniqueItems.";
            }
            properties["nombre"].Description = "Obligatorio y no blanco. El contrato HTTP no declara longitud máxima ni recorta el nombre.";
        }
        if (type == typeof(RegistrarCumplimientoHttpRequest) && properties["fecha"] is OpenApiSchema date)
            date.Type = JsonSchemaType.String;
        if (type == typeof(ConfirmarCorreoRequest))
        {
            schema.Required ??= new HashSet<string>();
            schema.Required.Add("desafioId");
            properties["desafioId"].Description = "Identificador no vacío del desafío de verificación.";
        }
        if (type == typeof(DiaHistorialHabito))
        {
            properties["estado"] = StringEnum("cumplido", "pendiente", "no_programado", "desconocido");
            properties["tieneRegistro"].Description = "Hecho persistido independiente: puede ser true con no_programado o desconocido. Solo esta consulta admite 200 con días desconocidos.";
        }
        if (type == typeof(MetricaRachaResponse))
        {
            properties["estado"] = StringEnum("disponible", "no_aplicable", "no_disponible");
            properties["valor"] = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null, Minimum = "0", Description = "0 o mayor si disponible; null si no_aplicable o no_disponible. Métricas independientes en un 200 parcial." };
        }
        if (type == typeof(HabitoResponse) || type == typeof(HabitoDiarioResponse))
        {
            properties["categoriaId"].Description = "Puede ser null en hábitos legados; no inferir categoría.";
            properties["iconoId"].Description = "Puede ser null en hábitos legados; no asignar un ícono implícito.";
        }
        if (type == typeof(RegistroUsuarioRequest))
            properties["password"].Description = "Solo nuevas altas: 8–128 valores escalares Unicode válidos, una mayúscula, un dígito decimal y puntuación/símbolo. Sin recortar ni normalizar. No aplicar a contraseñas históricas de login/verificación.";
        foreach (var key in new[] { "password", "token" })
            if (properties.TryGetValue(key, out var secret) && secret is OpenApiSchema input)
            { input.WriteOnly = true; input.Format = "password"; }
        if (type == typeof(LoginUsuarioResponse) && properties["accessToken"] is OpenApiSchema accessToken)
        { accessToken.ReadOnly = true; accessToken.Description = "Credencial sensible de respuesta; no se incluye un ejemplo ni se precarga en Swagger UI."; }
        if (type == typeof(ProblemDetails) || type == typeof(ValidationProblemDetails))
        {
            properties["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null, Format = "int32" };
            if (type == typeof(ValidationProblemDetails))
            {
                schema.Required ??= new HashSet<string>();
                schema.Required.Add("errors");
            }
            properties["code"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Opcional; códigos documentados por respuesta. No todos los errores previstos lo incluyen." };
            properties["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Identificador del servidor; obligatorio solo en el 500 inesperado seguro." };
        }
        return Task.CompletedTask;
    }

    private static OpenApiSchema StringEnum(params string[] values) => new()
    { Type = JsonSchemaType.String, Enum = values.Select(value => (JsonNode)JsonValue.Create(value)!).ToList() };

    private static OpenApiSchema InternalError() => new()
    {
        Type = JsonSchemaType.Object, Required = new HashSet<string> { "status", "title", "code", "traceId" },
        Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Enum = [JsonValue.Create(500)!] },
            ["title"] = StringEnum("No pudimos completar la operación."),
            ["code"] = StringEnum("internal_error"), ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String }
        }
    };
    private static OpenApiHeader Header(string description, string? pattern = null) => new()
    { Description = description, Schema = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = pattern } };
    private static Dictionary<string, OpenApiMediaType> Content(IOpenApiSchema schema, string media = "application/problem+json") =>
        new() { [media] = new() { Schema = schema } };
    private static OpenApiResponse Response(string description, IOpenApiSchema schema, string media = "application/problem+json") =>
        new() { Description = description, Content = Content(schema, media) };
    private static void SetProblem(OpenApiResponse response, IOpenApiSchema schema, string description)
    { response.Description = description; response.Content = Content(schema); }

    private static string Description(Type controller, string action) => (controller.Name, action) switch
    {
        (nameof(AuthController), nameof(AuthController.Login)) => "Iniciar sesión con email y contraseña existentes. No aplica la política de nuevas contraseñas. No exige Bearer.",
        (nameof(UsuariosController), nameof(UsuariosController.Registrar)) => "Registrar una cuenta pendiente de verificar correo. No devuelve JWT ni confirma entrega del mensaje.",
        (nameof(UsuariosController), nameof(UsuariosController.ConsultarFechaLocal)) => "Fecha civil del usuario autenticado según la zona IANA persistida vigente, independiente del historial.",
        (nameof(UsuariosController), nameof(UsuariosController.ConsultarZonaHoraria)) => "Zona IANA persistida exclusivamente del usuario autenticado.",
        (nameof(UsuariosController), nameof(UsuariosController.CambiarZonaHoraria)) => "Cambiar zona IANA vigente con la misma sesión; no reescribe cumplimientos ni vigencias históricas.",
        (nameof(VerificacionCorreoController), nameof(VerificacionCorreoController.Confirmar)) => "Confirmar un desafío con token y contraseña existente; no impone la política de contraseñas de altas.",
        (nameof(VerificacionCorreoController), nameof(VerificacionCorreoController.Reenviar)) => "Solicitar reenvío sin revelar existencia, estado o entrega de correo de la cuenta.",
        (nameof(HabitosController), nameof(HabitosController.ListarActivos)) => "Todos los hábitos propios actualmente activos, incluidos legados; orden estable. [] es un resultado exitoso.",
        (nameof(HabitosController), nameof(HabitosController.ListarInactivos)) => "Hábitos propios inactivos, incluidos legados. Consulta sin reactivación.",
        (nameof(HabitosController), nameof(HabitosController.ObtenerPorId)) => "Detalle propio activo o inactivo. ETag en encabezado, no en el cuerpo; ajeno/inexistente reciben el mismo 404.",
        (nameof(HabitosController), nameof(HabitosController.Crear)) => "Crear hábito propio con categoría e ícono independientes y días explícitos. startDate se calcula en backend con fecha civil local. Sin Location.",
        (nameof(HabitosController), nameof(HabitosController.Editar)) => "Editar hábito propio. Cambiar días entra en vigor según fecha civil local al guardar (o inicio si aún no llegó); no reescribe historia. Omitir If-Match conserva el contrato anterior.",
        (nameof(HabitosController), nameof(HabitosController.Desactivar)) => "Desactivar, sin eliminar historial. Propio ya inactivo también devuelve 204; no hay reactivación V1.",
        (nameof(HabitosController), nameof(HabitosController.ListarIconos)) => "28 IDs string oficiales en orden. Independientes de las diez categorías; no son GUID.",
        (nameof(HabitosController), nameof(HabitosController.ListarCategorias)) => "Diez categorías oficiales con ID GUID y orden. No determinan automáticamente el ícono.",
        (nameof(HabitosController), nameof(HabitosController.ConsultarDiarios)) => "Oportunidades de una fecha civil explícita; conserva COMP-007. Colección sin envoltorio de Hoy.",
        (nameof(HabitosController), nameof(HabitosController.ConsultarHoy)) => "Hoy civil según la zona IANA persistida vigente; fecha única del envoltorio y sus oportunidades.",
        (nameof(HabitosController), nameof(HabitosController.ConsultarCalendario)) => "Rango inclusivo de 1–42 días, ascendente. Conteos de oportunidades; futuro representa planificación y cero programados es neutral. Un 422 afecta al rango completo.",
        (nameof(HabitosController), nameof(HabitosController.ConsultarHistorial)) => "Hábito propio activo o inactivo, rango inclusivo de 1–42 días ascendente. Estado de oportunidad y tieneRegistro independientes. 200 admite desconocido; no devuelve 422 por ello. La API no impone la restricción visual de selección futura.",
        (nameof(HabitosController), nameof(HabitosController.ListarCumplimientos)) => "Registros persistidos por fecha descendente, incluidos fuera de programación o inactivo. [] válido; no depende de inicialización global del historial.",
        (nameof(HabitosController), nameof(HabitosController.RegistrarCumplimiento)) => "Registrar para hoy o ayer civil mostrado por el cliente, desde inicio y según programación histórica; exige hábito actualmente activo. Un 409 no acredita un registro y una respuesta incierta requiere reconciliar antes de un nuevo intento manual.",
        (nameof(RachasController), nameof(RachasController.General)) => "Racha general actual y mejor hasta fechaCorte civil, historia completa sin límite de 42 días. 200 admite disponibilidad parcial; 0 disponible no equivale a no_disponible.",
        (nameof(RachasController), nameof(RachasController.Individual)) => "Rachas de hábito propio activo o inactivo. Inactivo: actual no_aplicable y mejor histórica cuando sea determinable. 200 conserva las métricas disponibles; 422 solo si ninguna aplicable es determinable.",
        _ => throw new InvalidOperationException("Falta describir una operación HTTP de CONSTIA.")
    };
}
