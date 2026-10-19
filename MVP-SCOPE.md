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
* Categoría oficial, independiente del ícono elegido.
* Ícono oficial elegido independientemente de la categoría.
* Usuario propietario.
* Días de la semana en los que debe realizarse.
* Versiones históricas de sus días programados.

Un hábito deberá tener al menos un día de la semana seleccionado.

Para las nuevas creaciones, `FechaInicio` se asignará automáticamente en el backend a partir del instante UTC actual convertido a la zona IANA persistida del usuario. El usuario no la ingresará y el cliente no será su fuente de autoridad. El nuevo contrato rechazará expresamente las solicitudes que envíen `FechaInicio`. Se conservará como fecha civil persistida y el cálculo será verificable mediante `TimeProvider`. No tiene que coincidir con un día programado; no se recalcularán las fechas de inicio de hábitos existentes.

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

Solo se podrán registrar cumplimientos para hoy o ayer, entendidos como fechas civiles locales del usuario. Se rechazarán fechas anteriores a ayer, futuras, anteriores a `FechaInicio` o no programadas según la programación histórica, así como nuevos cumplimientos de hábitos actualmente inactivos. La fecha local se calculará desde un instante UTC y la zona horaria IANA persistida del usuario. Un hábito ajeno o inexistente responderá `404`; la falta de autenticación válida responderá `401`; un duplicado responderá `409`. COMP-004 realizará una comprobación previa y COMP-005 incorporará la garantía ante concurrencia.

Si un cumplimiento ya registrado deja de corresponder a un día programado debido a una edición consolidada de esa fecha, permanecerá almacenado como hecho realizado. No se contará como oportunidad programada ni contribuirá a métricas basadas en cumplimientos programados; no se eliminará ni modificará retroactivamente.

---

### 3.5. Calendario

El usuario podrá consultar sus hábitos organizados por fecha.

Para cada día se deberán mostrar únicamente los hábitos cuyo estado histórico sea **Activo** en esa fecha, cuya `FechaInicio` ya se alcanzó y cuya versión de programación vigente incluía ese día. Un estado desconocido no se presentará como Inactivo ni como incumplimiento; la interfaz deberá poder diferenciarlo hasta que el historial esté disponible.

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

Para cada fecha se utilizarán la programación y el estado vigentes en esa fecha. Los períodos de estado son semiabiertos `[VigenteDesde, VigenteHasta)`. Un hábito nuevo comienza Activo desde `FechaInicio`; al desactivarlo pasa a Inactivo desde la fecha civil local del propietario. V1 no permite reactivarlo y los cumplimientos ya registrados se conservan. Las rachas individuales y estadísticas interpretarán las versiones históricas de programación y estado, sin cambiar las fórmulas definidas en la especificación.

TEMP-005 inicializa hábitos existentes a partir de un único corte UTC persistido y un snapshot inmutable de estado, `FechaInicio`, zona IANA y fecha local del corte. Antes de capturar el snapshot se bloquean altas de hábitos y cambios de zona horaria; hasta verificar el backfill se bloquean desactivaciones y nuevos cumplimientos. Las operaciones independientes pueden continuar cuando no alteren los datos congelados ni dependan del historial. Los cambios de estado previos al corte no podrán reconstruirse con certeza.

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

### 3.11. Categorías oficiales de hábitos

Decisión funcional aprobada el **19/10/2026**: las categorías predefinidas forman parte de V1. El catálogo inicial contiene exactamente estas diez categorías:

| Categoría oficial | Ejemplos | Ícono de referencia visual |
| --- | --- | --- |
| Salud y movimiento | Caminar, entrenar, hacer ejercicio | Mancuerna |
| Alimentación e hidratación | Beber agua, comer frutas, cocinar saludable | Manzana |
| Descanso y sueño | Dormir temprano, mantener una rutina nocturna | Luna |
| Bienestar emocional | Meditar, escribir un diario, practicar gratitud | Loto |
| Aprendizaje y crecimiento | Leer, estudiar idiomas, aprender habilidades | Libro |
| Productividad y organización | Planificar el día, organizar tareas, estudiar | Portátil |
| Relaciones y vínculos | Llamar familiares, compartir tiempo con amigos | Corazón |
| Finanzas personales | Registrar gastos, ahorrar, revisar presupuestos | Billete |
| Creatividad y ocio | Tocar música, dibujar, practicar hobbies | Nota musical |
| Hogar y vida cotidiana | Ordenar, limpiar, cuidar plantas o mascotas | Brote |

Cada hábito nuevo deberá pertenecer a una sola categoría válida del catálogo, identificada por un ID estable y no por su nombre visible. También deberá tener un ícono oficial válido. La categoría podrá consultarse y editarse mediante las operaciones de hábitos, respetando propiedad y reglas generales de V1. Omitir la categoría en una edición conservará su valor; no se permitirá eliminar una categoría ya asignada. Editar un hábito histórico sin categoría no obligará a clasificarlo. La semántica de `null` explícito y la edición de ícono se precisarán en API-CAT-01. No habrá creación, edición ni eliminación de categorías personalizadas.

Categoría e ícono son conceptos independientes: el usuario podrá elegir cualquiera de los dieciséis íconos oficiales para su hábito; la categoría no impone uno automáticamente. La referencia visual de la tabla no es una restricción de selección. Billete es el recurso oficial de referencia para Finanzas personales y puede elegirse para cualquier categoría; Finanzas personales no lo selecciona automáticamente.

Los hábitos históricos podrán conservar `CategoriaId` e `IconoId` nulos, sin asignaciones arbitrarias ni pérdida de información histórica. Las nuevas creaciones requerirán ambos identificadores oficiales válidos una vez habilitada la funcionalidad. La estrategia recomendada y sus verificaciones están en el [análisis de impacto](docs/superpowers/plans/2026-10-19-categorias-v1-fecha-inicio.md); todavía no se ejecutó ninguna migración.

Antecedente histórico: PEN-006 fue aprobado visualmente el 18/10/2026 con cinco categorías y quince íconos. Las revisiones aprobadas el 19/10/2026 conservan esa aprobación inicial y establecen diez categorías sin íconos en el Paso 1 y dieciséis íconos en grilla 4 × 4 en el Paso 2, con Billete en fila 4/columna 4. La aprobación es exclusivamente visual y estática: no certifica selección, desplazamiento, navegación, persistencia ni accesibilidad funcional. AUD-BASE no modifica Penpot ni inicia PEN-007.

---

## 4. Funcionalidades fuera del MVP

Las siguientes funcionalidades quedan fuera de V1:

* Recordatorios.
* Objetivos.
* Categorías personalizadas (crear, editar o eliminar el catálogo por parte del usuario).
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

## 7. Trazabilidad de decisiones funcionales

El 19/10/2026 se aprobó incluir el catálogo de diez categorías oficiales y calcular automáticamente `FechaInicio` según la fecha civil local del usuario. Esto reemplaza la exclusión anterior de Categorías y resuelve los dos pendientes funcionales registrados durante PEN-006; no certifica su implementación ni modifica aprobaciones visuales anteriores. Las categorías personalizadas siguen fuera de V1. Reglas detalladas en `SPECIFICATION.md`, secciones 2.3 y 2.5.

La reconciliación AUD-BASE del 19/10/2026 incorpora además la obligatoriedad de categoría e ícono válidos en nuevas altas, su independencia, la conservación de valores nulos históricos y el rechazo expreso de `FechaInicio` enviada en el contrato nuevo. Conserva las reglas temporales de main integrado, incluida la ventana de cumplimientos de hoy o ayer y la inicialización de TEMP-005; no implementa estas nuevas decisiones en código.
