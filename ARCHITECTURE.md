# Arquitectura de CONSTIA

## 1. Propósito general

La arquitectura de CONSTIA separa las responsabilidades de la interfaz, la API, la lógica de aplicación, las reglas del dominio y el acceso a datos. Esta separación ayuda a mantener el MVP simple, verificar su comportamiento y permitir su evolución sin agregar estructura innecesaria.

La arquitectura se organiza así:

```text
CONSTIA/
├── src/
│   ├── Constia.Web
│   ├── Constia.API
│   ├── Constia.Application
│   ├── Constia.Domain
│   └── Constia.Infrastructure
└── tests/
    └── Constia.Tests
```

El stack previsto es C#, .NET 10, Blazor, ASP.NET Core Web API, Entity Framework Core 10 y SQL Server 2022.

## 2. Responsabilidad de cada proyecto

### `Constia.Web`

Es la interfaz Blazor de CONSTIA. Presenta las funcionalidades del MVP al usuario y consume `Constia.API` mediante HTTP. No tiene referencias directas a `Constia.Application`, `Constia.Domain` ni `Constia.Infrastructure`.

### `Constia.API`

Expone la API HTTP y recibe las solicitudes de `Constia.Web` u otros clientes. Traduce las entradas y salidas del protocolo HTTP y coordina la ejecución de los casos de uso mediante `Constia.Application`.

### `Constia.Application`

Coordina las operaciones que ofrece el sistema, como registrar un usuario, gestionar hábitos, registrar cumplimientos y consultar el progreso. Aplica las reglas del flujo de cada operación y utiliza el dominio para las reglas de negocio.

### `Constia.Domain`

Contiene los conceptos y reglas centrales del producto, como usuarios, hábitos, programación semanal y cumplimientos. Las reglas funcionales del dominio deben poder expresarse sin depender de la API, la base de datos o detalles de infraestructura.

### `Constia.Infrastructure`

Implementa el acceso a datos y los detalles técnicos de persistencia previstos, usando Entity Framework Core y SQL Server. Provee a las capas superiores las operaciones necesarias para guardar y consultar información.

### `Constia.Tests`

Contiene pruebas de las reglas del dominio y de los casos de uso de aplicación. Las pruebas ayudan a verificar el comportamiento acordado en `MVP-SCOPE.md` y `SPECIFICATION.md`.

## 3. Dependencias permitidas

Las dependencias entre proyectos son:

- Web → API mediante HTTP; sin referencia directa a Application, Domain o Infrastructure
- API → Application, Infrastructure
- `Constia.API` puede referenciar `Constia.Infrastructure` para realizar la composición de dependencias y configuración de infraestructura, pero no debe utilizar directamente sus mecanismos de persistencia para ejecutar casos de uso.
- Application → Domain
- Infrastructure → Application, Domain
- Tests → Domain, Application

No se permiten dependencias circulares. No deben agregarse referencias entre proyectos fuera de las permitidas sin revisar y actualizar primero la arquitectura acordada.

## 4. Lógica por capa

- **Web:** presentación e interacción de la interfaz Blazor. Envía solicitudes HTTP a la API; la lógica de negocio pertenece a Application o Domain.
- **API:** recepción y validación de la forma de las solicitudes, coordinación HTTP y presentación de respuestas. La lógica de negocio pertenece a Application o Domain.
- **Application:** coordinación de casos de uso y de los pasos necesarios para cumplirlos. Las reglas propias del negocio deben permanecer en Domain.
- **Domain:** reglas y conceptos funcionales, por ejemplo, que un hábito tenga al menos un día programado, que un hábito nuevo sea activo y cómo se determina el cumplimiento de las reglas de rachas.
- **Infrastructure:** consultas, persistencia y adaptación a Entity Framework Core y SQL Server. No debe definir reglas de negocio.
- **Tests:** verificaciones del comportamiento de Domain y Application. No deben alterar la funcionalidad para hacer pasar una prueba.

## 5. Responsabilidades que deben evitarse

- **Web:** no debe contener reglas de negocio ni acceder directamente a Application, Domain, Infrastructure o a la base de datos. La comunicación con el backend se realiza mediante HTTP a la API.
- **API:** no debe contener cálculos de rachas o estadísticas ni reglas de negocio; tampoco debe acceder directamente a la base de datos para ejecutar casos de uso.
- **Application:** no debe asumir las responsabilidades del transporte HTTP ni depender de detalles concretos de SQL Server para definir reglas funcionales.
- **Domain:** no debe depender de ASP.NET Core, Entity Framework Core, SQL Server ni de la interfaz.
- **Infrastructure:** no debe decidir reglas del producto ni convertirse en el lugar donde se coordinan los casos de uso.
- **Tests:** no deben cambiarse o eliminarse únicamente para conseguir que pasen. Si contradicen la especificación, se debe informar el conflicto.

## 6. Flujo general de una operación

Una operación sigue, en términos generales, este recorrido:

1. El usuario interactúa con `Constia.Web`, que envía una solicitud HTTP a `Constia.API`.
2. `Constia.API` recibe la solicitud y la entrega al caso de uso correspondiente en `Constia.Application`.
3. `Constia.Application` coordina la operación y aplica las reglas del flujo, utilizando `Constia.Domain` para las reglas de negocio.
4. Cuando necesita consultar o guardar información, la operación utiliza las capacidades de persistencia implementadas por `Constia.Infrastructure`.
5. `Constia.Infrastructure` accede a SQL Server mediante Entity Framework Core y devuelve el resultado a la operación.
6. `Constia.Application` entrega el resultado a `Constia.API`, que prepara la respuesta HTTP.
7. `Constia.Web` recibe la respuesta HTTP y presenta el resultado al usuario.

La comunicación de `Constia.Web` con el backend se realiza mediante HTTP a través de `Constia.API`. La lógica de negocio permanece en Domain y Application; Infrastructure resuelve la persistencia y API gestiona la comunicación HTTP.

## 7. Simplicidad y evolución

- Implementar solamente lo necesario para el alcance definido en `MVP-SCOPE.md`.
- Usar `SPECIFICATION.md` como fuente de verdad para las reglas funcionales.
- Evitar sobreingeniería, cambios innecesarios y funcionalidades previstas para versiones futuras.
- No agregar proyectos, capas, patrones, dependencias o tecnologías que no hayan sido definidos o cuya necesidad no esté justificada.
- Mantener las responsabilidades separadas y las dependencias dentro de los límites establecidos.
- Si hay ambigüedad en requisitos, arquitectura o reglas de negocio, plantear la duda antes de implementar en lugar de asumir una solución.
