# API-CAT-01 — Contratos de categorías, íconos y FechaInicio

**APROBADO — Diseño técnico y contractual — 19/10/2026**

**Aprobado para planificar su implementación posterior. No implementado ni verificado funcionalmente.**

El usuario aprobó explícitamente DEC-01 a DEC-08 y PREC-01 a PREC-03. La aprobación no autoriza código, migraciones, bases de datos, tests, staging, commits, push ni PR. Los contratos, ejemplos y firmas futuras conservados describen el diseño aprobado; no una implementación existente.

Las verificaciones de la redacción inicial se conservan como evidencia histórica en el apartado 15. Los pendientes de API-CAT-01 citados en AUD-BASE quedan resueltos por este registro sin modificar aquellos documentos; permanece condicionada la ejecución del corte al inventario real de consumidores (DEC-08).

> Para una ejecución futura autorizada, aplicar superpowers:executing-plans o la modalidad que acuerde el usuario. Este documento no autoriza escribir código, migraciones, tests, commits, PR ni iniciar UI-006/PEN-007.

**Goal:** cerrar los contratos antes de CAT-01/CAT-02, preservando históricos y garantías temporales.
**Architecture:** controllers y DTOs de API; validación y coordinación en Application; invariantes en Domain; persistencia en Infrastructure. Sin System.Text.Json ni EF dentro de Application/Domain.
**Tech Stack:** .NET 10, ASP.NET Core MVC, System.Text.Json, EF Core 10, SQL Server y xUnit existentes; sin dependencias nuevas.
**Spec:** [MVP-SCOPE](../../../MVP-SCOPE.md), [SPECIFICATION](../../../SPECIFICATION.md), [ARCHITECTURE](../../../ARCHITECTURE.md) y [AUD-BASE](2026-10-19-categorias-v1-fecha-inicio.md).

## 1. Entorno y alcance verificados durante la redacción inicial (antecedente)

- Worktree exclusivo: C:/Users/FIO PC/.codex/worktrees/aud-base/Constia.
- Rama chore/AUD-BASE; HEAD 30058bbcd90e8f6a3733784f6826470eb67989d4.
- Base integrada con COMP-005 y TEMP-005, no el checkout histórico de TEMP-004.
- Estado inicial: cuatro documentos funcionales modificados y plan AUD-BASE sin seguimiento. Se preservan íntegramente sus bytes.
- Única escritura autorizada en API-CAT-01: este documento nuevo. No se ejecutan pruebas ni se accede a bases de datos.
- La consulta de hashes/estado del checkout original se limita a verificar preservación; no se utiliza como fuente del diseño de contratos.

## 2. Decisiones de producto cerradas

Diez categorías globales y predefinidas; sin categorías personalizadas. Cada alta nueva requiere exactamente una categoría y un ícono oficiales válidos y explícitos. Hay dieciséis íconos. No hay asignación automática de Libro, Billete u otro recurso, ni dependencia entre categoría e ícono.

Los históricos pueden conservar CategoriaId e IconoId nulos sin inferencias ni backfill arbitrario. Omitir categoría al editar conserva su valor; no se permite borrar una asignada y editar un histórico sin categoría no obliga a clasificarlo.

FechaInicio la calcula el backend desde un instante UTC único, TimeProvider y zona IANA persistida del propietario. El nuevo POST debe rechazar su presencia, incluido null. No se reescriben fechas históricas.

DEC-01 aprueba rechazar null explícito en edición con 400 y prohíbe borrar categoría e ícono en V1. Estas decisiones contractuales están cerradas; su implementación y aceptación funcional siguen pendientes.

## 3. Contratos y firmas actuales — evidencia de fuentes

Rutas relativas a la raíz del worktree indicada; enlaces apuntan al código de esta base.

| Componente | Firma/propiedades actuales y evidencia |
| --- | --- |
| [CrearHabitoHttpRequest](../../../src/Constia.API/Contracts/CrearHabitoHttpRequest.cs) | sealed class: Nombre:string con Required, Descripcion:string?, FechaInicio:DateOnly? con Required, DiasProgramados:IReadOnlyCollection<DayOfWeek>? con Required |
| [CrearHabitoRequest](../../../src/Constia.Application/Habitos/CrearHabitoRequest.cs) | record(UsuarioId:Guid, Nombre:string, Descripcion:string?, FechaInicio:DateOnly, DiasProgramados:IReadOnlyCollection<DayOfWeek>) |
| [CrearHabito](../../../src/Constia.Application/Habitos/CrearHabito.cs) | ctor(IUsuarioRepository, IHabitoRepository, IEstadoHabitoReadinessRepository); EjecutarAsync(CrearHabitoRequest, CancellationToken=default):Task<HabitoCreado?> |
| [EditarHabitoHttpRequest](../../../src/Constia.API/Contracts/EditarHabitoHttpRequest.cs) | sealed class: Nombre:string con Required, Descripcion:string?, DiasProgramados:IReadOnlyCollection<DayOfWeek>? con Required |
| [EditarHabitoRequest](../../../src/Constia.Application/Habitos/EditarHabitoRequest.cs) | record(Nombre:string, Descripcion:string?, DiasProgramados:IReadOnlyCollection<DayOfWeek>) |
| [EditarHabito](../../../src/Constia.Application/Habitos/EditarHabito.cs) | ctor(IHabitoRepository, IUsuarioRepository, IFechaLocalUsuario); EjecutarAsync(Guid habitId, Guid usuarioId, EditarHabitoRequest, CancellationToken=default):Task<HabitoListado?> |
| [HabitoResponse](../../../src/Constia.API/Contracts/HabitoResponse.cs) | record(Id:Guid, Nombre:string, Descripcion:string?, CreatedAt:DateTimeOffset, StartDate:DateOnly, Estado:EstadoHabito, DiasProgramados:IReadOnlyList<DayOfWeek>) |
| [HabitoCreado](../../../src/Constia.Application/Habitos/HabitoCreado.cs) | Id, UsuarioId, Nombre, Descripcion, FechaCreacion, FechaInicio, Estado y DiasProgramados; sin categoría/ícono |
| [HabitoListado](../../../src/Constia.Application/Habitos/HabitoListado.cs) | Los mismos datos sin UsuarioId; sin categoría/ícono |
| [HabitosController](../../../src/Constia.API/Controllers/HabitosController.cs) | ApiController, Authorize y ruta api/habitos. Crear: POST; Editar: PUT {id:guid}; ListarActivos: GET; ListarInactivos: GET inactivos; ObtenerPorId: GET {id:guid}. CancellationToken en acciones y casos de uso |
| [Habito](../../../src/Constia.Domain/Habito.cs) | ctor(Usuario, string nombre, string? descripcion, DateOnly fechaInicio, IEnumerable<DayOfWeek>); FechaCreacion usa DateTimeOffset.UtcNow, fecha de entrada inicia programación y período Activo. No contiene categoría ni ícono |
| [HabitoRepository](../../../src/Constia.Infrastructure/Habitos/HabitoRepository.cs) | ListarActivos/InactivosPorUsuarioAsync, BuscarPorIdYUsuarioAsync, BuscarParaModificarPorIdYUsuarioAsync, AgregarAsync y GuardarCambiosAsync. Lecturas AsNoTracking; edición rastreada y ConcurrencyVersion |
| [FechaLocalUsuario](../../../src/Constia.Application/Temporal/FechaLocalUsuario.cs) | ctor(TimeProvider, IZonaHorariaIana); ObtenerHoy(string):DateOnly. Resuelve zona, obtiene UTC, convierte y devuelve fecha local |
| [IFechaLocalUsuario](../../../src/Constia.Application/Temporal/IFechaLocalUsuario.cs) | DateOnly ObtenerHoy(string timeZoneId) |
| [ZonaHorariaIana](../../../src/Constia.Application/Temporal/ZonaHorariaIana.cs) | EsValida y Resolver: validan TZDB y disponibilidad del sistema; zona persistida inválida lanza excepción, sin fallback |
| [Program](../../../src/Constia.API/Program.cs) | TimeProvider.System e interfaces temporales registrados; AddControllers sin personalización JSON observada |

CrearHabito.EjecutarAsync comprueba SnapshotCapturadoAsync antes de crear; Desactivar y RegistrarCumplimiento requieren HistorialCompletoAsync. TEMP-005 está en este código. La respuesta actual de alta es 201 con cuerpo, sin Location explícita. El PUT devuelve 200; propietario ausente/ajeno devuelve 404 y conflictos 409.

La API conserva defaults web: nombres camelCase, entrada sin distinguir mayúsculas y minúsculas y enums numéricos, al no existir un convertidor a strings. No se propone alterar esa configuración. DayOfWeek: domingo=0, lunes=1, ..., sábado=6; Estado: Activo=0, Inactivo=1. Esto se deduce del código/configuración; no se verificó ejecutando HTTP.

## 4. POST /api/habitos — diseño aprobado, no implementado

Content-Type application/json; autenticación actual. El propietario procede del JWT, nunca de un usuarioId del cuerpo.

| JSON público | Tipo HTTP / Application | Regla |
| --- | --- | --- |
| nombre | string / string | Obligatorio, no null, no vacío ni solo espacios; conservar validación y contenido actual, sin trim silencioso |
| descripcion | string o null / string? | Opcional; omitido/null conserva semántica actual de ausencia |
| categoriaId | string UUID / Guid | Obligatorio, no null, parseable, distinto de Guid.Empty y existente en catálogo global |
| iconoId | string / string | Obligatorio, no null, clave exacta de catálogo. Rechazar vacío, espacios, mayúsculas o alias desconocidos; no convertir nombres visibles a claves |
| diasProgramados | array de enums numéricos / IReadOnlyCollection<DayOfWeek> | Obligatorio, no null, al menos un día válido 0..6; preservar deduplicación y orden normalizado actuales |
| fechaInicio | Prohibido | Cualquier presencia a nivel raíz, con cualquier valor, produce 400 |
| usuarioId u otros campos no mapeados | Sin autoridad | Mantener política actual de ignorarlos, salvo fechaInicio prohibida; no ampliar mass assignment |

No se agrega un máximo de nombre/descripción por inferencia visual. El código actual no define ese máximo; si se desea, aprobarlo como validación adicional y evaluar históricos antes de aplicarlo en PUT. No se modifica globalmente la aceptación numérica del serializador; el esquema documenta enteros para días y las pruebas fijarán compatibilidad real.

Application futura (diseño aprobado):
CrearHabitoRequest(Guid UsuarioId, string Nombre, string? Descripcion, Guid CategoriaId, string IconoId, IReadOnlyCollection<DayOfWeek> DiasProgramados).
No contiene FechaInicio, FechaCreacion ni zona de cliente. EjecutarAsync conserva Task<HabitoCreado?> y CancellationToken. Validar también entradas internas: Guid.Empty, clave inválida y días/nombre inválidos no pueden persistirse por omitir el adaptador HTTP.

Ejemplo válido según el contrato y los IDs aprobados del apartado 6 (no implica una respuesta ejecutada):

```json
{
  "nombre": "Leer 20 minutos",
  "descripcion": null,
  "categoriaId": "82c0b8e6-0b9d-4e18-9bf8-000000000005",
  "iconoId": "libro",
  "diasProgramados": [
    1,
    2,
    3,
    4,
    5
  ]
}
```

Ejemplos inválidos completos, cada uno debe devolver 400 y no guardar:
```json
{
  "nombre": "Leer 20 minutos",
  "categoriaId": "82c0b8e6-0b9d-4e18-9bf8-000000000005",
  "iconoId": "libro",
  "diasProgramados": [
    1,
    2,
    3,
    4,
    5
  ],
  "fechaInicio": null
}
```

```json
{
  "nombre": "Leer 20 minutos",
  "categoriaId": null,
  "iconoId": "Libro",
  "diasProgramados": []
}
```

El segundo caso contiene varios errores: categoría null, clave visible en vez de funcional y días vacíos. Cada condición se prueba además por separado; no se promete recopilar todos los errores cuando el binding falla antes.

### 4.1 Rechazo localizado y explícito de FechaInicio

Estrategia aprobada por DEC-04: quitar FechaInicio del DTO futuro y agregar exclusivamente en CrearHabitoHttpRequest una propiedad de extensión [JsonExtensionData] Dictionary<string, JsonElement>? CamposAdicionales. Implementar validación de objeto IValidatableObject, sin acceso a BD, que rechace cualquier clave de ese diccionario igual a "fechaInicio" mediante StringComparison.OrdinalIgnoreCase.

La decisión se basa en presencia de la clave, no en su valor. Cubre fechaInicio, FechaInicio, FECHAINICIO, valores null, fecha, número, boolean, objeto o array. Los escapes JSON se comparan después de deserializar el nombre. Repeticiones de fechaInicio siguen dejando una clave prohibida; un último null tampoco permite eludir el rechazo. La verificación se limita a la propiedad raíz: un texto que mencione FechaInicio en descripcion no es un campo de entrada.

Microsoft documenta que JsonExtensionData captura miembros no mapeados y que JsonElement conserva null como JsonValueKind.Null: [documentación oficial](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.serialization.jsonextensiondataattribute). Con ApiController, un ModelState inválido produce 400 antes de ejecutar la acción: [validación MVC](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation?view=aspnetcore-10.0). La garantía específica de este DTO deberá probarse con el formatter MVC real, no solo con JsonSerializer aislado.

CamposAdicionales es infraestructura de entrada: no se mapea a Application, no se persiste ni se devuelve. Validar con miembro "fechaInicio" para el error de esta regla. No permitir que se capture como una propiedad reconocida ni ignorarla con JsonIgnore suponiendo que eso detecta presencia.

Alternativas:
- JsonUnmappedMemberHandling.Disallow en este DTO también rechaza fechaInicio null, pero rechaza todos los campos desconocidos y rompería la prueba actual que ignora usuarioId. No es la recomendación mínima.
- Aplicar Disallow globalmente afectaría otros endpoints: descartado para este alcance.
- Convertidor JSON propio permitiría inspeccionar todo el objeto, pero añade lógica de parsing/mapeo innecesaria aquí.
- Mantener DateOnly? FechaInicio y validar solo !=null no detecta null explícito: descartado.

[Microsoft describe el tratamiento por defecto y el atributo por tipo](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/missing-members). No se cambia ninguna opción global de JSON.

## 5. PUT /api/habitos/{id} — diseño aprobado, no implementado

DEC-01 aprueba ampliar el PUT existente: mantiene ruta, 200, nombre y días obligatorios y semántica actual de descripción (omisión/null elimina descripción). Solo los dos metadatos nuevos tienen conservación por omisión. No convertir todo el PUT en PATCH ni añadir un endpoint solo para cada metadato.

| Campo | Omitido | Null explícito | Valor informado |
| --- | --- | --- | --- |
| nombre | 400, como hoy | 400 | No vacío/no espacios |
| descripcion | Ausencia, como hoy | Ausencia, como hoy | Texto |
| diasProgramados | 400, como hoy | 400 | Al menos un día válido |
| categoriaId | Conservar, incluido null histórico | 400 aprobado, incluso en histórico | Guid no vacío y existente; asignar/reemplazar |
| iconoId | Conservar, incluido null histórico | 400 aprobado, incluso en histórico | Clave oficial exacta; asignar/reemplazar |

No permitir borrar categoría ni ícono en V1, conforme a DEC-01. Un histórico puede editar nombre/días y mantener ambos null. Libro no se incorpora implícitamente.

Ejemplo para cambiar ambos metadatos sin cambiar días:

```json
{
  "nombre": "Leer 20 minutos",
  "descripcion": null,
  "diasProgramados": [
    1,
    2,
    3,
    4,
    5
  ],
  "categoriaId": "82c0b8e6-0b9d-4e18-9bf8-000000000005",
  "iconoId": "billete"
}
```

La combinación Aprendizaje y crecimiento + Billete es válida; ilustra independencia, no una recomendación visual.

### 5.1 Presencia en HTTP y normalización hacia Application

Estrategia localizada aprobada para EditarHabitoHttpRequest: propiedades tipadas Guid? CategoriaId y string? IconoId con backing fields e init que registran presencia. Indicadores CategoriaIdInformada/IconoIdInformado, privados para escritura y marcados JsonIgnore, se inicializan en false y solo cambian durante asignación de sus propiedades correspondientes. Nunca se aceptan del cuerpo.

Validación de objeto:
- Indicador false: conservar.
- Indicador true y valor null: 400.
- Indicador true con Guid.Empty/clave vacía: 400.
- UUID mal formado o tipo JSON incompatible: 400 de model binding.
- ID bien formado: Application verifica pertenencia al catálogo.
- Regla localizada aprobada: contar asignaciones y rechazar una segunda aparición de categoriaId/iconoId, incluso con distinto casing, con 400. Evita ambigüedad valor/null sin endurecer el resto del API.

Un nullable común sin indicador no distingue omisión de null. La semántica depende de estos indicadores reales y de pruebas usando el serializer MVC. No habilitar IgnoreNullValues ni opciones de lectura que omitan la asignación de null. No usar [Required] en estos dos campos de PUT: haría obligatoria la clasificación histórica.

Después de validar HTTP, no existe una operación de borrado. Por eso puede normalizarse sin wrapper genérico en Application:
EditarHabitoRequest(string Nombre, string? Descripcion, IReadOnlyCollection<DayOfWeek> DiasProgramados, Guid? CategoriaId = null, string? IconoId = null).
En este contrato INTERNO, null significa exclusivamente conservar; no significa null explícito del cliente, porque ese caso se rechazó antes. Un caller interno tampoco dispone de operación de borrado. Si en una versión futura se aprobara borrar, habría que revisar esta representación; no anticiparlo ahora.

Esta propuesta evita un tipo Optional<T> compartido con JSON/EF. Una alternativa es un CambioCampo<T> explícito sin dependencias; aporta poco mientras solo haya conservar/asignar. Requiere más DTOs y mapeos, por eso no se recomienda para V1.

### 5.2 Orden y efectos de edición

Mantener búsqueda por hábito y usuario, 404 para ajeno/inexistente y concurrencia actual. Comprobar todos los valores y catálogos antes de mutar el agregado rastreado; no cambiar categoría y luego descubrir un error de días.

Con mismos días, cambiar categoría/ícono no cambia FechaInicio, FechaCreacion, Estado, programación, períodos de estado ni cumplimientos. Si la misma solicitud también cambia días, solo ese cambio aplica las reglas existentes de versión semanal. Conservar ConcurrencyVersion y guardado atómico; no añadir ETag/If-Match en esta tarea ni afirmar que se detectan formularios obsoletos desde una lectura anterior al caso de uso.

No crear reactivación ni una prohibición nueva de edición de inactivos: las pruebas actuales permiten editar su configuración. No introducir un bloqueo nuevo de historial completo en PUT si la operación no depende de él. La edición sigue usando la fecha local para programación y conserva su tratamiento de zona inválida.

## 6. Catálogo de categorías e IDs aprobados y congelados

Recomiendo GET /api/habitos/categorias, con Authorize coherente con HabitosController. No colisiona con {id:guid}; devolver 200 con array ordenado por orden ascendente. Catálogo global, sin usuarioId en la respuesta. Sin POST/PUT/DELETE de categorías.

Respuesta contractual aprobada completa (todavía no implementada):

```json
[
  {
    "id": "82c0b8e6-0b9d-4e18-9bf8-000000000001",
    "nombre": "Salud y movimiento",
    "orden": 1
  },
  {
    "id": "82c0b8e6-0b9d-4e18-9bf8-000000000002",
    "nombre": "Alimentación e hidratación",
    "orden": 2
  },
  {
    "id": "82c0b8e6-0b9d-4e18-9bf8-000000000003",
    "nombre": "Descanso y sueño",
    "orden": 3
  },
  {
    "id": "82c0b8e6-0b9d-4e18-9bf8-000000000004",
    "nombre": "Bienestar emocional",
    "orden": 4
  },
  {
    "id": "82c0b8e6-0b9d-4e18-9bf8-000000000005",
    "nombre": "Aprendizaje y crecimiento",
    "orden": 5
  },
  {
    "id": "82c0b8e6-0b9d-4e18-9bf8-000000000006",
    "nombre": "Productividad y organización",
    "orden": 6
  },
  {
    "id": "82c0b8e6-0b9d-4e18-9bf8-000000000007",
    "nombre": "Relaciones y vínculos",
    "orden": 7
  },
  {
    "id": "82c0b8e6-0b9d-4e18-9bf8-000000000008",
    "nombre": "Finanzas personales",
    "orden": 8
  },
  {
    "id": "82c0b8e6-0b9d-4e18-9bf8-000000000009",
    "nombre": "Creatividad y ocio",
    "orden": 9
  },
  {
    "id": "82c0b8e6-0b9d-4e18-9bf8-000000000010",
    "nombre": "Hogar y vida cotidiana",
    "orden": 10
  }
]
```

Estos diez UUID están APROBADOS DEFINITIVAMENTE por DEC-02 y congelados por PREC-02; todavía no se persisten por esta tarea. CAT-01/CAT-02 deberán utilizar exactamente los mismos en definición, precarga, fixtures y ejemplos; no regenerarlos entre entornos. El sufijo legible no permite calcular identidad desde orden: cambiar orden no cambia ID. No tienen relación con Penpot ni con el nombre.

No se crean seeds aquí. Cualquier cambio futuro de identidad requiere una decisión explícita y una estrategia de compatibilidad; no regenerar ni sustituir estos UUID durante la implementación. No se renombra una categoría para cambiar su identidad y no hay categorías personalizadas en V1.

Application futura (diseño aprobado): CategoriaHabitoListado(Guid Id, string Nombre, int Orden) y ListarCategoriasHabito.EjecutarAsync(CancellationToken=default):Task<IReadOnlyList<CategoriaHabitoListado>>. Un catálogo de lectura en Application ofrece listar y comprobar existencia; Infrastructure implementa acceso EF, sin DbContext en controller/Application. Los nombres exactos de interfaces técnicas podrán concretarse en CAT-01 sin cambiar este contrato HTTP.

El catálogo no depende del historial de estados: no responder 503 por readiness de TEMP-005 a una lectura de opciones independiente. Sí conservar 401 para acceso no autenticado.

## 7. Catálogo de íconos: claves, recursos y lectura

DEC-02 aprueba IconoId como string ASCII minúscula, comparada ordinalmente, elegida de una allowlist estática versionada de dieciséis elementos. No usar Guid de Penpot, posición de grilla, nombre visible ni filename como identidad. Las dieciséis claves siguientes están aprobadas y congeladas; no son propiedades ya implementadas en el backend.

| Orden de presentación aprobado | iconoId aprobado | Nombre visible actual | Referencia gráfica del manifiesto FRONTEND |
| --- | --- | --- | --- |
| 1 | libro | Libro | constia-lectura.png |
| 2 | fuerza | Fuerza | constia-movimiento.png |
| 3 | agua | Agua | constia-agua.png |
| 4 | loto | Loto | constia-meditacion.png |
| 5 | manzana | Manzana | constia-alimentacion.png |
| 6 | luna | Luna | constia-luna.png |
| 7 | cafe | Café | constia-cafe.png |
| 8 | corredor | Corredor | constia-corredor.png |
| 9 | corazon | Corazón | constia-corazon.png |
| 10 | cerebro | Cerebro | constia-cerebro.png |
| 11 | sol | Sol | constia-sol.png |
| 12 | huella | Huella | constia-huella.png |
| 13 | brote | Brote | constia-brote.png |
| 14 | portatil | Portátil | constia-portatil.png |
| 15 | musica | Música | constia-nota-musical.png |
| 16 | billete | Billete | constia-billete.png |

La clave no cambia si se cambia un nombre, ruta o posición. Fuerza/Movimiento, Loto/Meditación y Manzana/Alimentación son alias visuales, no claves adicionales. Confeti, marca y controles no cuentan como íconos seleccionables. Los PNG oficiales mantienen proporciones y transparencia; no se crean ni copian recursos en API-CAT-01.

Definición estática única en Domain, sin I/O ni referencias a archivos; Application usa esa misma definición para validar y listar. No se propone tabla EF para íconos. Recomiendo además GET /api/habitos/iconos, autorizado y de solo lectura: expone la misma allowlist, evita que el frontend replique los IDs y no agrega persistencia. Frente a un manifiesto duplicado en frontend/backend, un caso de uso trivial de lectura cuesta menos coordinación y evita catálogos funcionales diferentes.

Respuesta 200 array ordenado; cada elemento tiene id:string, nombre:string y orden:int. Inicio y final ilustrativos; la respuesta real contendrá los dieciséis, sin campos de ruta:
```json
[
  {
    "id": "libro",
    "nombre": "Libro",
    "orden": 1
  },
  {
    "id": "billete",
    "nombre": "Billete",
    "orden": 16
  }
]
```

El ejemplo anterior está abreviado y no representa un catálogo de dos elementos. En implementación se debe devolver el inventario completo de la tabla. No exponer una ruta de disco, URL arbitraria o PNG en Base64. En la implementación futura, el frontend mantendrá un manifiesto de clave→asset oficial; debe comprobar que cubre exactamente las claves recibidas, sin deducir filename del ID. Para una clave futura desconocida, mostrar estado neutro de recurso no disponible, sin sustituirla silenciosamente por otro ícono oficial.

Application futura (diseño aprobado): IconoHabitoListado(string Id, string Nombre, int Orden) y ListarIconosHabito.Ejecutar():IReadOnlyList<IconoHabitoListado>, sin consultas SQL. Catálogo versionado en releases del backend; no añadir versionado de API ni un parámetro de versión por este trabajo. No habilitar nuevos IDs que el cliente no pueda representar sin coordinar releases.

## 8. Contratos de consulta y respuestas de escritura

Agregar al final de los resultados existentes:
- HabitoCreado: Guid? CategoriaId y string? IconoId; conservar UsuarioId interno.
- HabitoListado: Guid? CategoriaId y string? IconoId.
- HabitoResponse: Guid? CategoriaId y string? IconoId.

No devolver UsuarioId en HTTP, como hoy. No exponer entidades EF. Conservar exactamente los nombres públicos id, nombre, descripcion, createdAt, startDate, estado y diasProgramados. FechaInicio sigue saliendo como startDate, no crear otro campo fechaInicio. startDate es DateOnly persistido; createdAt es DateTimeOffset UTC. No recalcular startDate al consultar ni al editar.

Los dos campos nuevos se incluyen con null explícito en históricos (no se omiten). Mantenerlo mediante configuración local del response si hiciera falta, sin cambiar reglas globales. Listas siguen siendo arrays, no agregar envoltorios ni paginación en esta tarea.

Respuesta de alta 201 y edición/detalle 200, ejemplo temporal ilustrativo para Buenos Aires:

```json
{
  "id": "34b24730-44b1-4baf-a225-272246d63301",
  "nombre": "Leer 20 minutos",
  "descripcion": null,
  "createdAt": "2026-10-19T02:30:00+00:00",
  "startDate": "2026-10-18",
  "estado": 0,
  "diasProgramados": [
    1,
    2,
    3,
    4,
    5
  ],
  "categoriaId": "82c0b8e6-0b9d-4e18-9bf8-000000000005",
  "iconoId": "libro"
}
```

Para un histórico, el mismo esquema puede contener "categoriaId":null y "iconoId":null, con sus fechas originales. Para GET /api/habitos y GET /api/habitos/inactivos se devuelve un array de este esquema. PUT devuelve los valores finales, conservados o reemplazados, no solo el delta.

No hay endpoint de programación histórica por fecha en el controller actual. diasProgramados sigue representando la versión abierta actual; agregar metadatos no lo convierte en una consulta histórica ni altera vigencias.

Rendimiento: devolver IDs escalares evita Include de categoría, carga de imagen y búsquedas por cada hábito. Mantener filtro de propietario/estado, orden actual y AsNoTracking; no hacer inner join que descarte null históricos. Consultar cada catálogo por separado, no desde el mapper por fila. Un eventual enriquecimiento con nombres necesitaría una proyección o lectura de catálogo en bloque, no N+1.

## 9. Matriz de validación y códigos HTTP aprobados para implementación futura

Casos individuales, suponiendo autenticación, recurso propio, resto del cuerpo válido y readiness disponible salvo que se indique. El orden de errores combinados no es un contrato: autenticación y model binding pueden responder antes del caso de uso.

| Condición | POST crear | PUT editar / consulta | Formato y efecto |
| --- | --- | --- | --- |
| Solicitud válida | 201 | PUT 200; GET 200 | HabitoResponse / array |
| categoriaId ausente | 400 | PUT conserva; 200 | Validación en alta; sin clasificación forzada al editar |
| categoriaId null | 400 | PUT 400 aprobado | ValidationProblemDetails |
| categoriaId UUID vacío | 400 | PUT 400 | No persistir |
| categoriaId mal formado/tipo incorrecto | 400 | PUT 400 | Error de binding |
| Categoría bien formada inexistente | 400 | PUT 400 | No usar 404 de hábito; error del campo |
| iconoId ausente | 400 | PUT conserva; 200 | Sin default |
| iconoId null | 400 | PUT 400 aprobado | No borrar |
| iconoId vacío, espacios o desconocido | 400 | PUT 400 | Valor fuera de allowlist |
| fechaInicio presente con fecha u otro valor | 400 | PUT no la modifica, fuera de campos editables | Rechazo obligatorio localizado en POST |
| fechaInicio presente con null | 400 | PUT no la modifica | Presencia, no comparación con null |
| diasProgramados ausente/null/vacío | 400 | PUT 400 | Mantener obligatoriedad |
| Día fuera de 0..6 | 400 | PUT 400 | No guardar |
| Días repetidos válidos | Normalizar | Normalizar | Conservar comportamiento actual, sin versión si conjunto no cambia |
| nombre ausente/null/blanco | 400 | PUT 400 | Validación existente |
| CategoriaId/IconoId duplicados en PUT | No se endurece el POST por esta regla | PUT 400 aprobado | Local a nuevos metadatos; incluso con casing diferente |
| Histórico sin categoría/ícono | No permite nuevas altas así | GET 200 con null; PUT omitidos conserva null | No inferir ni ocultar el hábito |
| Edición concurrente durante carga/guardado | No aplica como edición | 409 | ProblemDetails actual |
| Conflicto de vigencia al cambiar días | No aplica | 409 | Conservar actual |
| Usuario no autenticado/identidad inválida | 401 | 401 | Conservar challenge/JWT y respuestas actuales |
| Hábito inexistente o ajeno | No aplica a la ruta de alta | 404 | Mismo resultado, sin revelar existencia |
| Snapshot TEMP-005 no capturado | 503 | No agregar bloqueo a lecturas/PUT independientes | code habit_status_history_not_initialized |
| Snapshot capturado pero backfill no completo | Alta puede continuar, como hoy | Desactivación/cumplimiento 503; PUT/lectura no se bloquean por esta nueva funcionalidad | Preservar diferencia entre snapshot e historial completo |
| Estado histórico desconocido para cumplimiento | No convertir alta en consulta histórica | Operación dependiente 422 | code habit_status_history_unknown, conservar |
| Zona IANA persistida inválida | 500 recomendado al integrar el cálculo | PUT conserva 500 actual | ProblemDetails, sin fallback ni persistencia |
| Cumplimiento duplicado, inactivo o historia no Activa | Sin cambio | Operación existente 409 | Fuera del nuevo contrato; conservar |

En PUT, FechaInicio continúa sin autoridad y no pertenece al request. Para no ampliar la ruptura a otro endpoint, esta propuesta conserva que un campo no mapeado se ignore allí; nunca se aplicará. Si se desea rechazar también su presencia en PUT, requiere aprobación de ese endurecimiento. El rechazo obligatorio aprobado corresponde al POST.

Errores nuevos de validación: application/problem+json con ValidationProblemDetails (familia ProblemDetails), status 400 y errors por campo. Reutilizar validación automática para binding y DTO; catálogos se validan en Application y se traducen con ValidationProblem en la acción, sin consultas BD desde validadores HTTP. No convertir toda ArgumentException o toda DbUpdateException en "categoría inválida". Mantener el tratamiento de las excepciones existentes.

Ejemplo de error nuevo por fecha prohibida:
```json
{
  "title": "Los datos del hábito no son válidos.",
  "status": 400,
  "errors": {
    "fechaInicio": [
      "No se permite enviar FechaInicio; el backend determina la fecha de inicio."
    ]
  }
}
```

Puede incluir type, instance y traceId del pipeline. Los mensajes humanos y el URI type no son discriminadores estables aprobados. errors.fechaInicio se fija para la nueva regla; claves de errores de binding existentes pueden seguir siendo Nombre o $.categoriaId. No prometer una normalización global de claves JSON a camelCase ni cambiar ApiBehaviorOptions para otros endpoints.

Los códigos ya existentes habit_status_history_unknown y habit_status_history_not_initialized se conservan. No se agregan códigos globales nuevos sin acordarlos. 401 del middleware puede no tener el mismo cuerpo que Unauthorized(ProblemDetails); se conserva esa diferencia y no se afirma que todos los errores actuales estén uniformados. 404 conserva el comportamiento actual de MVC. Añadir Location en POST sería coherente con creación REST, pero se deja a aprobación/API-004 para no presentarlo como una garantía existente.

## 10. Reloj único en Application, conservación de TEMP-005

Hoy Habito.FechaCreacion utiliza UtcNow directamente, mientras FechaLocalUsuario.ObtenerHoy lee TimeProvider; no producen necesariamente el mismo instante. Solo inyectar ObtenerHoy en CrearHabito no resuelve ese desacople.

Diseño temporal aprobado para implementación posterior:
- Agregar al servicio temporal existente IFechaLocalUsuario una operación de conversión pura DateOnly ObtenerFechaLocal(DateTimeOffset instanteUtc, string timeZoneId). Mantener ObtenerHoy(string) para los consumidores actuales; puede delegar en la conversión.
- Inyectar TimeProvider en CrearHabito. Tras propietario, readiness y validaciones, capturar exactamente una vez instanteUtc = timeProvider.GetUtcNow().ToUniversalTime().
- Convertir ese instante con ObtenerFechaLocal(instanteUtc, usuario.TimeZoneId), que no consulta otro reloj y sigue usando IZonaHorariaIana. Usar el snapshot de zona leído del propietario en esa operación; no releerlo a mitad del cálculo.
- En una modificación futura del constructor/factory de Habito, recibir explícitamente DateTimeOffset fechaCreacionUtc y DateOnly fechaInicio, además de categoría/ícono. Domain no consulta relojes y no calcula IANA.
- FechaCreacion = instanteUtc; FechaInicio = fecha local; programación inicial.VigenteDesde = fecha local; período inicial Activo.VigenteDesde = fecha local.
- Infrastructure solo persiste; mantener un guardado atómico y ConcurrencyVersion inicial. No almacenar una fecha de cliente ni recalcular nada en EF.
- El instante es el momento de creación en backend, no la apertura del formulario. Reintentar una solicitud sin idempotency key puede crear otra entidad, como hoy; no se promete idempotencia nueva.

No usar ObtenerHoy después de capturar instanteUtc porque volvería a leer el reloj. No introducir TimeProvider en Domain ni convertir DateOnly histórico a medianoche UTC. Unificar firma del constructor y fixtures se hará en HAB-DATE-01 autorizado, no aquí.

Conservar gate SnapshotCapturadoAsync para altas. No sustituirlo por HistorialCompletoAsync: impediría altas que la base integrada ya permite durante la fase posterior a captura. Conservar los gates de desactivación/cumplimientos, corte UTC y snapshot inmutable. La captura temporal de una nueva alta es independiente del corte histórico ya persistido.

Medianoche y DST: convertir desde UTC evita elegir una hora local ambigua. Instante 2026-10-19T02:30:00Z da fecha 18 en America/Argentina/Buenos_Aires y 19 en Asia/Tokyo. Crear domingo con L–V mantiene domingo como inicio y lunes como primera oportunidad. No reescribir fechas históricas futuras/pasadas ni reconstruir períodos desconocidos.

## 11. Consumidores y transición de contrato

Búsqueda del worktree integrado, no de otros worktrees:
- src/Constia.Web/Components/Pages solo contiene plantilla Home/Error/NotFound; no se encontró consumidor implementado del flujo de hábitos ni cliente HTTP de ese formulario.
- tests/Constia.Tests/CrearHabitoTests.cs construye CrearHabitoRequest con fecha y exige conservarla. Deberá cambiar su expectativa de alta y conservar las demás validaciones.
- tests/Constia.API.Tests/HabitosControllerTests.cs usa POST/PUT/GET; CrearHabito_ConJwtValido_Devuelve201YGuardaElHabito espera StartDate 2026-10-06. CrearHabito_IgnoraUsuarioIdEnJsonYUsaElSubDelToken prueba una propiedad desconocida ignorada. Conservar esa garantía de propiedad al cambiar el DTO.
- tests/Constia.API.Tests/JwtApiFactory.cs sustituye repositorios, readiness y reloj; las pruebas no acreditan constraints SQL.
- src/Constia.API/Constia.API.http solo contiene una petición de plantilla a weatherforecast; no es un cliente del alta. No se encontraron scripts de creación en tools, docs o .github. El CLI StateHistoryBackfill es una herramienta de datos, no consumidor del contrato HTTP de alta.
- No se verificaron consumidores externos, herramientas manuales, aplicaciones desplegadas ni telemetría. Su ausencia en el repositorio no demuestra inexistencia.

Cambios: POST es incompatible (IDs nuevos obligatorios y fecha prohibida). PUT conserva compatibilidad de metadatos omitidos; un cliente que reenvíe los null del GET histórico al PUT recibirá 400 según DEC-01 y debe omitirlos. No reutilizar un response como request. Campos extra en respuestas suelen ser aditivos, pero un consumidor con esquema cerrado puede romperse: requiere inventario real.

DEC-08 aprueba conceptualmente el corte coordinado sobre la misma ruta, con ejecución condicionada a que el inventario real confirme su viabilidad: desplegar esquema aditivo, probar migración/compatibilidad, preparar clientes, impedir escritores antiguos y activar servidor/cliente estrictos coordinadamente. La obligatoriedad no se declara habilitada mientras existan instancias o rutas antiguas que creen null. No mantener una ruta vieja que la eluda.

Si hay clientes externos que impiden ese corte, volver a aprobar la estrategia (versionado o retiro controlado) antes de implementar. Este documento no autoriza versionado ni configura flags. No inventar categoría/ícono para acomodar clientes antiguos. Los null de históricos son compatibles con lecturas, no una excepción para altas posteriores al cambio.

## 12. Pruebas futuras — definición, no ejecución

| Nivel / suite | Casos y resultados que deben fijarse |
| --- | --- |
| Unitarias en Constia.Tests | Catálogo exacto diez IDs/nombres/orden y dieciséis claves únicas; categorías/íconos inválidos no guardan; Libro no se asigna por omisión; combinaciones categoría/ícono independientes |
| Unitarias de Application/Domain | Crear con único instante: FechaCreacion exacta y tres inicios civiles iguales; TimeProvider espía con una sola lectura; zona inválida sin persistencia; BA/Tokio, 02:59:59Z/03:00Z y DST |
| Unitarias de edición | Omitidos conservan null/valor; valores nuevos válidos reemplazan; inválido no muta parcialmente; metadatos solos no alteran períodos/días/estado/fechas/cumplimientos; inactivos mantienen reglas actuales |
| HTTP MVC con JwtApiFactory | POST válido 201 y salida ampliada; requeridos ausentes/null/Guid.Empty/desconocidos; días inválidos; fechaInicio con valor/null y casing/escapes/duplicados produce 400 con resto válido; propiedad desconocida usuarioId sigue sin autoridad |
| HTTP presencia en PUT | Campo omitido frente a null explícito; primera asignación en histórico; reemplazo; no borrado; duplicados/casing según DEC-04 y PREC-01; flags internos del DTO no se aceptan del cuerpo ni se serializan como dominio |
| HTTP consultas/catálogos | GET activos/inactivos/detalle y resultados POST/PUT incluyen IDs; null explícito histórico; nombres existentes, enums y fechas conservados; catálogo completo ordenado y autenticado |
| HTTP TEMP-005 | Snapshot no capturado 503 sin persistencia; capturado/incompleto permite alta; operaciones dependientes conservan 503 y 422; 401/404/409 intactos; sin requisito artificial de historial completo para catálogos |
| SQL Server aislado | FK y precarga de diez categorías, transiciones null, persistencia de ícono, dos contextos en edición con conflicto/rollback, conservación exacta de filas/fechas/períodos/snapshot/cumplimientos y unicidad COMP-005 |

Reutilizar CrearHabitoTests, EditarHabitoTests, HabitoTests, FechaLocalUsuarioTests y suites HTTP existentes. Para SQL reutilizar fixtures desechables de Temp003MigrationSqlServerTests, HabitoConcurrencySqlServerTests, RegistrarCumplimientoSqlServerTests y Temp005StateHistorySqlServerTests; requieren habilitación/CI aislado, no una base persistente. No es necesario otro framework para estos casos.

No se escriben ni ejecutan tests en API-CAT-01. La validación de JSON en este documento no equivale a probar el formatter, el servicio ni SQL Server.

## 13. Archivos de implementación futura y secuencia revisable

Los archivos siguientes NO se modifican en esta tarea.

| Trabajo posterior | Archivos y responsabilidad |
| --- | --- |
| Contrato HTTP localizado | src/Constia.API/Contracts/CrearHabitoHttpRequest.cs y EditarHabitoHttpRequest.cs: extensión/presencia/validación; HabitoResponse.cs: IDs nullable; DTOs nuevos de catálogo separados de entidades |
| Casos de uso y resultados | src/Constia.Application/Habitos/CrearHabitoRequest.cs, EditarHabitoRequest.cs, CrearHabito.cs, EditarHabito.cs, HabitoCreado.cs, HabitoListado.cs, ListarHabitosActivos.cs, ListarHabitosInactivos.cs y ObtenerHabitoPorId.cs |
| Catálogos | Nuevos Domain/CategoriaHabito.cs y definición estática de íconos; Application/Habitos con lectura/validación; Infrastructure implementa solo catálogo persistido. Nombres finales de archivos nuevos se cierran en CAT-01 |
| Reloj único | src/Constia.Application/Temporal/IFechaLocalUsuario.cs y FechaLocalUsuario.cs, Domain/Habito.cs y CrearHabito/fixtures para recibir instante explícito; conservar IZonaHorariaIana |
| Persistencia | src/Constia.Infrastructure/Configurations/HabitoConfiguration.cs, nueva configuración de categorías, migración/snapshot futuros y repositorios acotados por propietario |
| Adaptador HTTP/DI | src/Constia.API/Controllers/HabitosController.cs y Program.cs: mapeos, respuestas, lectura de catálogos y dependencias, sin cambiar opciones JSON globales |
| Pruebas/CI | tests/Constia.Tests y tests/Constia.API.Tests, incluidos fakes; .github/workflows/ci.yml solo cuando se agreguen pruebas SQL autorizadas |

Orden posterior: revisión del cierre documental y autorización separada del punto de control Git → autorización posterior de implementación → CAT-01 catálogo → CAT-02 transición/esquema → casos de uso de categorías/íconos/reloj → adaptación HTTP → regresiones y SQL aislado → frontend con autorización propia. No hacer una única implementación ni migrar antes de cerrar contratos.

## 14. Registro de aprobación explícita — 19/10/2026

**APROBADO — Diseño técnico y contractual — 19/10/2026.**

**Aprobado para planificar su implementación posterior. No implementado ni verificado funcionalmente.**

### DEC-01 — Edición mediante PUT

Aprobado ampliar PUT /api/habitos/{id}. categoriaId e iconoId omitidos conservan sus valores actuales, incluidos null históricos. Null explícito en cualquiera produce 400; un identificador válido asigna o reemplaza y uno inválido se rechaza. No se permite borrar categoría ni ícono en V1. Un histórico con ambos null puede editarse sin clasificación obligatoria si los omite. Nombre, descripción y días conservan el contrato vigente, sin convertir toda la operación en PATCH.

### DEC-02 — Identificadores estables

Aprobados definitivamente los diez UUID de la sección 6: prefijo completo 82c0b8e6-0b9d-4e18-9bf8- y sufijos 000000000001 a 000000000010, en el orden oficial. No se generan nuevos UUID ni se cambian los existentes.

Aprobadas las dieciséis claves: libro, fuerza, agua, loto, manzana, luna, cafe, corredor, corazon, cerebro, sol, huella, brote, portatil, musica y billete. Son independientes del nombre visible, PNG, orden, Penpot y categoría. No regenerarlas ni renombrarlas arbitrariamente tras su persistencia. Criterio de aceptación futura: exactamente diez UUID únicos y dieciséis claves únicas con los valores aprobados, no solo el conteo.

### DEC-03 — Catálogos de lectura

Aprobados GET /api/habitos/categorias y GET /api/habitos/iconos. Ambos requieren autenticación, son de solo lectura, devuelven arrays ordenados con identidad estable y no permiten CRUD. Categorías usa persistencia relacional; íconos una definición estática versionada sin tabla SQL adicional.

### DEC-04 — Validación JSON localizada

Aprobados JsonExtensionData e IValidatableObject en el DTO de creación para rechazar cualquier presencia de FechaInicio, incluido null. Aprobados los indicadores de presencia en PUT y el rechazo de propiedades duplicadas de categoriaId/iconoId, incluidas variantes de casing. Sin configuración JSON global, convertidor global ni abstracción genérica innecesaria. Es obligatorio verificar el comportamiento mediante HTTP MVC real; aprobar la estrategia no prueba que funcione.

### DEC-05 — Respuestas HTTP ampliadas

Aprobados categoriaId:Guid? e iconoId:string? en creación, edición, consulta individual y listados de activos/inactivos. Históricos devuelven null explícito. Se preservan createdAt, startDate, estado, diasProgramados, enums numéricos y formatos temporales vigentes. Sin propiedades redundantes ni cambios globales de serialización.

### DEC-06 — Un único instante UTC

Aprobada una sola captura UTC con TimeProvider para derivar FechaCreacion, FechaInicio civil local, inicio de programación e inicio del período Activo usando la zona IANA persistida. Domain recibe valores explícitos, sin reloj ni resolución IANA. Conservar SnapshotCapturadoAsync de TEMP-005 y todas sus garantías; no recalcular ni reescribir fechas históricas.

### DEC-07 — Errores HTTP

Aprobados nuevos errores de validación 400 con ValidationProblemDetails. Conservar 401, 404, 409, 422 y 503 donde correspondan, sin reinterpretar excepciones generales como errores de catálogo ni refactorizar globalmente el middleware. Location en POST queda fuera de API-CAT-01 para una revisión separada en API-004.

### DEC-08 — Compatibilidad y transición

Aprobado conceptualmente un corte coordinado sobre la misma ruta, condicionado al inventario real de consumidores antes del despliegue. Identificar clientes dependientes, coordinar el cambio incompatible, impedir escritores antiguos que creen sin categoría/ícono y probar esquema aditivo/migraciones antes de activarlo. Si hay consumidores externos incompatibles, solicitar una nueva decisión del usuario. No implementar versionado preventivo ni mantener una ruta antigua que eluda obligatoriedad.

Se conservan las decisiones de producto previas: diez categorías, dieciséis íconos, ambos obligatorios e independientes en altas nuevas; null históricos permitidos; sin categorías personalizadas, reactivación ni cambios retroactivos. PEN-006 sigue aprobado visual y estáticamente, no como comportamiento funcional.

## 15. Review Focus y verificaciones documentales históricas

Este apartado conserva las comprobaciones de la redacción inicial de API-CAT-01, anterior al presente cierre. Sus referencias a valores propuestos y revisión pendiente son históricas; el estado vigente aprobado está en el apartado 14 y las precisiones en el 16.

- FechaInicio null o con casing distinto debe rechazarse; la prueba HTTP debe demostrar presencia, no solo cambio de DTO.
- GET histórico devuelve null y un cliente no debe reenviarlo como borrado en PUT.
- Un cambio de metadata no crea una versión semanal y una validación fallida no deja mutación parcial.
- Una instancia antigua no debe seguir creando null después de activar obligatoriedad.
- La captura temporal única no debe releer reloj ni sustituir SnapshotCapturado por HistorialCompleto.

Skills: dotnet-webapi, constia-design-system, superpowers:writing-plans y superpowers:verification-before-completion. Se respetan las restricciones explícitas del usuario frente a recomendaciones genéricas: no convertir enums a strings, no sustituir DateOnly civil por DateTimeOffset, no instalar herramientas ni ejecutar tests/commits por este plan.

Limitaciones: análisis de fuentes y documentación oficial, sin ejecución del serializer/HTTP/producto, sin SQL ni inspección de consumidores externos. La aprobación estática de PEN-006 se consulta desde la skill vigente, sin nuevas lecturas o cambios de Penpot. No se afirma que ninguna propuesta ya funcione.

La comprobación final contrastará rama, HEAD y hashes de ambos worktrees: se exige un único archivo nuevo y ningún cambio en el inventario previo, incluidos los cinco documentos de AUD-BASE. git diff --check revisará existentes y documento nuevo mediante comparación sin indexarlo. El resultado se informa al usuario; no se crean commits ni push.

### Resultado de la verificación final de API-CAT-01

- AUD-BASE conserva rama chore/AUD-BASE y HEAD 30058bbcd90e8f6a3733784f6826470eb67989d4; cero commits por delante de origin/main e índice sin cambios preparados.
- Los 169 archivos previamente inventariados en AUD-BASE conservan sus SHA-256, incluidos los cuatro documentos funcionales y el plan AUD-BASE. Única adición: este documento.
- El checkout original conserva feature/TEMP-004, HEAD 89c0a3933b978de6782d1f80a0e2da244588379b, mismo status y hashes de sus 419 archivos inventariados; ninguna adición, modificación ni pérdida.
- Sin cambios en src, tests, tools o .github y sin migraciones nuevas. No se ejecutaron pruebas, compilación, accesos SQL, commits, push o PR ni operaciones de escritura en otros servicios.
- git diff --check de cambios previos: salida 0. Documento nuevo comprobado sin indexarlo con git diff --no-index --check contra archivo vacío: código 1 de diferencia, cero mensajes de errores de espacios. Enlaces locales sin destinos rotos.
- Ocho ejemplos JSON sintácticamente válidos; diez UUID propuestos únicos con formato válido y dieciséis claves de ícono únicas. Son comprobaciones del documento, no de la API.
- Entrega detenida para revisión y aprobación de los contratos; ninguna tarea CAT-01/CAT-02, UI-006 o PEN-007 iniciada.


## 16. Precisiones adicionales aprobadas — 19/10/2026

### PREC-01 — Verificación HTTP real como condición bloqueante

La aceptación funcional exige pruebas usando el formatter real de ASP.NET Core MVC, no solo inspección estática, llamadas directas al controller o JsonSerializer aislado. Cubrir como mínimo:
- Propiedad omitida, null explícito, valor válido e identificador inválido.
- Variantes de casing y propiedades repetidas de los dos metadatos en PUT.
- FechaInicio presente con valor y con null en POST, incluidos casing/escapes.
- Ausencia de cambios globales de JSON y compatibilidad con campos desconocidos que antes se ignoraban.

Si la estrategia no supera esas pruebas, detener la implementación y proponer un ajuste localizado antes de continuar. Esta aprobación no equivale a que el formatter ya se haya verificado. No se ejecutan esas pruebas en el cierre documental.

### PREC-02 — Identificadores congelados

Aprobación permanente de los diez UUID y dieciséis claves de DEC-02. Una vez implementados, cambiar identidad requiere decisión explícita y estrategia de compatibilidad. Los nombres visibles y los recursos gráficos pueden evolucionar sin modificar la clave funcional. Las verificaciones futuras deben contrastar valores exactos y unicidad, no generar sustitutos.

### PREC-03 — Punto de control Git previo a implementación

Antes de CAT-01 preparar inventario exacto de modificados/nuevos, revisar diffs y documentos sin seguimiento, separar cualquier código ajeno, proponer agrupación y mensaje de commit y comprobar ausencia de secretos/archivos personales y preservación del checkout original.

Este cierre solo prepara el punto de control del apartado 17: NO autoriza git add, commit, push, reset, stash, clean, rebase ni PR. La ejecución de staging/commit requiere autorización posterior, separada de la aprobación contractual. Ninguna tarea de implementación empieza con esta entrega.

## 17. Punto de control documental preparado — no ejecutado

### Inventario exacto propuesto para un único commit documental

Raíz exclusiva: C:/Users/FIO PC/.codex/worktrees/aud-base/Constia, rama chore/AUD-BASE, HEAD 30058bbcd90e8f6a3733784f6826470eb67989d4.

| Estado Git observado | Archivo | Resumen del contenido pendiente |
| --- | --- | --- |
| Modificado, versionado | MVP-SCOPE.md | Categorías V1, dieciséis íconos independientes, requisitos de altas, null históricos y fecha local automática; reglas de main conservadas |
| Modificado, versionado | SPECIFICATION.md | Contrato funcional, rechazo de FechaInicio, transición y antecedentes visuales; sin alterar reglas temporales de main |
| Modificado, versionado | ARCHITECTURE.md | Responsabilidades por capa y adaptación futura, conservación de COMP-005/TEMP-005 |
| Modificado, versionado | README.md | Resumen del alcance actualizado, sin reactivación, implementación nueva todavía pendiente |
| Nuevo, sin seguimiento | docs/superpowers/plans/2026-10-19-categorias-v1-fecha-inicio.md | Plan y evidencia de AUD-BASE, base integrada, preservación y transición propuestas en ese momento |
| Nuevo, sin seguimiento | docs/superpowers/plans/2026-10-19-api-cat-01-contratos-categorias-iconos-fecha-inicio.md | Diseño contractual aprobado, ocho decisiones, tres precisiones, evidencia técnica y este punto de control |

Los cinco primeros archivos no se modifican durante el cierre. El único archivo editado en esta ejecución es API-CAT-01; sigue sin seguimiento hasta un staging autorizado. Los pendientes de API-CAT-01 que todavía aparecen en documentos previos corresponden al estado histórico de AUD-BASE: este registro contiene la resolución aprobada, sin sobrescribir aquellos archivos.

Agrupación recomendada: un solo commit con estos seis Markdown porque forman la base documental coherente antes de CAT-01. No incluir assets, exportaciones, ZIP, scripts personales, archivos del checkout original ni cambios de implementación. Los cuatro diffs versionados agregan/ajustan documentación; sus eliminaciones sustituyen exclusión genérica de categorías, fecha manual y activación/reactivación imprecisa. Ambos planes nuevos se revisan completos, sin indexarlos.

### Staging selectivo propuesto — SOLO PARA AUTORIZACIÓN FUTURA

El comando siguiente se entrega como texto; NO fue ejecutado:

```powershell
git -C 'C:\Users\FIO PC\.codex\worktrees\aud-base\Constia' add -- 'MVP-SCOPE.md' 'SPECIFICATION.md' 'ARCHITECTURE.md' 'README.md' 'docs/superpowers/plans/2026-10-19-categorias-v1-fecha-inicio.md' 'docs/superpowers/plans/2026-10-19-api-cat-01-contratos-categorias-iconos-fecha-inicio.md'
```


Mensaje de commit sugerido:

    docs: reconcile V1 habit contracts and approve API-CAT-01

El mensaje incluye API-CAT-01 y agrupa reconciliación y aprobación documental. No se propone una implementación ni un cierre funcional. No se entrega ni ejecuta un comando de commit en esta tarea.

### Alcance de revisión y limitaciones

Se revisa el inventario de seis Markdown, su contenido y sus diffs, incluidos los planes nuevos. La detección de secretos combina revisión de contenido y patrones de claves privadas, tokens comunes y credenciales explícitas; no se copian valores sensibles al informe. Las rutas personales que identifican los worktrees son referencias de entorno, no archivos personales incluidos en el commit. No se consultan configuraciones personales ni se incorporan sus archivos.

No se ejecutan compilación, pruebas, migraciones, scripts de datos ni SQL. Las garantías COMP-005/TEMP-005 siguen respaldadas por código preservado y documentación, no por una nueva ejecución de pruebas. Skills efectivamente aplicadas en este cierre: dotnet-webapi, constia-design-system, writing-plans, verification-before-completion y using-git-worktrees para verificar aislamiento existente; no se crea otro worktree.

Próximo paso: revisión de este cierre y autorización explícita posterior para el punto de control Git. La autorización de implementación de CAT-01/CAT-02 será independiente. No iniciar UI-006 ni PEN-007.


### Resultado de verificación del cierre documental — 19/10/2026

- Estado principal APROBADO y alcance de aprobación técnica/contractual comprobados. Registrados DEC-01 a DEC-08 y PREC-01 a PREC-03, una vez cada uno.
- Diez UUID y dieciséis claves coinciden exactamente con los valores previos y los aprobados por el usuario. Ocho bloques JSON sin cambios; evidencia de contratos y verificaciones históricas conservada.
- Inventario del punto de control: cuatro Markdown versionados modificados y dos planes Markdown sin seguimiento; únicamente estos seis archivos. No se preparó ninguno en el índice.
- Comparación SHA-256 del inventario inicial del cierre: solo cambia este documento en AUD-BASE; los otros 169 archivos inventariados permanecen iguales. No se agregaron ni eliminaron archivos durante el cierre.
- Checkout original: los 419 archivos inventariados, status, feature/TEMP-004 y HEAD 89c0a3933b978de6782d1f80a0e2da244588379b permanecen iguales.
- AUD-BASE conserva chore/AUD-BASE y HEAD 30058bbcd90e8f6a3733784f6826470eb67989d4; cero commits nuevos, índice vacío.
- Sin cambios en src, tests, tools o .github y sin migraciones nuevas. No se ejecutaron operaciones SQL, compilación ni pruebas.
- git diff --check sin errores. Ambos planes sin seguimiento revisados también mediante --no-index --check, con cero mensajes de errores de espacios; código 1 esperado por diferencia respecto del archivo vacío. Enlaces locales del documento sin destinos rotos.
- Revisión de los seis candidatos y detección de patrones de secretos: cero hallazgos; ningún archivo personal entre los candidatos. Esto no sustituye revisión futura del diff preparado antes de autorizar el commit.
- No se ejecutaron git add, commit, push, reset, stash, clean, rebase ni creación de PR. Tampoco se modificaron Penpot, Trello, skills ni otros worktrees.
