# Categorías V1, íconos y FechaInicio automática — plan reconciliado en AUD-BASE

Estado: preparación documental para revisión, 19/10/2026. No autoriza implementar funcionalidades, aplicar migraciones, crear commits ni iniciar PEN-007 o UI-006.

## Fuentes y alcance

Las decisiones de producto están en [MVP-SCOPE.md](../../../MVP-SCOPE.md), secciones 3.2 y 3.11, y [SPECIFICATION.md](../../../SPECIFICATION.md), secciones 2.3 y 2.5. Las responsabilidades técnicas se delimitan en [ARCHITECTURE.md](../../../ARCHITECTURE.md), sección 8. Los nombres exactos y el orden de las diez categorías se conservan en esas fuentes, sin crear otro catálogo divergente aquí.

Este documento reconcilia el plan local anterior con main remoto actualizado y las aprobaciones posteriores de iconografía. Las decisiones de producto aprobadas se distinguen de recomendaciones técnicas todavía pendientes de revisión. No se modifica la skill ni Penpot.

## Base Git de AUD-BASE

- Remoto origin: https://github.com/ivandinicola/CONSTIA.git.
- Main remoto comprobado mediante fetch limitado y ls-remote: 30058bbcd90e8f6a3733784f6826470eb67989d4.
- COMP-005: 0e7470c690d149c9c40382560faf7271341dd530, integrado por ascendencia.
- TEMP-005: ff1862836981182bc8d56e9016af44489cc93cd2, integrado por ascendencia; main contiene el merge del PR #5.
- Checkout original preservado: C:/Users/FIO PC/Desktop/Constia, feature/TEMP-004, 89c0a3933b978de6782d1f80a0e2da244588379b.
- Worktree AUD-BASE: C:/Users/FIO PC/.codex/worktrees/aud-base/Constia, rama chore/AUD-BASE. Estado inicial limpio y HEAD igual a origin/main.
- La aplicación de cambios locales se realizó por hunks documentales comprobados con git apply --check, no por sustitución completa de los documentos de main.

No se hicieron merges ni cherry-picks. Las referencias remotas no acreditan despliegue ni migraciones aplicadas. No se consulta SQL Server.

## Decisiones aprobadas que deberá cumplir la implementación futura

- Diez categorías globales y predefinidas; categorías personalizadas fuera de V1.
- Dieciséis íconos oficiales, independientes de la categoría.
- Todo hábito nuevo requiere una categoría oficial válida y un ícono oficial válido.
- Los hábitos históricos pueden conservar CategoriaId e IconoId nulos. No asignar valores arbitrarios ni inferirlos desde nombres, textos o imágenes.
- Omitir la categoría al editar conserva su valor; V1 no permite eliminar una categoría asignada.
- Editar un hábito histórico sin categoría no obliga a clasificarlo.
- El usuario no ingresa FechaInicio. Backend es su autoridad: instante UTC mediante TimeProvider, zona IANA persistida del propietario y fecha civil local.
- El contrato nuevo rechazará expresamente solicitudes que incluyan FechaInicio. No basta con ignorar propiedades JSON desconocidas.
- Calcular una sola fecha de inicio por creación y reutilizarla en hábito, programación inicial y período inicial Activo.
- No reescribir fechas históricas, períodos de programación/estado ni cumplimientos; sin reactivación en V1.

## Pendientes que corresponden a API-CAT-01

- Semántica de null explícito frente a categoría ausente. Está aprobado conservar por omisión y prohibir la eliminación; no se declara null explícito equivalente a omisión sin resolverlo.
- Edición de ícono: presencia, null, sustitución y tratamiento de históricos. La obligatoriedad de ícono en nuevas altas ya está aprobada.
- Forma y valores definitivos de las claves de ícono y de los GUID fijos de categorías.
- Compatibilidad, inventario de consumidores, despliegue coordinado o versionado y códigos/cuerpos de error.
- Detalle del rechazo de FechaInicio en entrada, conservando StartDate y CreatedAt en las respuestas actuales.

Estos pendientes no impiden AUD-BASE, porque no se implementan contratos durante esta tarea.

## Hechos de fuentes en la base integrada

| Área | Evidencia | Consecuencia |
| --- | --- | --- |
| Alta | src/Constia.API/Contracts/CrearHabitoHttpRequest.cs y src/Constia.Application/Habitos/CrearHabitoRequest.cs todavía reciben FechaInicio | La nueva decisión no está implementada |
| CrearHabito | src/Constia.Application/Habitos/CrearHabito.cs carga propietario, exige SnapshotCapturadoAsync y usa la fecha del request | La adaptación debe conservar la protección de TEMP-005 |
| Tiempo | src/Constia.Application/Temporal/FechaLocalUsuario.cs utiliza TimeProvider.GetUtcNow, TimeZoneInfo.ConvertTime y DateOnly; ZonaHorariaIana valida la zona | Reutilizar el servicio existente, sin zona del servidor ni fallback silencioso |
| Dominio | src/Constia.Domain/Habito.cs crea programación y período inicial Activo desde FechaInicio; todavía no contiene categoría ni ícono | Agregar metadatos sin cambiar las inicializaciones históricas |
| Estado | src/Constia.Domain/PeriodoEstadoHabito.cs y Habito.ObtenerEstadoPara representan intervalos semiabiertos; ausencia de período devuelve estado desconocido | No confundir desconocido con Inactivo ni con incumplimiento |
| Inicialización | src/Constia.Infrastructure/Habitos/EstadoHabitoBackfillService.cs conserva snapshot de estado, fecha, zona y corte; EstadoHabitoReadinessRepository distingue snapshot capturado de historial completo | No repetir ni alterar el backfill al incorporar categorías |
| Cumplimientos | src/Constia.Application/Cumplimientos/RegistrarCumplimiento.cs permite hoy o ayer locales, exige estado actual e histórico Activo y programación válida | Preservar la ventana de main, sin recuperar la regla anterior de cualquier fecha pasada |
| Unicidad | src/Constia.Infrastructure/Configurations/CumplimientoConfiguration.cs, migración AddCumplimientoUniqueHabitDate y pruebas SQL | Mantener garantía por hábito/fecha ante concurrencia |
| Edición | src/Constia.Application/Habitos/EditarHabito.cs y Habito.ActualizarConfiguracionVersionada | Metadatos solos no crean versiones de programación; conservar consolidación del mismo día |
| Repositorio | src/Constia.Infrastructure/Habitos/HabitoRepository.cs aplica propiedad y ConcurrencyVersion | Conservar guardado atómico y conflicto concurrente; no paralelizar un DbContext |
| Lecturas | HabitoCreado, HabitoListado y HabitoResponse todavía no incluyen categoría ni ícono | Adaptar todas las proyecciones sin perder históricos null |
| Frontend | src/Constia.Web/Components/Pages conserva páginas de plantilla | La aprobación de PEN-006 no prueba un formulario funcional |

No se ejecutaron pruebas del producto. La existencia de código, migraciones y tests no implica resultados satisfactorios de ejecución ni estado de base real.

## Recomendaciones técnicas — no implementadas

Catálogo CategoriaHabito persistido y global de solo lectura para usuarios: Guid fijo por categoría, Nombre y Orden. Las claves deberán revisarse una vez y conservarse; no usar nombres como FK ni GUID aleatorios al arrancar. Precarga mediante migración revisada, sin CRUD de categorías.

Habito.CategoriaId nullable para históricos, FK sin default y eliminación restringida. Nuevas altas con validación estricta; no mantener servidores o rutas antiguas que eludan esa obligación. Lecturas sin inner join que descarte históricos. No se propone una tabla de versiones de categoría ni métricas históricas inferidas.

IconoId con clave estable independiente, catálogo cerrado de dieciséis recursos y mapeo a PNG oficiales existentes. Conservar null histórico; no asignar Libro o Billete por defecto en backend. El frontend deberá enviar la elección explícita, incluso cuando Libro sea la selección inicial visual.

Application coordina la fecha local una sola vez tras cargar al propietario, preservando readiness. Domain recibe la fecha explícita, y EF conserva StartDate obligatoria. Se puede evaluar alinear también FechaCreacion con el mismo instante controlable; no modificar registros anteriores.

La estrategia de transición recomendada es aditiva: catálogo y columnas nullable, lecturas compatibles, despliegue coordinado de escritores estrictos y clasificación histórica solo explícita. No exige convertir luego las columnas a NOT NULL ni realizar backfill arbitrario.

## Integridad temporal que no debe alterarse

1. TEMP-003: una programación aplicable por fecha desde FechaInicio, intervalos [VigenteDesde, VigenteHasta), consolidación de ediciones del mismo día y edición previa al inicio para históricos con fechas futuras.
2. TEMP-004: ConcurrencyVersion de edición y conservación de todos los cambios ante un conflicto fallido.
3. TEMP-005: estado inicial Activo desde FechaInicio, desactivación por fecha local, sin reactivación y sin inventar historia previa al corte.
4. Snapshot: antes de capturarlo se bloquean altas y cambios de zona; hasta completar el historial se bloquean desactivaciones y nuevos cumplimientos dependientes.
5. Historia desconocida: respuesta 422 habit_status_history_unknown; inicialización pendiente: 503 habit_status_history_not_initialized.
6. COMP-004/005: cumplimientos solo para hoy o ayer locales, fecha programada y no anterior al inicio; unicidad por hábito/fecha; no admitir nuevos cumplimientos de hábitos actualmente inactivos.
7. Los cumplimientos ya registrados permanecen, aunque una edición del mismo día cambie las oportunidades. Cambiar zona, categoría o ícono no reescribe las fechas históricas.

Ejemplo de creación futura: domingo 18/10/2026 con lunes a viernes seleccionados conserva inicio y estado Activo desde el domingo, sin oportunidad dominical; primera oportunidad el lunes 19. Un instante 2026-10-19T02:30:00Z corresponde al 18 en Buenos Aires y al 19 en Tokio. Estos son criterios de prueba, no resultados ejecutados.

## Plan de implementación posterior — requiere autorización

| Tarea | Objetivo y archivos probables | Dependencias y verificación |
| --- | --- | --- |
| API-CAT-01 | Especificar compatibilidad, presencia/null, edición de ícono y rechazo de FechaInicio; contratos y documentación, sin migración inicial | AUD-BASE revisada; matriz de operaciones/clientes y ejemplos acordados |
| CAT-01 | Nueva entidad Domain/CategoriaHabito.cs, catálogo de lectura en Application/Infrastructure y configuración EF | API-CAT-01; diez nombres exactos, claves fijas y orden estable |
| CAT-02 | Migración aditiva sobre main integrado, FK nullable sin default y snapshot EF revisado | CAT-01; SQL Server aislado: datos históricos intactos, FK y precarga verificadas |
| HAB-CAT-01 | Habito, CrearHabito/EditarHabito, requests y resultados con categoría | CAT-02; alta obligatoria, edición por propietario y sin cambios temporales por metadatos |
| HAB-DATE-01 | CrearHabito y contratos de alta: fecha local automática, rechazo explícito de entrada y readiness conservado | API-CAT-01 y TEMP-005; reloj controlado, medianoche, DST, día no programado y zona inválida |
| HAB-ICON-01 | Persistencia/validación/proyecciones de ícono independiente y transición null | API-CAT-01; dieciséis recursos, edición acordada, sin inferencias ni cambios de categoría |
| API-CAT-02 | Controller, DTOs HTTP, catálogo de lectura y respuestas coherentes | Casos de uso; altas, listas activas/inactivas, detalles, edición, propiedad y errores |
| QA-CAT-01 | Pruebas unitarias, HTTP y SQL; ampliación de fixtures/CI existentes | Persistencia/API; migración conserva fechas, períodos, snapshot, estados y cumplimientos; concurrencia real |
| UI-006 futura | Flujo aprobado contra API estable, sin fecha editable | Backend verificado y autorización propia; diez categorías y dieciséis íconos independientes |
| UI-007 / PEN-007 | Edición futura conforme al diseño y contrato correspondientes | Fuera de AUD-BASE; no iniciadas |

Riesgos principales: base antigua de migración, clientes o instancias que sigan creando null, confusión entre ausencia y null, cálculo doble cerca de medianoche, pérdida del gating de TEMP-005 y regresiones de la ventana hoy/ayer. La aceptación de cada tarea exige pruebas de su comportamiento y preservación, sin ampliar el alcance de diseño ni alterar datos reales.

## Diseño vigente y antecedentes

La aprobación inicial de PEN-006 del 18/10/2026 mostraba cinco categorías y quince íconos. Es un antecedente histórico conservado, superado puntualmente por las revisiones aprobadas del 19/10: Paso 1 con diez categorías sin íconos, filas de 334 × 54 px, texto con margen interno de 16 px, radios derechos y Aprendizaje y crecimiento seleccionada. Vista inicial con cinco opciones; el resto requerirá desplazamiento funcional. Continuar permanece fijo fuera del listado.

Paso 2 aprobado con dieciséis íconos en cuatro filas de cuatro; Libro seleccionado y Billete en fila 4/columna 4. Pasos 3–5 preservan selector semanal, resumen con Aprendizaje y crecimiento y Confirmación con check/confeti aprobados. Billete ya está aprobado: no queda una definición visual pendiente de Finanzas personales.

La aprobación es visual y estática. Selección, scroll, navegación, persistencia, accesibilidad e integración necesitan implementación y pruebas futuras. No modificar Penpot, recursos, masters o skills por esta reconciliación.

## Relación con Trello y límites de ejecución

HAB-006 y HAB-009 conservan sus cierres históricos; no acreditan los nuevos incrementos. API-004 corresponde a compatibilidad y respuestas, UI-006 al formulario futuro y UI-007 a edición futura. AUD-BASE no crea, mueve ni comenta tarjetas. Los IDs propuestos de tareas no son tarjetas creadas.

No ejecutar instalaciones, backfill, dotnet ef database update, pruebas sobre bases persistentes, commits, push o PR. Este plan no es una autorización de implementación.

## Antecedente documental: verificación de la formalización previa del 19/10/2026

El apartado siguiente se conserva como trazabilidad de la entrega original en feature/TEMP-004. Sus lecturas de Trello, conteos y comprobaciones corresponden a aquella entrega; no fueron reejecutadas ni publicadas en AUD-BASE. No describe la base Git ni la revisión documental actuales.

- Diff revisado: solo MVP-SCOPE.md, SPECIFICATION.md, ARCHITECTURE.md y README.md entre los archivos versionados; este análisis es el único documento nuevo de esta tarea. Se conservan los archivos no versionados que ya existían.
- Diez nombres comparados, en el mismo orden y con coincidencia exacta entre alcance y especificación.
- Se comprobaron 19 bloques anteriores sobre temporalidad, estados, cumplimientos y métricas: sus reglas se conservan. Los únicos textos anteriores sustituidos son la exclusión genérica de Categorías, el ingreso de fecha en creación y la mención imprecisa de activar/desactivar en README.
- Enlaces locales comprobados y ejemplos de calendario/zona verificados, incluido el domingo 18 y lunes 19 de octubre y el mismo instante UTC convertido a Buenos Aires/Tokio. Esto verifica documentación; no certifica el caso de uso futuro.
- Se contrastaron 77 archivos protegidos de assets y skill contra la evidencia de aprobación previa, sin cambios. No se modificaron código, pruebas, migraciones, diseños, base de datos ni herramientas.
- Git diff --check sin errores de espacios. No se compiló ni ejecutaron pruebas .NET: los cambios son documentales y el código conserva el contrato anterior, cuya adaptación queda propuesta.
- Lectura posterior de Trello: comentario exacto presente en cada tarjeta, descripciones y comentarios previos conservados, listas y estados intactos. El tablero conserva sus 98 tarjetas, sin creaciones ni cambios de nombre, descripción o lista.

| Tarjeta actualizada con comentario | Lista conservada | Verificación |
| --- | --- | --- |
| [API-004 — Respuestas HTTP consistentes — Revisar códigos y contratos de respuesta.](https://trello.com/c/uWILWOkN/51-api-004-respuestas-http-consistentes-revisar-c%C3%B3digos-y-contratos-de-respuesta) | BACKLOG | Comentario del 19/10/2026 verificado; sin cambio de lista |
| [UI-006 — Crear hábito — Crear formulario para crear hábitos.](https://trello.com/c/sGkAC1rk/58-ui-006-crear-h%C3%A1bito-crear-formulario-para-crear-h%C3%A1bitos) | BACKLOG | Comentario del 19/10/2026 verificado; sin cambio de lista |
| [UI-007 — Editar hábito — Crear formulario para modificar hábitos.](https://trello.com/c/AYG762ts/59-ui-007-editar-h%C3%A1bito-crear-formulario-para-modificar-h%C3%A1bitos) | BACKLOG | Comentario del 19/10/2026 verificado; sin cambio de lista |
| [PEN-007 — Editar hábito — Diseño en Penpot](https://trello.com/c/WJZVzcGW/94-pen-007-editar-h%C3%A1bito-dise%C3%B1o-en-penpot) | BACKLOG | Comentario del 19/10/2026 verificado; sin cambio de lista |
| [PEN-006 — Crear hábito y selector semanal — Diseño en Penpot](https://trello.com/c/smMp9TpX/93-pen-006-crear-h%C3%A1bito-y-selector-semanal-dise%C3%B1o-en-penpot) | HECHO | Comentario del 19/10/2026 verificado; sin cambio de lista |
| [HAB-006 — Crear hábito](https://trello.com/c/Mxb56sqt/23-hab-006-crear-h%C3%A1bito) | HECHO | Comentario del 19/10/2026 verificado; sin cambio de lista |
| [HAB-009 — Editar hábito — Permitir modificar los datos configurables del hábito.](https://trello.com/c/uCL4MG97/26-hab-009-editar-h%C3%A1bito-permitir-modificar-los-datos-configurables-del-h%C3%A1bito) | HECHO | Comentario del 19/10/2026 verificado; sin cambio de lista |

No se considera implementada ninguna decisión por estos comentarios. No se inició PEN-007 y la entrega se detiene para revisión documental.

## Verificación de AUD-BASE

La verificación final de AUD-BASE se documenta después de revisar el diff y contrastar los hashes del checkout original. No se certifican compilación, pruebas .NET/SQL, migraciones aplicadas ni despliegue.


Resultados de comprobación de AUD-BASE (19/10/2026):

- Checkout original: mismo HEAD 89c0a3933b978de6782d1f80a0e2da244588379b, misma rama feature/TEMP-004 y mismo status. Los 419 archivos del inventario versionado/sin seguimiento conservan longitud y SHA-256; cero archivos cambiados, agregados o perdidos frente a ese inventario.
- Inventario sin seguimiento preservado: .playwright-mcp (3), Corregir-UserSecrets-Diagnostico-Corregido.ps1 (1), assets/constia-iconos-v1.zip (1), assets/constia-iconos-v1 (71), assets/design-exports (207), docs/superpowers (2), temp-004-add-habit-concurrency-version.zip (1): 286 archivos. No se copiaron recursos gráficos ni configuraciones personales al worktree nuevo.
- Al iniciar no se observaron otros subagentes activos, procesos Git/dotnet detectados ni archivos lock en .git. La comparación final de hashes no detectó modificaciones concurrentes en el inventario original.
- Worktree nuevo: chore/AUD-BASE, HEAD igual a origin/main 30058bbcd90e8f6a3733784f6826470eb67989d4, cero commits por delante, índice sin cambios preparados.
- Únicos cambios: ARCHITECTURE.md, MVP-SCOPE.md, README.md, SPECIFICATION.md y este plan nuevo respecto de main. Sin cambios en src, tests, tools, .github, AGENTS.md, DEVELOPMENT.md, solución ni .gitignore.
- git diff --check sin errores. Las nueve migraciones existentes se conservan; no hay migraciones nuevas. COMP-005 y TEMP-005 son ancestros de main.
- Comparación textual: bloques de programación, cumplimientos, estados y métricas de main conservados; en la sección de estados solo se agregó una reiteración de la prohibición de reactivación ya vigente.
- Diez nombres y orden coincidentes entre MVP-SCOPE y SPECIFICATION. Referencias actuales a dieciséis íconos y Billete aprobado; cinco categorías/quince íconos quedan solo como antecedentes expresos.
- No se ejecutaron compilación, pruebas .NET/SQL, instalación, acceso a bases reales ni backfill. No se crearon commits, PRs ni push. No se consultaron ni modificaron Trello/Penpot en AUD-BASE y no se modificaron skills.

Próximo paso: revisión de AUD-BASE por el usuario; luego API-CAT-01 para cerrar los pendientes contractuales. No comenzar implementación con esta entrega.
