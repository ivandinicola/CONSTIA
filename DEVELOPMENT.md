# CONSTIA — Development Guide

## 1. Propósito

Este documento explica cómo preparar el entorno de desarrollo de CONSTIA, ejecutar sus servicios y trabajar con el proyecto localmente.

Debe mantenerse actualizado a medida que evolucione el proyecto.

---

## 2. Requisitos

Para desarrollar CONSTIA se necesitan:

* Windows.
* .NET SDK 10.
* Git.
* Docker Desktop.
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

### Docker

```powershell
docker --version
docker run hello-world
```

Docker Desktop debe estar ejecutándose.

---

## 4. SQL Server de desarrollo

CONSTIA utiliza SQL Server 2022 para desarrollo local.

La base de datos se ejecuta mediante Docker.

### Contenedor

Nombre previsto:

```text
habittracker-sqlserver
```

### Puerto

```text
1433
```

### Imagen

```text
mcr.microsoft.com/mssql/server:2022-latest
```

### Volumen

```text
habittracker-sql-data
```

El volumen permite conservar los datos aunque el contenedor sea detenido o eliminado.

### Verificar el contenedor

```powershell
docker ps
```

El contenedor debe aparecer como activo.

### Verificar los logs

```powershell
docker logs habittracker-sqlserver
```

SQL Server debe indicar que está listo para aceptar conexiones.

---

## 5. Credenciales y secretos

Las credenciales de desarrollo no deben almacenarse directamente en el repositorio.

No deben incluirse en:

* Código fuente.
* `README.md`.
* `DEVELOPMENT.md`.
* `appsettings.json` versionado.
* Commits.
* Issues.
* Documentación pública.

Los secretos deberán gestionarse mediante mecanismos apropiados de configuración de desarrollo.

La contraseña utilizada actualmente para el contenedor local de SQL Server existe únicamente como configuración de desarrollo y no forma parte de este repositorio.

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

## 10. Entity Framework Core

Las migraciones de base de datos serán administradas mediante Entity Framework Core.

Comandos habituales:

```powershell
dotnet ef migrations add <NombreMigracion>
```

y:

```powershell
dotnet ef database update
```

Las migraciones deberán representar cambios intencionales en el modelo y mantenerse versionadas junto con el código.

No se deberán modificar manualmente las tablas de la base de datos para reemplazar el flujo normal de migraciones durante el desarrollo.

---

## 11. Ejecución de la aplicación

Los comandos concretos para ejecutar `Constia.API` y `Constia.Web` se documentarán una vez creados los proyectos.

La ejecución local deberá permitir levantar:

```text
Constia.Web
     │
     │ HTTP
     ▼
Constia.API
     │
     ▼
SQL Server
```

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
