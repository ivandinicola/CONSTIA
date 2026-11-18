# CONSTIA — Development Guide

## 1. Propósito

Este documento explica cómo preparar el entorno de desarrollo de CONSTIA, ejecutar sus servicios y trabajar con el proyecto localmente.

Debe mantenerse actualizado a medida que evolucione el proyecto.

---

## 2. Requisitos

Para desarrollar CONSTIA se necesitan:

* Windows con PowerShell 7.
* .NET SDK 10.
* Git.
* Docker Desktop con Docker Compose v2 o posterior y Docker Engine 28.0.0 o posterior.
* Docker con soporte para WSL 2.
* SQL Server 2022 mediante Docker.
* Un editor compatible con el proyecto, como Visual Studio Code o Codex.

### Tecnologías principales

* C#.
* .NET 10.
* ASP.NET Core Web API.
* Entity Framework Core 10.
* SQL Server 2022.
* Blazor.
* xUnit.
* Git.
* Docker.

---

## 3. Preparación del entorno

Antes de trabajar en CONSTIA, verificar que las herramientas principales estén disponibles.

### .NET

```powershell
dotnet --version
```

La versión utilizada por el proyecto debe corresponder a .NET 10.

### Git

```powershell
git --version
```

### Entity Framework Core

```powershell
dotnet ef --version
```

Si falta la herramienta, instalá la versión indicada en la sección de migraciones con `dotnet tool install --global dotnet-ef --version 10.0.12`.

### Docker

```powershell
docker --version
docker run hello-world
```

Docker Desktop debe estar ejecutándose. Antes de levantar SQL Server con Compose, verificá que la versión del servidor sea 28.0.0 o posterior.

```powershell
docker version --format '{{.Server.Version}}'
```

En versiones anteriores, otros equipos del mismo segmento de red podían acceder a puertos publicados en localhost. [Publicación de puertos en Docker](https://docs.docker.com/engine/network/port-publishing/#publishing-ports).

---

## 4. SQL Server de desarrollo

Desde la raíz del checkout, usá Docker Compose con el motor Linux activo. El `compose.yaml` fija SQL Server 2022 Developer por digest y acepta su EULA mediante `ACCEPT_EULA=Y`. Publica SQL únicamente en `127.0.0.1`. El puerto local predeterminado es `11433` y se puede cambiar con `CONSTIA_SQL_PORT`.

Elegí un nombre de proyecto Compose propio. Con `constia-dev`, el volumen de datos se llama `constia-dev_sql-data`. Para validar una instalación limpia, usá un nombre nuevo y comprobá que no exista un volumen de ese proyecto antes de arrancar.

```powershell
$env:COMPOSE_PROJECT_NAME = 'constia-dev'
$env:CONSTIA_SQL_PORT = '11433'
$env:MSSQL_SA_PASSWORD = Read-Host 'Contraseña local de SQL Server (sa)' -MaskInput
docker compose up -d --wait
docker compose ps
```

Usá una contraseña que cumpla la política de SQL Server. La salida de `docker compose ps` debe mostrar `healthy`: el control ejecuta `SELECT 1` con autenticación y comprueba el código de salida de `sqlcmd`. Un contenedor que solo figura como iniciado todavía puede estar inicializando SQL.

## 5. Configuración local

Si usás la alternativa `.env`, Compose lee la contraseña desde ese archivo local ignorado por Git. El ejemplo de conexión para EF y la API toma `MSSQL_SA_PASSWORD` de la sesión de PowerShell. Antes de construir la cadena, cargá en esa sesión la misma contraseña mediante entrada enmascarada.

```powershell
$env:MSSQL_SA_PASSWORD = Read-Host 'Misma contraseña de SQL Server configurada en .env' -MaskInput
```

Cargá la siguiente configuración en la sesión de PowerShell 7 que ejecuta las migraciones y la API. Usá `Read-Host -MaskInput` para los secretos. Si abrís otra terminal, cargá allí la configuración que corresponda. Conservá los valores secretos fuera de Git y evitá imprimir las variables, la configuración resuelta de Compose o las cadenas de conexión.

| Variable | Uso |
|---|---|
| `MSSQL_SA_PASSWORD` | Contraseña de SQL Server para el volumen de desarrollo. |
| `ConnectionStrings__DefaultConnection` | Conexión de la API y de EF a la base local `ConstiaLocal`. |
| `JwtSettings__SigningKey` | Clave exclusiva de la API local, con al menos 32 bytes UTF-8. |
| `JwtSettings__Issuer`, `JwtSettings__Audience`, `JwtSettings__ExpirationMinutes` | Emisor, audiencia y vigencia del JWT local. |
| `CONSTIA_STATE_HISTORY_CONNECTION` | La misma conexión, solo para el inicializador TEMP-005. |
| `Api__BaseUrl` | URL de la API para Web. En `Development` ya es `https://localhost:7260/`. |

```powershell
$sql = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$sql['Data Source'] = "127.0.0.1,$env:CONSTIA_SQL_PORT"
$sql['Initial Catalog'] = 'ConstiaLocal'
$sql['User ID'] = 'sa'
$sql['Password'] = $env:MSSQL_SA_PASSWORD
$sql['Encrypt'] = $true
$sql['TrustServerCertificate'] = $true
$env:ConnectionStrings__DefaultConnection = $sql.ConnectionString
$env:JwtSettings__SigningKey = Read-Host 'Clave JWT local' -MaskInput
$env:JwtSettings__Issuer = 'constia-local'
$env:JwtSettings__Audience = 'constia-web-local'
$env:JwtSettings__ExpirationMinutes = '60'
$env:Resend__Enabled = 'false'
$env:Bienvenida__CapturarEventos = 'false'
$env:Bienvenida__ProcesamientoHabilitado = 'false'
$env:Bienvenida__EnvioHabilitado = 'false'
$env:Bienvenida__MantenimientoHabilitado = 'false'
```

`TrustServerCertificate` se usa para el SQL Server de desarrollo en loopback. Para HTTPS entre el navegador, Web y la API, usá el certificado local de ASP.NET Core.

---

## 6. Estructura esperada

La solución tendrá una estructura similar a:

```text
CONSTIA/
├── AGENTS.md
├── README.md
├── MVP-SCOPE.md
├── SPECIFICATION.md
├── ARCHITECTURE.md
├── DEVELOPMENT.md
├── .gitignore
│
├── src/
│   ├── Constia.Web/
│   ├── Constia.API/
│   ├── Constia.Application/
│   ├── Constia.Domain/
│   └── Constia.Infrastructure/
│
└── tests/
    └── Constia.Tests/
```

---

## 7. Restaurar dependencias

Una vez creada la solución, desde la raíz del repositorio:

```powershell
dotnet restore
```

Este comando restaura los paquetes necesarios para los proyectos de la solución.

---

## 8. Compilar

Para compilar toda la solución:

```powershell
dotnet build
```

La solución deberá compilar correctamente antes de considerar completada una tarea de desarrollo.

---

## 9. Ejecutar pruebas

Para ejecutar los tests:

```powershell
dotnet test
```

Las pruebas deben ejecutarse después de cambios relevantes, especialmente cuando se modifican reglas de dominio, casos de uso o funcionalidades existentes.

---

## 10. Migraciones e inicialización local

Con SQL en estado `healthy` y la configuración local cargada, prepará el certificado HTTPS y aplicá las migraciones versionadas desde la raíz. Usá `dotnet-ef` 10.0.12, la versión de EF Core referenciada en el proyecto.

```powershell
dotnet dev-certs https --trust
dotnet ef database update --project src/Constia.Infrastructure --startup-project src/Constia.API -- --environment Development
```

En la base local nueva, antes de arrancar la API, elegí un corte UTC y completá TEMP-005. Si necesitás reintentar una captura, reutilizá el mismo corte. Verificá que `status` informe `snapshot captured: True`, `expected legacy habits: 0`, `snapshot rows: 0` y `completed: True`. La sección 16 documenta el procedimiento separado para bases con datos existentes.

```powershell
$env:CONSTIA_STATE_HISTORY_CONNECTION = $env:ConnectionStrings__DefaultConnection
$corteUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
dotnet run --project tools/Constia.StateHistoryBackfill -- capture --cutover-utc $corteUtc --apply
dotnet run --project tools/Constia.StateHistoryBackfill -- backfill --apply
dotnet run --project tools/Constia.StateHistoryBackfill -- status
```

---

## 11. Ejecución de la aplicación

Arrancá la API en la terminal con su configuración y esperá que escuche en `https://localhost:7260`.

```powershell
${env:Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics} = 'Information'
dotnet run --project src/Constia.API --launch-profile https
```

En otra terminal arrancá Web y abrí `https://localhost:7158`. El perfil de desarrollo de Web ya apunta a la API por HTTPS.

```powershell
dotnet run --project src/Constia.Web --launch-profile https
```

Comprobá la instalación con una cuenta sintética verificada, preparada únicamente en la base de ensayo. Para prepararla sin enviar correo, usá `VerificacionCorreoRepository.RegistrarAsync` y luego `ConfirmarAsync` con un token generado para la prueba, como en `EmailVerificationSqlServerTests`. La cuenta se guarda primero como pendiente y después pasa a verificada en la base desechable. Iniciá sesión desde Web y comprobá que Hoy cargue. El `200` de `GET /api/habitos/diarios/hoy` verifica que la API autenticó la solicitud y pudo consultar la base con el historial inicializado.

Desde Mis hábitos, abrí Nuevo hábito, completá nombre y categoría y avanzá al selector. Para la revisión con el catálogo original, comprobá que Web reciba `200` de `GET /api/habitos/categorias` y `GET /api/habitos/iconos` en la API real y que el selector muestre los 16 IDs originales en orden, de `libro` a `billete`. Terminá ahí el ensayo. Estos GET los realiza el servidor Web y se comprueban en los registros de estado y ruta de la API, sin registrar credenciales ni cuerpos.

Para detener el entorno local y conservar el volumen, usá `docker compose down` con el mismo nombre de proyecto. En un ensayo desechable, comprobá las etiquetas de proyecto del contenedor, la red y el volumen antes de ejecutar `docker compose down --volumes` con ese nombre.

### Compatibilidad del registro (UI-002)

El selector de zona horaria IANA de `/registro` requiere que el navegador implemente `Intl.supportedValuesOf("timeZone")`. Si la función no está disponible, el formulario muestra un aviso y deshabilita el alta. La zona horaria se elige explícitamente.

### Sesiones web (UI-003)

Ejecutá una sola instancia de `Constia.Web`. La validez de cada sesión se guarda en memoria del proceso hasta el vencimiento del ticket. Un reinicio o una actualización obliga a volver a iniciar sesión, al igual que el paso desde una versión anterior sin marca de sesión. Para desplegar réplicas se necesitan un almacén de sesiones y claves de ASP.NET Core Data Protection compartidos y una prueba de revocación entre instancias.

La regresión manual `tests/Constia.Web.Tests/Test-LogoutHttps.ps1` requiere PowerShell 7, Constia.Web en HTTPS local y una cuenta de prueba verificada. Pasá el origen, el correo y una contraseña como `SecureString`. Usá `-SkipCertificateCheck` solo con un certificado local de prueba.

---

## 12. Flujo de trabajo

El desarrollo de CONSTIA utiliza un flujo incremental.

### 1. Seleccionar una tarea

Elegir una tarjeta de Trello ubicada en `HOY`.

La tarea debe estar claramente definida y relacionada con un identificador, por ejemplo:

```text
HAB-006
```

### 2. Analizar

Antes de modificar el código:

* Leer la descripción de la tarea.
* Revisar `SPECIFICATION.md` cuando corresponda.
* Revisar `ARCHITECTURE.md` si afecta estructura o responsabilidades.
* Entender qué comportamiento se espera.

### 3. Implementar

Realizar únicamente los cambios necesarios para completar la tarea.

Evitar implementar funcionalidades futuras que todavía no formen parte del MVP.

### 4. Verificar

Ejecutar las comprobaciones correspondientes:

```powershell
dotnet build
dotnet test
```

o las pruebas específicas que correspondan.

### 5. Commit

Cada commit relacionado con una tarea deberá incluir su identificador de Trello.

Ejemplo:

```text
feat(HAB-006): create habit
```

### 6. Revisión

Comprobar que:

* El comportamiento cumple la especificación.
* Los tests pasan.
* No existen cambios innecesarios.
* No se introdujeron secretos.
* La arquitectura continúa respetándose.

### 7. Finalizar

Una vez verificada la tarea, mover la tarjeta correspondiente de Trello a `HECHO`.

---

## 13. Convenciones de Git

Los commits deben ser pequeños y representar cambios relacionados.

Formato recomendado:

```text
tipo(ID): descripción
```

Ejemplos:

```text
chore(SETUP-001): initialize Constia solution
feat(USR-001): add user domain
feat(HAB-006): create habit
test(STR-008): test 70 percent rule
docs(ARCH-001): update architecture
```

Tipos habituales:

* `feat` — nueva funcionalidad.
* `fix` — corrección de un error.
* `test` — pruebas.
* `refactor` — reorganización sin cambiar el comportamiento esperado.
* `docs` — documentación.
* `chore` — tareas de configuración o mantenimiento.

---

## 14. GitHub

El repositorio remoto de CONSTIA se almacenará en GitHub.

Flujo general:

```text
Trabajo local
     ↓
git add
     ↓
git commit
     ↓
git push
     ↓
GitHub
```

Los archivos generados temporalmente por herramientas de desarrollo no deberán versionarse.

El archivo `.gitignore` deberá evitar que archivos innecesarios o sensibles sean incluidos en Git.

---

## 15. Cambios de este documento

Este documento debe actualizarse cuando cambie alguno de los siguientes aspectos:

* Requisitos de desarrollo.
* Herramientas utilizadas.
* Forma de ejecutar el proyecto.
* Configuración de Docker.
* Configuración de SQL Server.
* Flujo de migraciones.
* Comandos necesarios para tests o compilación.
* Flujo de Git.
* Estructura necesaria para desarrollar CONSTIA.

La información aquí documentada debe representar el procedimiento real utilizado para trabajar en el proyecto.

## 16. TEMP-005: inicialización del historial de estados

TEMP-005 agrega períodos históricos de estado y deja el historial de hábitos existentes sin inicializar hasta que se capture y verifique un corte. La API responde `503` para operaciones dependientes del historial mientras falte la marca global de finalización. El helper no se ejecuta al iniciar la API.

### Orden de despliegue

1. Revisar la integridad de `Habito.Estado`, `Habito.StartDate` y `Usuario.TimeZoneId`. Los valores fuera del modelo o las zonas IANA inválidas requieren revisión operativa; no se corrigen automáticamente.
2. Detener todas las instancias de la aplicación anterior antes de aplicar la migración. Mantener el servicio en mantenimiento hasta que la captura inicial quede confirmada. No permitir que escritores antiguos sigan activos.
3. Aplicar las migraciones TEMP-005 en orden mediante el procedimiento de despliegue autorizado: `20261016191803_AddHabitoEstadoHistory` y luego `20261016194046_ConstrainPeriodoEstadoHabitoState`. Crean las tablas de historial/inicialización/snapshot y la restricción de estado; no rellenan ni reinterpretan datos existentes.
4. Elegir una sola fecha/hora UTC para el corte y conservar exactamente el mismo literal en el registro de despliegue. Configurar `CONSTIA_STATE_HISTORY_CONNECTION` fuera del repositorio.
5. Ejecutar primero sin `--apply` para revisar el comando, y luego capturar el corte:

   ```powershell
   dotnet run --project tools/Constia.StateHistoryBackfill -- capture --cutover-utc 2026-10-16T02:30:00Z --apply
   ```

   El helper persiste el UTC una sola vez. En una transacción serializable bloquea las tablas fuente `Usuario` y `Habito`, guarda el TimeZoneId, la fecha local derivada, FechaInicio y Estado por cada hábito preexistente, y confirma juntos el snapshot y `SnapshotCapturedAtUtc`. Si la transacción se interrumpe, revierte la captura completa; el UTC ya guardado permanece. Reintentar requiere el mismo timestamp. Una captura confirmada se reutiliza como autoridad del corte: el estado actual se compara mientras la inicialización siga incompleta, y el UTC/TimeZoneId actuales no se usan para reconstruir los valores congelados.
6. Una vez confirmado el snapshot, desplegar/permitir las operaciones independientes. Crear hábitos nuevos, editar datos/programación y cambiar zona horaria son seguros porque el corte de legado ya es inmutable. La desactivación y el registro de cumplimientos siguen bloqueados con `503` hasta completar el backfill.
7. Aplicar o reintentar el backfill:

   ```powershell
   dotnet run --project tools/Constia.StateHistoryBackfill -- backfill --apply
   ```

   Cada hábito se procesa en una unidad de trabajo que comparte `ConcurrencyVersion` con la raíz. Los períodos ya creados se validan y se omiten; una discrepancia detiene el proceso sin corregir datos automáticamente. Una interrupción deja `CompletedAtUtc` vacío, por lo que las escrituras dependientes continúan bloqueadas.
8. Verificar el estado antes de habilitar operaciones dependientes:

   ```powershell
   dotnet run --project tools/Constia.StateHistoryBackfill -- status
   ```

   `CompletedAtUtc` solo se escribe después de validar la cantidad exacta del snapshot, un período abierto por hábito y la coincidencia de la proyección `Habito.Estado`. La API levanta el bloqueo una vez que observa esa marca.

Las fechas anteriores al inicio verificable del período se consideran desconocidas: el registro de cumplimiento devuelve `422` con código `habit_status_history_unknown`; una inicialización global incompleta devuelve `503` con código `habit_status_history_not_initialized`. Los duplicados y conflictos de concurrencia conservan `409`. No se ejecutó ni autorizó aquí la migración o el backfill sobre bases persistentes.

### Operación de la ventana y evidencia

Antes de iniciar la ventana, registrar el entorno y base objetivo, la versión desplegada, la persona responsable, la ventana UTC elegida, el plan de retorno de servicio y la ubicación segura de la evidencia. Confirmar un respaldo recuperable y el procedimiento autorizado para restaurarlo. No copiar cadenas de conexión, contraseñas, tokens ni otros secretos a tickets, logs o esta documentación.

Poner la API en mantenimiento y detener todas las instancias y procesos que puedan escribir en `Usuario` o `Habito` antes de fijar el corte. Confirmar que no queden escritores antiguos activos. Las escrituras de desactivación y registro de cumplimientos deben permanecer bloqueadas hasta que el estado global indique inicialización completa. Mientras el corte está fijado pero el snapshot aún no se confirmó, mantener también detenidas las escrituras de perfil/zona horaria y de hábitos; después de confirmar el snapshot pueden habilitarse las operaciones independientes documentadas en el paso 6.

Antes de capturar o reanudar, recopilar por consultas de solo lectura y guardar bajo el control operativo del entorno:

- El corte UTC exacto persistido y su registro de cambio.
- `ExpectedLegacyHabitCount`, `SnapshotCapturedAtUtc` y `CompletedAtUtc`.
- El número de hábitos y snapshots, y los identificadores afectados.
- Para cada hábito discrepante, `Habito.Estado`, `Habito.StartDate`, `Usuario.TimeZoneId` y los valores correspondientes de `SnapshotEstadoHabito`.
- Los períodos de estado existentes, su vigencia, `ConcurrencyVersion` y los mensajes/horarios del helper.
- El resultado de `status` y los resultados de las comprobaciones de integridad y del respaldo.

Preservar la evidencia original antes de cualquier intervención. Las consultas de diagnóstico no deben modificar registros. No editar snapshots, estados, fechas ni períodos directamente para hacer que la validación pase.

### Validación de consistencia y clasificación de divergencias

El snapshot confirmado es la autoridad para el corte histórico. Verificar que el UTC sea el valor único persistido, que `SnapshotCapturedAtUtc` exista, que el número de filas coincida con el conteo congelado, que cada zona IANA congelada sea válida y que `FechaCorteLocal` vuelva a derivarse del UTC y de esa zona guardada. Comprobar que cada hábito del snapshot siga existiendo y que su `FechaInicio` actual coincida con la fecha congelada. Antes de completar el backfill, comprobar además que su estado actual coincida con el capturado: las escrituras de estado están bloqueadas durante ese período.

Clasificar las diferencias antes de decidir:

- **Operación válida de la aplicación:** un cambio actual de `TimeZoneId` después de la captura no reescribe el snapshot y no es una divergencia; una desactivación posterior a `CompletedAtUtc` puede hacer que `Habito.Estado` difiera del estado del corte, siempre que la proyección actual y el período abierto coincidan. Si `CompletedAtUtc` está presente y sus metadatos de finalización son coherentes, reejecutar `backfill --apply` es una operación sin cambios: no procesa ni compara el snapshot con el estado actual, no modifica tablas y comunica que la inicialización ya está completa. Esta repetición no sustituye una auditoría de integridad posterior; para inspeccionar el historial ya habilitado, usar `status` y las verificaciones operativas de solo lectura.
- **Posible modificación manual o corrupción:** durante una inicialización incompleta, un estado actual distinto del snapshot, una `FechaInicio` distinta, una zona IANA inválida, una fecha local que no derive del UTC y zona congelados, un conteo diferente o un período parcial incompatible deben detener el proceso. No asumir que se trata de una operación legítima, porque esas escrituras están bloqueadas.
- **Información imposible de reconstruir:** si no puede establecerse con evidencia qué estado, zona o fecha correspondían al corte, no inferirlos a partir del estado actual ni de una zona actual. Mantener mantenimiento para operaciones dependientes, conservar los datos y escalar la resolución con responsables de producto/operaciones y DBA.

Un cambio de zona horaria posterior a la captura es válido porque la zona del snapshot se conserva y se utiliza para derivar su fecha local; nunca sustituirla por la zona actual. `FechaInicio` es inmutable por la aplicación, por lo que una diferencia respecto del snapshot requiere investigación. El helper comprueba coherencia interna de zona/UTC/fecha y contra `FechaInicio`, pero no dispone de una firma o checksum histórico: una alteración manual coordinada de varios valores que siga siendo internamente coherente no puede probarse solo con estas columnas. Ante sospecha de esa alteración, no continuar sin evidencia externa confiable.

### Reanudación segura y criterios de continuación

Si el proceso se interrumpe antes de confirmar la captura, la transacción revierte el snapshot; el corte UTC que ya se persistió no cambia. Si la captura quedó confirmada, reutilizarla y no volver a leer los valores actuales para reconstruirla. Reanudar con exactamente el mismo literal UTC y ejecutar `status` antes y después de `backfill --apply`. Si `CompletedAtUtc` ya está presente y es coherente con el corte y la captura, el comando finaliza correctamente como no-op, sin validar el snapshot ni escribir datos; no usar esa repetición como auditoría posterior. El backfill incompleto confirma cada hábito de manera atómica; un hábito ya inicializado se omite solo cuando su único período abierto coincide con el snapshot. Una unidad incompleta se revierte al fallar y puede reintentarse con el mismo snapshot.

Solo continuar si se cumplen todos estos puntos: snapshot capturado y completo; metadatos y conteos consistentes; UTC, zonas y fechas locales coherentes; `FechaInicio` coincidente; estado actual igual al snapshot mientras `CompletedAtUtc` sea nulo; períodos parciales compatibles o inexistentes; y respaldo/evidencia disponibles. Al final, `status` debe confirmar todos los hábitos del snapshot con un único período abierto, todos los hábitos actuales con estado histórico y proyección `Habito.Estado` coincidente. Solo entonces puede persistirse `CompletedAtUtc` y habilitarse la desactivación y el registro de cumplimientos.

Si una validación falla y no existe evidencia suficiente para explicar o resolver la diferencia, detener el helper, mantener las operaciones dependientes bloqueadas (`503`) y conservar la ventana de mantenimiento para esas operaciones. No completar el marcador, no cambiar el corte, no borrar ni regenerar el snapshot y no aplicar correcciones automáticas. La continuación requiere una resolución operativa documentada y basada en evidencia; si esa evidencia no existe, mantener el bloqueo y definir con los responsables un procedimiento explícito de recuperación antes de tocar los datos.

La API usa `CompletedAtUtc` como barrera global; no vuelve a auditar toda la consistencia del historial en cada solicitud. Si una verificación operativa detecta una inconsistencia después de que el marcador ya fue completado, poner inmediatamente las operaciones dependientes en mantenimiento desde la plataforma de despliegue y mantenerlas bloqueadas hasta resolverla con evidencia. No asumir que el helper deshabilita por sí solo esas rutas después de completar el corte.

### Seguridad de rollback

Al revertir ambas migraciones TEMP-005 en orden inverso, el `Down()` de `ConstrainPeriodoEstadoHabitoState` retira su restricción y el `Down()` de `AddHabitoEstadoHistory` elimina `PeriodoEstadoHabito`, `SnapshotEstadoHabito` e `InicializacionEstadoHabito`. Después de iniciar la captura o generar períodos históricos, completar esa reversión elimina información que ya no puede reconstruirse necesariamente desde el estado actual. No usar rollback en ese punto sin un procedimiento específico, aprobado y probado, que preserve y permita recuperar todos esos datos. La reversión de estas migraciones no es un mecanismo de reparación del backfill; esta tarea no incorpora un mecanismo alternativo de rollback.

## UI-005 — Mis hábitos

`/habitos` requiere la sesión web vigente. Consume el listado completo de activos; la expansión usa sus datos y no genera una consulta de detalle por tarjeta. Crear está disponible mediante UI-006/UI-008 en `/habitos/nuevo`; editar y consultar inactivos permanecen no disponibles hasta UI-007 y UI-014.

La desactivación usa una confirmación explícita, POST del mismo origen con antifalsificación y DELETE autenticado solo desde Web. Un 204 confirma; si se pierde la respuesta, el detalle solo acredita el estado final. Los resultados inciertos se conservan y se consultan sin reenviar automáticamente. La coordinación comparte el alcance de una instancia de las sesiones: reiniciar invalida las sesiones; antes de desplegar réplicas hacen falta sesiones, claves y coordinación compartidas. No hay reactivación en V1.

## UI-006 y UI-008 — Crear hábitos

`/habitos/nuevo` requiere sesión y abre un borrador sin selecciones: nombre/categoría, ícono, lunes–domingo y resumen. No se envían descripción, FechaInicio ni propietario. La confirmación y fecha provienen de un 201 válido. Los catálogos se consultan desde el servidor; el JWT permanece en el ticket protegido y nunca llega al formulario. Los POST Web usan antifalsificación.

La coordinación retiene como máximo ocho borradores por sesión, cada uno por una hora o hasta el vencimiento de la sesión, lo que ocurra primero. IDs vencidos no se reconstruyen. Confirmados e inciertos bloquean un segundo POST; las revisiones evitan que un formulario obsoleto sobrescriba un paso posterior. No se infiere autoría por coincidencia en el listado ni se reenvía automáticamente un resultado incierto.

El alcance es una sola instancia, como las sesiones Web. Un reinicio invalida sesiones y pierde esta coordinación; la API no tiene idempotencia durable de creación. Revisar Mis hábitos antes de iniciar otro borrador tras una incertidumbre o un reinicio. Antes de réplicas o reintentos durables hacen falta almacenamiento/coordinación compartidos y un contrato de idempotencia API; esta entrega no los incorpora.

Recursos: dieciséis PNG existentes; check original del master Control · Realizado · PNG de Penpot (media 129977ce-683b-806a-8008-c92f778e5bc6), y confeti oficial aprobado. Sin ilustraciones sustitutas.

## UI-010 — Calendario de consulta

`/calendario` requiere sesión y consume desde el servidor tres lecturas: `/api/usuarios/mi-fecha-local`, STAT-005 para el rango visible de hasta 42 días y COMP-007 para la fecha seleccionada. La fecha civil usa la zona IANA persistida vigente, independientemente de la inicialización del historial; nunca el reloj del navegador. Navegar o recargar vuelve a consultar esta autoridad temporal.

Adaptación autorizada de PEN-002: conteos y detalle de solo lectura, sin puntos ni colores por categoría y sin acciones de registro. No se infieren categorías históricas desde metadatos actuales. Se conservan grilla lunes–domingo, Hoy/selección, estados diferenciados y neutralidad; las fechas futuras son planificación, no incumplimiento. El porcentaje del detalle se muestra solo para fechas no futuras y con oportunidades conocidas. La fecha elegida tiene contorno y texto «Elegido», además de color.

Un 422 de STAT-005 impide acreditar el resumen de todo el rango: no identifica celdas desconocidas. El detalle seleccionado usa su propia respuesta y puede estar disponible aun con error del rango. Inversamente, un error del detalle no invalida por sí solo los conteos acreditados. 503 se distingue de historia desconocida; vacío requiere 200 válido. Sin POST de cumplimiento, sin escritura, sin idempotencia nueva ni cambio de los contratos COMP-007/STAT-005. El dock activa Calendario y conserva Hoy/Cuenta.
