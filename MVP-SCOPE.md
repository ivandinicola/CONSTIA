# CONSTIA — MVP Scope

## 1. Producto

**CONSTIA — Tu constancia, día a día.**

CONSTIA es una aplicación de seguimiento de hábitos orientada a ayudar al usuario a construir constancia mediante la planificación de hábitos, el registro de cumplimientos, las rachas y la visualización del progreso.

---

## 2. Objetivo del MVP

El MVP debe permitir que una persona:

1. Cree una cuenta.
2. Inicie sesión.
3. Cree y configure hábitos.
4. Defina los días de la semana en los que debe realizar cada hábito.
5. Consulte los hábitos correspondientes a cada día.
6. Marque hábitos como realizados.
7. Consulte su historial.
8. Consulte sus rachas.
9. Consulte estadísticas básicas de progreso.
10. Conserve el historial de hábitos que ya no realiza mediante su desactivación.

El objetivo no es implementar todas las funcionalidades posibles de una aplicación de hábitos, sino construir una primera versión funcional, coherente y extensible.

---

## 3. Funcionalidades incluidas en V1

### 3.1. Registro y autenticación

El usuario podrá:

* Registrarse.
* Iniciar sesión.
* Acceder únicamente a sus propios datos.

La cuenta estará compuesta por:

* Identificador.
* Nombre.
* Email.
* Contraseña.
* Zona horaria identificada por un identificador IANA válido.

Las contraseñas no se almacenarán en texto plano.

Los usuarios nuevos deberán seleccionar explícitamente su zona horaria al registrarse y podrán modificarla posteriormente. Los usuarios existentes recibirán provisionalmente `Etc/UTC`, sin inferir su ubicación. Cambiar la zona no modificará fechas civiles de cumplimientos ni vigencias históricas. La fecha actual se determinará usando el instante UTC convertido a la zona del usuario, y el cálculo deberá poder probarse mediante `TimeProvider`.

---

### 3.2. Gestión de hábitos

El usuario podrá:

* Crear un hábito.
* Consultar sus hábitos activos.
* Editar un hábito.
* Desactivar un hábito.
* Consultar sus hábitos inactivos.

Un hábito tendrá:

* Nombre.
* Descripción.
* Fecha de creación.
* Fecha de inicio.
* Estado.
* Usuario propietario.
* Días de la semana en los que debe realizarse.
* Versiones históricas de sus días programados.

Un hábito deberá tener al menos un día de la semana seleccionado.

---

### 3.3. Programación semanal

Cada hábito podrá configurarse para:

* Todos los días.
* Determinados días de la semana.

Ejemplos:

**Tomar 2 L de agua**

* Lunes
* Martes
* Miércoles
* Jueves
* Viernes
* Sábado
* Domingo

**Ir al gimnasio**

* Lunes
* Miércoles
* Viernes

Los hábitos solamente serán considerados para un día cuando estén programados para ese día.

Las versiones tendrán vigencias semiabiertas `[VigenteDesde, VigenteHasta)`, sin solapamientos, y habrá una única versión aplicable desde `FechaInicio`. Los cambios de programación regirán desde la fecha local del usuario y no se permitirá editar retroactivamente versiones anteriores en V1. Varias ediciones durante el mismo día local se consolidarán en una versión, prevaleciendo la última programación para todo ese día. Cambiar nombre o descripción no generará una versión. Si se edita antes de `FechaInicio`, se actualizará la versión inicial sin adelantar su vigencia.

La programación actualmente almacenada se convertirá en la versión inicial desde `FechaInicio`; no se reconstruirán cambios históricos desconocidos.

---

### 3.4. Cumplimientos

El usuario podrá marcar un hábito como realizado.

Cada cumplimiento estará asociado a:

* Un hábito.
* Una fecha.

Solo se almacenarán los cumplimientos realizados.

No se generarán registros separados para representar un hábito no realizado.

Se podrán registrar cumplimientos para hoy y para fechas pasadas que correspondieran según la programación histórica. Se rechazarán fechas futuras, anteriores a `FechaInicio` o no programadas, así como nuevos cumplimientos de hábitos actualmente inactivos, incluso retroactivos. "Hoy" se determina según la zona horaria del usuario. Un hábito ajeno o inexistente responderá `404`; la falta de autenticación válida responderá `401`; un duplicado responderá `409`. COMP-004 realizará una comprobación previa y COMP-005 incorporará la garantía ante concurrencia.

Si un cumplimiento ya registrado deja de corresponder a un día programado debido a una edición consolidada de esa fecha, permanecerá almacenado como hecho realizado. No se contará como oportunidad programada ni contribuirá a métricas basadas en cumplimientos programados; no se eliminará ni modificará retroactivamente.

---

### 3.5. Calendario

El usuario podrá consultar sus hábitos organizados por fecha.

Para cada día se deberán mostrar únicamente los hábitos que estaban activos en esa fecha, cuya `FechaInicio` ya se alcanzó y cuya versión de programación vigente incluía ese día.

La interfaz permitirá registrar cumplimientos desde la vista correspondiente.

---

### 3.6. Historial

Los cumplimientos históricos de los hábitos deberán conservarse.

El usuario podrá consultar el historial de un hábito activo o inactivo.

---

### 3.7. Racha general

CONSTIA tendrá una racha general del usuario.

Un día contará para la racha cuando el usuario complete al menos el 70% de los hábitos programados para ese día.

La cantidad mínima de hábitos que deben completarse se redondeará hacia arriba.

Un día sin hábitos programados:

* No incrementa la racha.
* No rompe la racha.

Para cada fecha se utilizarán la programación y el estado vigentes en esa fecha. Las rachas individuales y estadísticas interpretarán las versiones históricas de programación, sin cambiar las fórmulas definidas en la especificación. Los cambios de estado anteriores a la incorporación del historial no podrán reconstruirse con certeza. La implementación del historial de estados corresponde a TEMP-005 y debe preceder a las métricas históricas.

---

### 3.8. Racha individual

Cada hábito tendrá una racha individual.

La racha individual se calculará según los cumplimientos consecutivos en las ocasiones en que el hábito estaba programado.

Las ocasiones programadas se determinarán mediante la versión histórica correspondiente y el estado vigente del hábito en cada fecha. Los hábitos inactivos conservarán su historial y los cumplimientos fuera de programación no contarán como cumplimientos programados.

Esto permitirá comparar hábitos diarios con hábitos programados únicamente determinados días.

---

### 3.9. Estadísticas

V1 incluirá estadísticas básicas:

#### Generales

* Racha actual.
* Mejor racha.
* Cumplimiento general.
* Progreso a lo largo del tiempo.

#### Por hábito

* Racha actual.
* Mejor racha.
* Cantidad de cumplimientos.
* Porcentaje de cumplimiento.
* Historial.

---

### 3.10. Visualización del progreso

El MVP deberá permitir una visualización del historial mediante una representación tipo calendario o heatmap.

El objetivo es mostrar visualmente el progreso y la constancia del usuario.

La interfaz deberá priorizar una representación clara y motivadora.

---

## 4. Funcionalidades fuera del MVP

Las siguientes funcionalidades quedan fuera de V1:

* Recordatorios.
* Objetivos.
* Categorías.
* Estadísticas avanzadas.
* Gamificación.
* Insignias.
* Niveles.
* Notificaciones.
* Aplicación móvil nativa.

Estas funcionalidades podrán formar parte de futuras versiones.

---

## 5. Principios del MVP

### Simplicidad

Implementar solamente lo necesario para cumplir el alcance.

### Conservación del historial

Desactivar un hábito no debe eliminar su historial.

### Datos calculados

Las rachas y estadísticas derivadas deberán calcularse a partir de los datos almacenados siempre que sea razonable.

### Aislamiento de usuarios

Un usuario solamente podrá acceder a sus propios datos.

### Extensibilidad

Las decisiones de V1 no deberán impedir innecesariamente futuras funcionalidades.

### No sobreingeniería

No implementar anticipadamente funcionalidades de V2 ni infraestructura que no sea necesaria para el MVP.

---

## 6. Criterio general de finalización

CONSTIA V1 se considerará completada cuando:

* Las funcionalidades incluidas estén implementadas.
* Las reglas definidas en `SPECIFICATION.md` se cumplan.
* Las funcionalidades principales estén verificadas mediante pruebas.
* El proyecto pueda ejecutarse desde un entorno limpio siguiendo `DEVELOPMENT.md`.
* El código se encuentre versionado mediante Git.
* La versión inicial pueda documentarse como `v0.1.0`.
