# AGENTS.md

## Propósito

CONSTIA — Tu constancia, día a día.

Este archivo define pautas para contribuir al proyecto. Mantener las soluciones simples, evitar sobreingeniería y preservar una separación clara de responsabilidades.

## Estructura del proyecto

Stack previsto:

- C#, .NET 10, ASP.NET Core Web API, Entity Framework Core 10 y SQL Server 2022.
- Blazor para la interfaz.
- xUnit para pruebas y Git para control de versiones.

Arquitectura prevista:

- `Constia.API`
- `Constia.Application`
- `Constia.Domain`
- `Constia.Infrastructure`
- `Constia.Tests`

Dependencias permitidas:

- API → Application, Infrastructure
- Application → Domain
- Infrastructure → Application, Domain
- Tests → Domain, Application

No agregar dependencias circulares. Respetar las dependencias permitidas y las responsabilidades de cada proyecto.

## Convenciones de trabajo

- Usar `MVP-SCOPE.md` y `SPECIFICATION.md` como fuentes de verdad funcionales cuando estén disponibles.
- Evitar sobreingeniería y cambios innecesarios. Mantener los cambios enfocados en el objetivo solicitado.
- No agregar dependencias ni tecnologías sin justificar su necesidad.
- Si hay ambigüedad en requisitos, arquitectura o reglas de negocio, no asumir una solución: plantear la duda antes de implementar.
- Antes de cambios relevantes, indicar brevemente qué archivos se modificarán, qué se cambiará y por qué.
- Cuando una tarea provenga de Trello, incluir su identificador en el commit siguiendo la convención del proyecto.
- Mantener una separación clara de responsabilidades entre proyectos y capas.
- No modificar ni eliminar tests únicamente para conseguir que pasen. Informar cualquier conflicto entre los tests y la especificación.

## Verificación

- Después de cambios relevantes, compilar y ejecutar los tests correspondientes.
- No considerar una tarea terminada sin verificar el resultado.
- Informar qué verificaciones se ejecutaron y señalar cualquier limitación o fallo.
