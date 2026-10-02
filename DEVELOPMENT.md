# CONSTIA — Development Guide

## 1. Propósito

Este documento explica cómo preparar el entorno de desarrollo de CONSTIA, ejecutar sus servicios y trabajar con el proyecto localmente.

Debe mantenerse actualizado a medida que evolucione el proyecto.

---

## 2. Requisitos

Para desarrollar CONSTIA se necesitan:

* Windows.
* .NET SDK 10.
* Git.
* Docker Desktop.
* Docker con soporte para WSL 2.
* SQL Server 2022 mediante Docker.
* Un editor compatible con el proyecto, como Visual Studio Code o Codex.

### Tecnologías principales

* C#.
* .NET 10.
* ASP.NET Core Web API.
* Entity Framework Core 10.
* SQL Server 2022.
* Blazor.
* xUnit.
* Git.
* Docker.

---

## 3. Preparación del entorno

Antes de trabajar en CONSTIA, verificar que las herramientas principales estén disponibles.

### .NET

```powershell
dotnet --version
```

La versión utilizada por el proyecto debe corresponder a .NET 10.

### Git

```powershell
git --version
```

### Entity Framework Core

```powershell
dotnet ef --version
```

### Docker

```powershell
docker --version
docker run hello-world
```

Docker Desktop debe estar ejecutándose.

---

## 4. SQL Server de desarrollo

CONSTIA utiliza SQL Server 2022 para desarrollo local.

La base de datos se ejecuta mediante Docker.

### Contenedor

Nombre previsto:

```text
habittracker-sqlserver
```

### Puerto

```text
1433
```

### Imagen

```text
mcr.microsoft.com/mssql/server:2022-latest
```

### Volumen

```text
habittracker-sql-data
```

El volumen permite conservar los datos aunque el contenedor sea detenido o eliminado.

### Verificar el contenedor

```powershell
docker ps
```

El contenedor debe aparecer como activo.

### Verificar los logs

```powershell
docker logs habittracker-sqlserver
```

SQL Server debe indicar que está listo para aceptar conexiones.

---

## 5. Credenciales y secretos

Las credenciales de desarrollo no deben almacenarse directamente en el repositorio.

No deben incluirse en:

* Código fuente.
* `README.md`.
* `DEVELOPMENT.md`.
* `appsettings.json` versionado.
* Commits.
* Issues.
* Documentación pública.

Los secretos deberán gestionarse mediante mecanismos apropiados de configuración de desarrollo.

La contraseña utilizada actualmente para el contenedor local de SQL Server existe únicamente como configuración de desarrollo y no forma parte de este repositorio.

---

## 6. Estructura esperada

La solución tendrá una estructura similar a:

```text
CONSTIA/
├── AGENTS.md
├── README.md
├── MVP-SCOPE.md
├── SPECIFICATION.md
├── ARCHITECTURE.md
├── DEVELOPMENT.md
├── .gitignore
│
├── src/
│   ├── Constia.Web/
│   ├── Constia.API/
│   ├── Constia.Application/
│   ├── Constia.Domain/
│   └── Constia.Infrastructure/
│
└── tests/
    └── Constia.Tests/
```

---

## 7. Restaurar dependencias

Una vez creada la solución, desde la raíz del repositorio:

```powershell
dotnet restore
```

Este comando restaura los paquetes necesarios para los proyectos de la solución.

---

## 8. Compilar

Para compilar toda la solución:

```powershell
dotnet build
```

La solución deberá compilar correctamente antes de considerar completada una tarea de desarrollo.

---

## 9. Ejecutar pruebas

Para ejecutar los tests:

```powershell
dotnet test
```

Las pruebas deben ejecutarse después de cambios relevantes, especialmente cuando se modifican reglas de dominio, casos de uso o funcionalidades existentes.

---

## 10. Entity Framework Core

Las migraciones de base de datos serán administradas mediante Entity Framework Core.

Comandos habituales:

```powershell
dotnet ef migrations add <NombreMigracion>
```

y:

```powershell
dotnet ef database update
```

Las migraciones deberán representar cambios intencionales en el modelo y mantenerse versionadas junto con el código.

No se deberán modificar manualmente las tablas de la base de datos para reemplazar el flujo normal de migraciones durante el desarrollo.

---

## 11. Ejecución de la aplicación

Los comandos concretos para ejecutar `Constia.API` y `Constia.Web` se documentarán una vez creados los proyectos.

La ejecución local deberá permitir levantar:

```text
Constia.Web
     │
     │ HTTP
     ▼
Constia.API
     │
     ▼
SQL Server
```

---

## 12. Flujo de trabajo

El desarrollo de CONSTIA utiliza un flujo incremental.

### 1. Seleccionar una tarea

Elegir una tarjeta de Trello ubicada en `HOY`.

La tarea debe estar claramente definida y relacionada con un identificador, por ejemplo:

```text
HAB-006
```

### 2. Analizar

Antes de modificar el código:

* Leer la descripción de la tarea.
* Revisar `SPECIFICATION.md` cuando corresponda.
* Revisar `ARCHITECTURE.md` si afecta estructura o responsabilidades.
* Entender qué comportamiento se espera.

### 3. Implementar

Realizar únicamente los cambios necesarios para completar la tarea.

Evitar implementar funcionalidades futuras que todavía no formen parte del MVP.

### 4. Verificar

Ejecutar las comprobaciones correspondientes:

```powershell
dotnet build
dotnet test
```

o las pruebas específicas que correspondan.

### 5. Commit

Cada commit relacionado con una tarea deberá incluir su identificador de Trello.

Ejemplo:

```text
feat(HAB-006): create habit
```

### 6. Revisión

Comprobar que:

* El comportamiento cumple la especificación.
* Los tests pasan.
* No existen cambios innecesarios.
* No se introdujeron secretos.
* La arquitectura continúa respetándose.

### 7. Finalizar

Una vez verificada la tarea, mover la tarjeta correspondiente de Trello a `HECHO`.

---

## 13. Convenciones de Git

Los commits deben ser pequeños y representar cambios relacionados.

Formato recomendado:

```text
tipo(ID): descripción
```

Ejemplos:

```text
chore(SETUP-001): initialize Constia solution
feat(USR-001): add user domain
feat(HAB-006): create habit
test(STR-008): test 70 percent rule
docs(ARCH-001): update architecture
```

Tipos habituales:

* `feat` — nueva funcionalidad.
* `fix` — corrección de un error.
* `test` — pruebas.
* `refactor` — reorganización sin cambiar el comportamiento esperado.
* `docs` — documentación.
* `chore` — tareas de configuración o mantenimiento.

---

## 14. GitHub

El repositorio remoto de CONSTIA se almacenará en GitHub.

Flujo general:

```text
Trabajo local
     ↓
git add
     ↓
git commit
     ↓
git push
     ↓
GitHub
```

Los archivos generados temporalmente por herramientas de desarrollo no deberán versionarse.

El archivo `.gitignore` deberá evitar que archivos innecesarios o sensibles sean incluidos en Git.

---

## 15. Cambios de este documento

Este documento debe actualizarse cuando cambie alguno de los siguientes aspectos:

* Requisitos de desarrollo.
* Herramientas utilizadas.
* Forma de ejecutar el proyecto.
* Configuración de Docker.
* Configuración de SQL Server.
* Flujo de migraciones.
* Comandos necesarios para tests o compilación.
* Flujo de Git.
* Estructura necesaria para desarrollar CONSTIA.

La información aquí documentada debe representar el procedimiento real utilizado para trabajar en el proyecto.
