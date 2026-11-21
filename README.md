# CONSTIA

### Tu constancia, día a día.

CONSTIA es una aplicación de seguimiento de hábitos diseñada para ayudar a las personas a construir constancia mediante pequeños objetivos repetidos en el tiempo.

La aplicación permite definir hábitos, elegir los días en los que deben realizarse, registrar cumplimientos y visualizar el progreso mediante rachas, estadísticas e historial.

---

## Estado del proyecto

**Candidato documental:** v0.1.0 — versión de código validada localmente.

El código de referencia es `395a2773524f801c70aa226d44c23908401d2229`, con [CI aprobada](https://github.com/ivandinicola/CONSTIA/actions/runs/44739455506). Las [notas de versión](docs/releases/v0.1.0.md) y la [matriz de aceptación V1](docs/releases/v0.1.0-aceptacion.md) están preparadas para revisión. La etiqueta y su SHA definitivo se aprobarán después de integrar y verificar el candidato documental.

Esta preparación no anuncia un servicio público ni acredita un despliegue. El alcance local admite una sola instancia de Web; los requisitos y límites están en [DEVELOPMENT.md](DEVELOPMENT.md).

El alcance funcional de la primera versión está definido en:

* [`MVP-SCOPE.md`](MVP-SCOPE.md) — alcance del MVP.
* [`SPECIFICATION.md`](SPECIFICATION.md) — especificación funcional.
* [`ARCHITECTURE.md`](ARCHITECTURE.md) — arquitectura técnica.

---

## Funcionalidades del MVP

### Usuarios

* Registro y confirmación de correo; cuenta pendiente hasta verificarla.
* Inicio de sesión.
* Autenticación y cierre de sesión con cookie protegida; JWT usado desde el servidor Web.
* Acceso aislado a los datos propios de cada usuario.
* Selección explícita de zona IANA y cambio desde Mi cuenta, sin reescribir fechas históricas.

### Hábitos

* Crear hábitos.
* Editar hábitos.
* Desactivar hábitos conservando su historial; sin reactivación en V1.
* Consultar hábitos activos.
* Consultar hábitos inactivos.
* Definir los días de la semana en los que se realiza cada hábito.
* Asignar una categoría del catálogo oficial de diez opciones, independiente del ícono.
* Elegir un ícono válido del catálogo oficial de veintiocho recursos, independientemente de la categoría.
* Asignar automáticamente la fecha de inicio desde la fecha civil local del usuario en el backend.

Las altas requieren categoría, ícono y al menos un día; el backend asigna `FechaInicio` y rechaza su envío por el cliente. Los hábitos legados pueden conservar categoría e ícono nulos. Al editar, omitir esos campos conserva su valor y no se permite borrarlos explícitamente. La edición Web usa ETag/If-Match y conserva el borrador ante un conflicto `412`.

El [análisis de impacto de octubre](docs/superpowers/plans/2026-10-19-categorias-v1-fecha-inicio.md) es un antecedente histórico. Los contratos vigentes están en [SPECIFICATION.md](SPECIFICATION.md), y su evidencia integrada en la matriz V1.

### Cumplimientos

* Marcar hábitos como realizados.
* Consultar el historial de cumplimientos.
* Evitar registros duplicados para una misma fecha.

### Rachas

* Racha general del usuario.
* Racha individual por hábito.
* Regla general de cumplimiento del 70%.
* Días sin hábitos programados considerados neutros.

### Estadísticas y progreso

* Estadísticas generales.
* Estadísticas individuales por hábito.
* Historial de progreso.
* Calendario mensual de consulta e historial por hábito, con oportunidad y registro separados.
* Resúmenes generales y por hábito de los últimos 30 días civiles, con estados parciales y reintentos por sección.

---

## Stack tecnológico

### Backend

* C#
* .NET 10
* ASP.NET Core Web API
* Entity Framework Core 10
* SQL Server 2022

### Frontend

* Blazor

### Testing

* xUnit

### Herramientas

* Git
* GitHub
* Docker
* Visual Studio Code / Codex

---

## Arquitectura

CONSTIA utiliza una arquitectura por capas con separación de responsabilidades:

```text
CONSTIA/
├── src/
│   ├── Constia.Web
│   ├── Constia.API
│   ├── Constia.Application
│   ├── Constia.Domain
│   └── Constia.Infrastructure
├── tests/
│   ├── Constia.Tests
│   ├── Constia.API.Tests
│   ├── Constia.Web.Tests
│   └── Constia.EnvioUnico.Tests
└── tools/
    ├── Constia.StateHistoryBackfill
    └── Constia.EnvioUnico
```

De manera general:

```text
Constia.Web ── HTTP ──> Constia.API
Constia.API ──> Constia.Application, Constia.Infrastructure
Constia.Application ──> Constia.Domain
Constia.Infrastructure ──> Constia.Application, Constia.Domain
Constia.Infrastructure ── persistencia ──> SQL Server
```

La explicación detallada de responsabilidades y dependencias se encuentra en [`ARCHITECTURE.md`](ARCHITECTURE.md).

---

## Documentación

| Documento                              | Descripción                                            |
| -------------------------------------- | ------------------------------------------------------ |
| [`AGENTS.md`](AGENTS.md)               | Reglas e instrucciones para trabajar sobre el proyecto |
| [`MVP-SCOPE.md`](MVP-SCOPE.md)         | Alcance funcional del MVP                              |
| [`SPECIFICATION.md`](SPECIFICATION.md) | Reglas y comportamiento funcional de V1                |
| [`ARCHITECTURE.md`](ARCHITECTURE.md)   | Arquitectura y responsabilidades técnicas              |
| [`DEVELOPMENT.md`](DEVELOPMENT.md) | Instalación local, migraciones, TEMP-005 y límites operativos |
| [Aceptación V1](docs/releases/v0.1.0-aceptacion.md) | Criterios, evidencia y pendientes de aprobación |
| [Notas v0.1.0](docs/releases/v0.1.0.md) | Alcance de la versión de código y límites |

---

## Principios del proyecto

CONSTIA se desarrolla siguiendo algunos principios:

**Simplicidad**
Implementar lo necesario sin agregar complejidad innecesaria.

**Separación de responsabilidades**
Cada proyecto y capa debe tener una responsabilidad clara.

**Evolución incremental**
Construir el MVP paso a paso antes de incorporar funcionalidades futuras.

**Código comprensible**
La implementación debe ser entendible y justificable, evitando generar código que no pueda ser explicado.

**Uso responsable de IA**
Las herramientas de IA, incluido Codex, se utilizan como asistentes de desarrollo. Las decisiones técnicas deben ser comprendidas y revisadas antes de incorporarse al proyecto.

---

## Futuras versiones

Las siguientes funcionalidades quedan fuera del MVP actual y podrán evaluarse posteriormente:

* Recordatorios.
* Objetivos.
* Categorías personalizadas (el catálogo oficial de diez categorías sí está incluido en V1).
* Estadísticas avanzadas.
* Gamificación.
* Insignias.
* Niveles.
* Notificaciones.
* Aplicación móvil nativa.

---

## Objetivo del proyecto

CONSTIA no busca únicamente convertirse en una aplicación funcional.

También es un proyecto de aprendizaje y portfolio orientado a profundizar conocimientos en:

* desarrollo con .NET;
* diseño de APIs;
* arquitectura de software;
* Entity Framework Core;
* SQL Server;
* testing;
* Docker;
* desarrollo frontend con Blazor;
* Git y GitHub;
* desarrollo asistido por inteligencia artificial.

El proyecto se construye de forma incremental, manteniendo trazabilidad entre requerimientos, código, pruebas y commits.

## Base documental AUD-BASE · 19/10/2026

La reconciliación partió de main remoto `30058bbcd90e8f6a3733784f6826470eb67989d4`, con COMP-005 y TEMP-005 integrados. Conservó sus reglas temporales, incluida la ventana de cumplimientos de hoy o ayer. PEN-006 tenía aprobación visual estática para diez categorías y dieciséis íconos, que no acreditaba implementación funcional. En aquella etapa solo se preparó Git y documentación. Las entregas posteriores están integradas; consultar la matriz V1 para el estado vigente.
