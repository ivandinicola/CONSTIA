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
* Usuario propietario.
* Días de la semana programados.

## 2.3. Creación

Para crear un hábito se deberá proporcionar como mínimo:

* Nombre.
* Fecha de inicio.
* Al menos un día programado.

La descripción será opcional.

Todo hábito nuevo comenzará en estado **Activo**.

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

## 3.4. Interpretación histórica del estado

El estado **Activo/Inactivo** de un hábito tendrá interpretación histórica por fecha civil. Para determinar las oportunidades programadas de una fecha se considerarán conjuntamente la programación vigente en esa fecha, el estado vigente en esa fecha y `FechaInicio`. No se utilizará el estado actual para reinterpretar todas las fechas pasadas.

La implementación del historial de estados corresponde a TEMP-005 y deberá completarse antes de implementar las métricas históricas. Los cambios de estado anteriores a la incorporación de ese historial no podrán reconstruirse con certeza si no fueron almacenados.

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

Se permitirá registrar cumplimientos para la fecha local actual del usuario y para fechas pasadas que correspondieran según la programación histórica. Se rechazarán fechas futuras, fechas anteriores a `FechaInicio`, fechas no programadas según la versión aplicable y nuevos cumplimientos de hábitos actualmente inactivos. La fecha "hoy" se determina con la zona horaria del usuario, no con la del servidor.

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
* Desactivar un hábito no elimina su historial.
* Las versiones de programación deben tener intervalos semiabiertos sin solapamientos y una única versión aplicable para cada fecha desde `FechaInicio`.
* Los cambios de programación tienen vigencia desde la fecha local del usuario y no editan versiones históricas anteriores en V1.
* Las ediciones de programación de un mismo día local se consolidan en una versión; prevalece la última configuración para todo ese día.
* El estado del hábito se interpreta históricamente por fecha civil. Los cambios anteriores a la implementación de ese historial no se pueden reconstruir con certeza.

## Cumplimientos

* Todo cumplimiento debe pertenecer a un hábito.
* No puede existir más de un cumplimiento del mismo hábito para una misma fecha.
* Un cumplimiento solamente debe registrarse para un hábito válido del usuario.
* Solo se permiten cumplimientos para hoy o fechas pasadas, nunca futuras ni anteriores a `FechaInicio`.
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
