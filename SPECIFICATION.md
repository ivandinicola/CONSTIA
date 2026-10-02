# CONSTIA — Functional Specification V1

## 1. Usuarios

### 1.1. Información

Cada usuario estará compuesto por:

* Identificador único.
* Nombre.
* Email.
* Contraseña.

### 1.2. Registro

El usuario podrá registrarse proporcionando:

* Nombre.
* Email.
* Contraseña.

El email deberá ser único.

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

## 3.3. Eliminación

En V1 no se eliminará físicamente un hábito.

La acción de "eliminar" un hábito se interpretará como **desactivarlo**.

La desactivación no elimina los cumplimientos existentes.

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

## 4.3. Duplicados

Un mismo hábito no podrá tener más de un cumplimiento para la misma fecha.

## 4.4. Días no programados

No se considera incumplimiento que un hábito no tenga un registro en una fecha en la que no estaba programado.

Ejemplo:

**Ir al gimnasio**

Días:

* Lunes.
* Miércoles.
* Viernes.

El martes no genera una obligación y no participa negativamente del cálculo de cumplimiento.

---

# 5. Consulta diaria

Para una fecha determinada, CONSTIA deberá obtener:

1. Los hábitos activos del usuario.
2. Los hábitos cuya configuración incluye el día de la semana correspondiente.
3. El estado de cumplimiento de esos hábitos para esa fecha.

Esto permite construir la lista diaria de hábitos.

---

# 6. Calendario

El usuario podrá navegar por fechas y consultar los hábitos correspondientes.

Para cada fecha deberán diferenciarse:

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

1. Obtener los hábitos activos programados para ese día.
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

Los hábitos inactivos no participan del cálculo de la racha actual.

Sus cumplimientos históricos pueden seguir utilizándose para consultas y estadísticas históricas del hábito.

---

# 8. Racha individual

## 8.1. Definición

La racha individual mide los cumplimientos consecutivos de un hábito en las ocasiones en que ese hábito estaba programado.

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
* Un usuario solamente puede acceder a sus propios datos.

## Hábitos

* Todo hábito debe pertenecer a un usuario.
* Todo hábito debe tener al menos un día programado.
* Un hábito nuevo comienza activo.
* Desactivar un hábito no elimina su historial.

## Cumplimientos

* Todo cumplimiento debe pertenecer a un hábito.
* No puede existir más de un cumplimiento del mismo hábito para una misma fecha.
* Un cumplimiento solamente debe registrarse para un hábito válido del usuario.
* Los días no programados no generan incumplimientos persistidos.

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
