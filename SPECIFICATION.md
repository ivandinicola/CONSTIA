# CONSTIA — Functional Specification V1

## 1. Usuarios

### 1.1. Información

Cada usuario estará compuesto por:

* Identificador único.
* Nombre.
* Email.
* Contraseña.
* Identificador de zona horaria IANA.

### 1.2. Registro

El usuario podrá registrarse proporcionando:

* Nombre.
* Email.
* Contraseña.
* Una zona horaria seleccionada explícitamente mediante un identificador IANA válido.

El email deberá ser único.

Los usuarios existentes recibirán provisionalmente la zona horaria `Etc/UTC`, sin inferir su ubicación. El usuario podrá modificar posteriormente su zona horaria. Ese cambio no modificará las fechas civiles de cumplimientos ni las vigencias históricas ya almacenadas.

La fecha actual del usuario se determinará a partir del instante UTC convertido a su zona horaria. El cálculo deberá poder probarse mediante `TimeProvider`.

### 1.3. Autenticación

El usuario podrá iniciar sesión mediante sus credenciales.

Las contraseñas no deberán almacenarse en texto plano. Deberá almacenarse un hash seguro.

Un usuario autenticado solamente podrá acceder y modificar sus propios recursos.

---

# 2. Hábitos

## 2.1. Concepto

Un hábito representa una actividad que el usuario desea realizar de manera recurrente.

## 2.2. Información

Un hábito tendrá conceptualmente:

* Identificador.
* Nombre.
* Descripción.
* Fecha de creación.
* Fecha de inicio.
* Estado.
* Una categoría oficial, identificada de forma estable e independiente del ícono.
* Un ícono oficial, identificado de forma estable e independiente de la categoría.
* Usuario propietario.
* Días de la semana programados.

## 2.3. Creación

Para crear un hábito se deberá proporcionar como mínimo:

* Nombre.
* Una categoría válida del catálogo oficial, mediante su identificador estable.
* Un ícono válido del catálogo oficial de dieciséis recursos, mediante un identificador estable.
* Al menos un día programado.

La descripción será opcional.

Todo hábito nuevo comenzará en estado **Activo**.

### FechaInicio automática para nuevas creaciones · decisión del 19/10/2026

El usuario no ingresará manualmente `FechaInicio`. El backend obtendrá el instante UTC actual mediante `TimeProvider`, lo convertirá a la zona horaria IANA persistida del usuario autenticado y determinará la fecha civil local correspondiente. Esa fecha será `FechaInicio` y seguirá persistida en el modelo. No se utilizará la zona horaria del servidor ni una fecha arbitraria enviada por el cliente. El nuevo contrato rechazará expresamente cualquier solicitud de creación que incluya `FechaInicio`; retirar la propiedad de un DTO sin validar su presencia no basta para satisfacer esta regla. Los detalles de compatibilidad y respuesta se especificarán en API-CAT-01.

El cálculo se realizará una vez por creación y su resultado se reutilizará como inicio del hábito, de su programación inicial y de su período inicial de estado Activo. El estado inicial será Activo desde `FechaInicio`. La operación deberá poder verificarse con un `TimeProvider` controlado. No se admite sustituir silenciosamente una zona persistida inválida por la del servidor.

`FechaInicio` no tiene que coincidir con un día programado. Ejemplo: si la fecha civil local al crear es domingo 18/10/2026 y se selecciona lunes a viernes, `FechaInicio` será 18/10/2026, el estado inicial será Activo y la primera oportunidad programada será lunes 19/10/2026. El domingo no programado y los días anteriores a `FechaInicio` no generan oportunidades ni incumplimientos.

Esta regla se aplica a nuevas creaciones una vez adaptado el contrato. No recalcula ni reescribe fechas de inicio, versiones o cumplimientos de hábitos históricos. Cambiar posteriormente la zona horaria no reinterpreta las fechas civiles ya almacenadas. La fecha de creación como instante y la fecha civil de inicio siguen siendo datos distintos.

## 2.4. Días programados

Cada hábito deberá tener uno o más días de la semana seleccionados.

Los días posibles son:

* Lunes.
* Martes.
* Miércoles.
* Jueves.
* Viernes.
* Sábado.
* Domingo.

Un hábito solamente corresponderá a un día cuando ese día esté incluido en su configuración.

### Versiones históricas

La programación semanal de cada hábito se conservará mediante versiones. Cada versión tendrá `VigenteDesde: DateOnly` y `VigenteHasta: DateOnly?`, y representará el intervalo semiabierto `[VigenteDesde, VigenteHasta)`. Para cada fecha desde `FechaInicio` deberá existir una única versión aplicable, sin períodos solapados ni fechas ambiguas.

Los cambios de días programados tendrán vigencia desde la fecha local del usuario en que se realicen. No se permitirá editar retroactivamente versiones históricas en V1. Si se realizan varias ediciones durante un mismo día local, se consolidarán en una única versión: prevalecerá la última programación para todo ese día y los estados intermedios intradía no se conservarán.

Los cambios de nombre o descripción no crearán versiones de programación. Si se modifica la programación antes de `FechaInicio`, se actualizará la versión inicial sin adelantar su vigencia.

La programación actualmente almacenada se convertirá en la versión inicial, vigente desde `FechaInicio` y con `VigenteHasta` nulo hasta que exista una versión posterior. No se reconstruirán cambios históricos que nunca fueron registrados.

---

## 2.5. Categorías oficiales · decisión del 19/10/2026

V1 incluye un catálogo inicial predefinido de diez categorías con estos nombres funcionales exactos:

1. Salud y movimiento.
2. Alimentación e hidratación.
3. Descanso y sueño.
4. Bienestar emocional.
5. Aprendizaje y crecimiento.
6. Productividad y organización.
7. Relaciones y vínculos.
8. Finanzas personales.
9. Creatividad y ocio.
10. Hogar y vida cotidiana.

Los ejemplos y los íconos de referencia están en `MVP-SCOPE.md`, sección 3.11. No se agregan categorías ni se renombran estas mediante esta formalización.

* Cada hábito nuevo debe tener exactamente una categoría válida del catálogo una vez habilitada la funcionalidad. Se validarán presencia e identidad; un nombre libre o un ID inexistente no reemplazan una categoría válida.
* Los identificadores serán estables entre despliegues y no dependerán del nombre visible. La estrategia técnica de claves y precarga se propone en el análisis de impacto; todavía no se crearon seeds.
* Se podrá consultar y editar la categoría dentro de las operaciones de hábitos, con las mismas garantías de propiedad, integridad y concurrencia que la edición general. Omitirla conserva el valor actual y V1 no permite eliminar una categoría ya asignada. Esta decisión no habilita reactivación ni cambios en `FechaInicio`.
* No se permitirá crear, editar ni eliminar categorías personalizadas en V1.
* Categoría e ícono son independientes. Todo hábito nuevo requiere un ícono oficial válido entre los dieciséis recursos disponibles. Billete está aprobado como recurso de referencia de Finanzas personales, pero puede elegirse para cualquier categoría. Elegir o cambiar una categoría no impone ni cambia automáticamente el ícono.
* Cambiar solo la categoría no modifica días, estado, cumplimientos ni fechas históricas y no crea una versión de programación semanal. No se aprueban métricas históricas por categoría ni una reconstrucción retrospectiva de categorías desconocidas.

### Transición de hábitos existentes

Los hábitos históricos podrán conservar `CategoriaId` e `IconoId` nulos durante una transición compatible. No se les asignará una categoría ni un ícono arbitrarios y no se borrarán o reconstruirán datos históricos. La excepción transitoria no habilita nuevas creaciones sin categoría e ícono válidos después de activar la funcionalidad.

Las lecturas deberán representar ambas ausencias durante la transición. Una categoría informada deberá ser válida; no se utilizará el nombre, el ícono o el texto del hábito para inferirla. Omitir la categoría en una edición conserva su valor, incluso cuando sea nulo; editar un hábito histórico sin categoría no obliga a clasificarlo. No se permite eliminar una categoría asignada. El tratamiento de `null` explícito frente a campo ausente y la semántica de edición del ícono quedan pendientes de especificación en API-CAT-01; no se asumen resueltos. No se ejecuta ninguna migración en AUD-BASE.

### Relación con las aprobaciones visuales

Antecedente histórico: la aprobación inicial de PEN-006 del 18/10/2026 mostraba cinco categorías y quince íconos. Las revisiones visuales aprobadas el 19/10/2026 establecen diez categorías en lista sin íconos en el Paso 1 y dieciséis íconos independientes en grilla 4 × 4 en el Paso 2; Libro permanece seleccionado y Billete ocupa fila 4/columna 4. Pasos 3–5 y el check/confeti aprobados se conservan. Las decisiones de Categorías V1 y FechaInicio automática local están aprobadas como producto, pero todavía requieren implementación. Esta aprobación visual es estática y no certifica contratos, desplazamiento, selección, navegación ni accesibilidad funcional. PEN-007 deberá contemplar edición de categoría e ícono, sin iniciarlo en AUD-BASE.

---

# 3. Estado del hábito

Un hábito podrá estar:

* **Activo**
* **Inactivo**

## 3.1. Activo

Un hábito activo:

* Aparece entre los hábitos actuales del usuario.
* Puede ser marcado como realizado.
* Participa en el seguimiento actual.
* Puede participar en el cálculo de la racha general cuando corresponda.

## 3.2. Inactivo

Un hábito inactivo:

* No aparece entre los hábitos activos.
* No genera nuevos pendientes.
* No participa del seguimiento actual.
* Conserva su información.
* Conserva sus cumplimientos históricos.
* Puede ser consultado posteriormente.
* No admite nuevos cumplimientos, incluso para fechas pasadas.

## 3.3. Eliminación

En V1 no se eliminará físicamente un hábito.

La acción de "eliminar" un hábito se interpretará como **desactivarlo**.

La desactivación no elimina los cumplimientos existentes.

No existe reactivación de hábitos en V1.

## 3.4. Interpretación histórica del estado

El estado **Activo/Inactivo** de un hábito tendrá interpretación histórica por fecha civil. Para determinar las oportunidades programadas de una fecha se considerarán conjuntamente la programación vigente en esa fecha, el estado vigente en esa fecha y `FechaInicio`. No se utilizará el estado actual para reinterpretar todas las fechas pasadas.

Cada período de estado utilizará el intervalo semiabierto `[VigenteDesde, VigenteHasta)`. Un hábito nuevo comienza con un período **Activo** desde `FechaInicio`. Al desactivarlo se cierra el período abierto y comienza uno **Inactivo** en la fecha civil local del propietario; varios intentos de desactivación en el mismo día se consolidan y no crean períodos vacíos. Si se desactiva antes de `FechaInicio`, el único período inicial comienza en `FechaInicio` como **Inactivo**. V1 no permite reactivar hábitos.

Los cumplimientos anteriores se conservan aunque el hábito pase a Inactivo. No se admiten nuevos cumplimientos cuando el hábito está actualmente Inactivo; además, el estado histórico de la fecha solicitada debe ser conocido y **Activo**. Un estado histórico desconocido responde `422 Unprocessable Entity` (`habit_status_history_unknown`) y nunca se interpreta como Inactivo ni como incumplimiento. Mientras la inicialización global del historial no esté completa, las operaciones que dependen de él responden `503 Service Unavailable` (`habit_status_history_not_initialized`).

TEMP-005 inicializará los hábitos existentes desde un corte UTC explícito e inmutable. La captura congela el estado persistido, `FechaInicio`, la zona IANA del propietario y la fecha civil local del corte. Los reintentos deben consumir ese mismo snapshot y no reconstruirlo a partir de valores actuales modificados. Los cambios anteriores a la incorporación del historial que no estén respaldados por datos persistidos no podrán reconstruirse con certeza. El historial debe completarse antes de habilitar desactivaciones y nuevos cumplimientos dependientes del estado, y antes de implementar métricas históricas.

---

# 4. Cumplimientos

## 4.1. Concepto

Un cumplimiento representa que un hábito fue realizado en una fecha determinada.

Conceptualmente contiene:

* Identificador.
* Hábito.
* Fecha.

## 4.2. Registro

Cuando el usuario marca un hábito como realizado, se crea un cumplimiento para esa fecha.

La existencia del registro representa el cumplimiento.

No se almacenará un registro adicional para representar "no realizado".

Solo se permitirá registrar cumplimientos para hoy o ayer, entendidos como fechas civiles locales del usuario. Se rechazarán fechas anteriores a ayer, futuras, anteriores a `FechaInicio` o no programadas según la versión histórica aplicable, así como nuevos cumplimientos de hábitos actualmente inactivos. La fecha local se calculará desde un instante UTC y la zona horaria IANA persistida del usuario; nunca se utilizará la zona horaria del servidor.

Un hábito inexistente o perteneciente a otro usuario responderá `404 Not Found`, sin revelar si existe. Sin autenticación válida se responderá `401 Unauthorized`. Un cumplimiento duplicado responderá `409 Conflict`. COMP-004 realizará una comprobación previa de duplicados; COMP-005 agregará la garantía de unicidad ante concurrencia.

## 4.3. Duplicados

Un mismo hábito no podrá tener más de un cumplimiento para la misma fecha.

## 4.4. Días no programados

No se considera incumplimiento que un hábito no tenga un registro en una fecha en la que no estaba programado.

No se permitirá registrar un cumplimiento para un día no programado según la versión vigente en esa fecha.

Ejemplo:

**Ir al gimnasio**

Días:

* Lunes.
* Miércoles.
* Viernes.

El martes no genera una obligación, no participa negativamente del cálculo de cumplimiento y no admite un nuevo cumplimiento para ese hábito.

---

# 5. Consulta diaria

Para una fecha determinada, CONSTIA deberá obtener:

1. Los hábitos que estaban activos para el usuario en la fecha consultada.
2. Los hábitos cuya versión de programación vigente en esa fecha incluye el día de la semana correspondiente y cuya `FechaInicio` ya se alcanzó.
3. El estado de cumplimiento de esos hábitos para esa fecha.

Esto permite construir la lista diaria de hábitos.

---

# 6. Calendario

El usuario podrá navegar por fechas y consultar los hábitos correspondientes.

Para cada fecha, usando la programación y el estado vigentes en ese día, deberán diferenciarse:

* Hábitos programados y cumplidos.
* Hábitos programados todavía no cumplidos.
* Días en los que determinados hábitos no correspondían.

Un día en el que un hábito no estaba programado no deberá presentarse como un incumplimiento de ese hábito.

---

# 7. Racha general

## 7.1. Definición

La racha general representa la cantidad de días consecutivos en los que el usuario alcanzó al menos el 70% de los hábitos programados para cada día.

## 7.2. Cálculo

Para cada día:

1. Obtener los hábitos que estaban activos y programados para ese día según su versión histórica y `FechaInicio`.
2. Contar cuántos fueron cumplidos.
3. Calcular el porcentaje de cumplimiento.
4. Determinar si se alcanzó el mínimo del 70%.

El número mínimo de hábitos necesarios se redondea hacia arriba.

### Ejemplos

| Programados | Mínimo cumplido |
| ----------: | --------------: |
|           1 |               1 |
|           2 |               2 |
|           3 |               3 |
|           4 |               3 |
|           5 |               4 |
|           6 |               5 |
|           7 |               5 |
|          10 |               7 |

## 7.3. Día sin hábitos programados

Si un día no tiene ningún hábito programado:

* No incrementa la racha.
* No rompe la racha.

El día se considera neutro.

## 7.4. Hábitos inactivos

Los hábitos inactivos no participan del cálculo de la racha actual durante el período en que estuvieron inactivos. La interpretación de fechas históricas deberá utilizar el estado vigente en cada fecha, no el estado actual.

Sus cumplimientos históricos pueden seguir utilizándose para consultas y estadísticas históricas del hábito.

---

# 8. Racha individual

## 8.1. Definición

La racha individual mide los cumplimientos consecutivos de un hábito en las ocasiones en que ese hábito estaba programado según la versión vigente en cada fecha. Las fechas anteriores a `FechaInicio` no son ocasiones programadas.

## 8.2. Ejemplo

Hábito:

**Ir al gimnasio**

Días:

* Lunes.
* Miércoles.
* Viernes.

Cumplimientos:

* Lunes → cumplido.
* Miércoles → cumplido.
* Viernes → cumplido.
* Lunes siguiente → cumplido.

Resultado:

**Racha individual: 4 cumplimientos consecutivos.**

Martes y jueves no interrumpen la racha porque el hábito no estaba programado esos días.

---

# 9. Estadísticas

## 9.1. Estadísticas generales

CONSTIA podrá mostrar:

* Racha actual.
* Mejor racha.
* Cumplimiento general.
* Evolución del progreso.

## 9.2. Estadísticas por hábito

Cada hábito podrá mostrar:

* Racha actual.
* Mejor racha.
* Cantidad de cumplimientos.
* Porcentaje de cumplimiento.
* Historial por fecha.

## 9.3. Historial de hábitos inactivos

Un hábito inactivo conservará sus estadísticas e historial histórico.

La desactivación solamente afecta al seguimiento actual.

---

# 10. Visualización del progreso

El historial podrá representarse mediante un calendario o heatmap.

La visualización deberá permitir identificar:

* Cumplimientos.
* Períodos de constancia.
* Rachas.
* Evolución del progreso.

La interfaz debe priorizar una experiencia visual positiva y clara.

Los días sin cumplimiento no requieren un registro persistido de "no realizado".

---

# 11. Reglas de integridad

## Usuarios

* El email debe ser único.
* Cada usuario debe tener una zona horaria identificada mediante un identificador IANA válido.
* Un usuario solamente puede acceder a sus propios datos.

## Hábitos

* Todo hábito debe pertenecer a un usuario.
* Todo hábito debe tener al menos un día programado.
* Un hábito nuevo comienza activo.
* Cada hábito nuevo requiere una sola categoría válida y un ícono oficial válido al habilitarse la funcionalidad; sus valores nulos solo se admiten en hábitos históricos durante la transición.
* Categoría e ícono son independientes. Omitir la categoría al editar conserva su valor y no se permite eliminarla una vez asignada.
* `FechaInicio` de nuevos hábitos se calcula en el backend desde UTC y la zona IANA persistida del usuario, mediante `TimeProvider`; no procede de una fecha ingresada por el cliente.
* Las fechas de inicio históricas se conservan. La fecha inicial no tiene que ser un día programado.
* Desactivar un hábito no elimina su historial.
* Las versiones de programación deben tener intervalos semiabiertos sin solapamientos y una única versión aplicable para cada fecha desde `FechaInicio`.
* Los cambios de programación tienen vigencia desde la fecha local del usuario y no editan versiones históricas anteriores en V1.
* Las ediciones de programación de un mismo día local se consolidan en una versión; prevalece la última configuración para todo ese día.
* El estado del hábito se interpreta históricamente por fecha civil en períodos semiabiertos. Los cambios anteriores al corte de inicialización no se pueden reconstruir con certeza.
* Un estado histórico desconocido no se interpreta como Inactivo ni como falta de cumplimiento; las consultas o escrituras que requieran dicho estado se rechazan con `422`.
* Las escrituras que dependan del historial permanecen bloqueadas con `503` hasta que la inicialización global se haya verificado.

## Cumplimientos

* Todo cumplimiento debe pertenecer a un hábito.
* No puede existir más de un cumplimiento del mismo hábito para una misma fecha.
* Un cumplimiento solamente debe registrarse para un hábito válido del usuario.
* Solo se permiten cumplimientos para hoy o ayer según la fecha civil local del usuario; nunca para fechas anteriores a ayer, futuras ni anteriores a `FechaInicio`.
* La fecha actual se determina según la zona horaria del usuario.
* Un cumplimiento debe corresponder a un día programado según la versión histórica aplicable.
* No se admiten nuevos cumplimientos para hábitos actualmente inactivos.
* Los días no programados no generan incumplimientos persistidos ni admiten nuevos cumplimientos.
* Un cumplimiento que deja de corresponder a un día programado por una edición consolidada de esa fecha permanece almacenado y representa un hecho realizado, pero no es una oportunidad programada ni contribuye a métricas basadas en cumplimientos programados. No se elimina ni modifica retroactivamente.

---

# 12. Datos calculados

Las siguientes métricas se consideran derivadas:

* Racha actual.
* Mejor racha.
* Porcentaje de cumplimiento.
* Progreso general.
* Estadísticas agregadas.

No deben persistirse como información independiente salvo que una necesidad técnica futura justifique explícitamente hacerlo.

---

# 13. Fuentes de verdad

Cuando exista una diferencia entre una implementación y esta especificación:

1. La regla funcional definida aquí tiene prioridad.
2. Los cambios funcionales deberán reflejarse primero en la especificación.
3. La implementación deberá adaptarse posteriormente.

Las decisiones de V2 no deben incorporarse a V1 sin modificar previamente el alcance del MVP.

## Decisión funcional del 19/10/2026

Las secciones 2.3 y 2.5 formalizan el catálogo de diez categorías V1 y `FechaInicio` automática local, aprobados explícitamente en esa fecha. Reemplazan la antigua exclusión de Categorías y el ingreso manual de fecha al crear. El código y los contratos existentes deberán adaptarse posteriormente; esta actualización documental no los declara implementados. Las reglas previas de programación versionada, estados por fecha, cumplimientos, zonas horarias y métricas se conservan.

Reconciliación AUD-BASE del 19/10/2026: se incorporan dieciséis íconos, obligatoriedad independiente en nuevas altas, conservación de ambos identificadores nulos históricos y rechazo expreso de `FechaInicio` enviada por clientes en el contrato nuevo. Se conservan íntegros los períodos semiabiertos, el snapshot inmutable de TEMP-005, las respuestas de historia desconocida/inicialización y la ventana de cumplimientos de hoy o ayer de main integrado. No se implementan contratos, validaciones ni migraciones en esta revisión documental.
