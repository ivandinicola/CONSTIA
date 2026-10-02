# CONSTIA

### Tu constancia, día a día.

CONSTIA es una aplicación de seguimiento de hábitos diseñada para ayudar a las personas a construir constancia mediante pequeños objetivos repetidos en el tiempo.

La aplicación permite definir hábitos, elegir los días en los que deben realizarse, registrar cumplimientos y visualizar el progreso mediante rachas, estadísticas e historial.

---

## Estado del proyecto

**Versión:** 0.1.0 — MVP en desarrollo

CONSTIA se encuentra actualmente en etapa de desarrollo.

El alcance funcional de la primera versión está definido en:

* [`MVP-SCOPE.md`](MVP-SCOPE.md) — alcance del MVP.
* [`SPECIFICATION.md`](SPECIFICATION.md) — especificación funcional.
* [`ARCHITECTURE.md`](ARCHITECTURE.md) — arquitectura técnica.

---

## Funcionalidades del MVP

### Usuarios

* Registro de usuario.
* Inicio de sesión.
* Autenticación.
* Acceso aislado a los datos propios de cada usuario.

### Hábitos

* Crear hábitos.
* Editar hábitos.
* Activar y desactivar hábitos.
* Consultar hábitos activos.
* Consultar hábitos inactivos.
* Definir los días de la semana en los que se realiza cada hábito.

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
* Visualización mediante calendario/heatmap.

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
└── tests/
    └── Constia.Tests
```

De manera general:

```text
Constia.Web
      │
      │ HTTP
      ▼
Constia.API
      │
      ▼
Constia.Application
      │
      ▼
Constia.Domain
      │
      ▼
Constia.Infrastructure
      │
      ▼
   SQL Server
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
| `DEVELOPMENT.md`                       | Guía para configurar y ejecutar el proyecto            |

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
* Categorías.
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
