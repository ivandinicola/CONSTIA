# CAT-02-R1 — Propuesta de validación SQL desechable

Estado: propuesta para autorización; no ejecutada. No modifica ci.yml, Docker ni la fixture de aislamiento actual.

## Hechos comprobados

- CI tiene un job build-and-test con SQL Server 2022 en el puerto fijo 1433. TEMP-003/004/005 y cumplimientos lo utilizan, cada suite con su opt-in.
- CategoriaHabitoSqlServerTests tiene cinco Facts: tres originales y dos de rollback añadidos en R1.
- La fixture CAT-02 actual requiere GITHUB_ACTIONS=true, CONSTIA_CAT02_SQLSERVER_TESTS=true y una conexión a master con sa en 127.0.0.1,1433. Crea una base Constia_Cat02_<UUID> por test y la elimina al finalizar.
- Ese opt-in y puerto fijo no identifican una instancia desechable: no habilitarlo localmente ni reutilizar la instancia de desarrollo. Tampoco basta con simular GITHUB_ACTIONS.
- Ningún test SQL CAT-02 se ejecutó. La verificación de Down sigue siendo estática.

## Ejecución propuesta (requiere autorización adicional)

1. Agregar un job independiente cat02-sqlserver en runner ubuntu de GitHub hospedado, con checkout y SDK iguales al job actual. No editar los pasos, filtros ni conteos de TEMP/COMP. El job actual podrá descubrir CAT-02 como omitido por falta de su opt-in; la validación obligatoria pertenecerá al job separado.
2. Restaurar y compilar Release. No usar secretos ni conexiones de desarrollo. Elegir una imagen oficial mcr.microsoft.com/mssql/server:2022-latest, resolver su digest y registrar el digest utilizado para reproducibilidad. Comprobar la versión mayor 16 al conectar. No introducir dependencias de test nuevas.
3. Antes de orquestar, exigir el daemon Docker local del runner hospedado: contexto local y socket Unix local, sin DOCKER_HOST remoto, contexto SSH/TCP ni daemon compartido. Si no se puede comprobar, abortar. Un paso de orquestación debe generar un nonce, nombre/hostname únicos cat02-<nonce>, una etiqueta com.constia.cat02.run=<nonce> y contraseña sa aleatoria nueva. Enmascarar la contraseña antes de utilizarla; pasarla por entorno, nunca en argumentos visibles ni en el informe. No reutilizar la contraseña del servicio TEMP.
4. Crear un contenedor NUEVO mediante docker run --detach --rm, MSSQL_PID=Developer, ACCEPT_EULA=Y y publicación 127.0.0.1::1433. No montar volúmenes del host, datos, volúmenes nombrados, redes de desarrollo ni socket Docker dentro del contenedor. El almacenamiento queda en la capa desechable del contenedor. Guardar el ID exacto devuelto; abortar si falla la creación, sin buscar otro servidor ni usar un fallback.
5. Leer docker inspect del ID recién creado: debe estar running, usar el image ID/digest oficial resuelto, tener hostname y etiqueta del nonce, Mounts vacío y publicar 1433/tcp solo en loopback con un puerto asignado distinto de 1433. Obtener ese puerto de inspect, sin reservarlo previamente ni intentar liberar puertos existentes. Si falla cualquier condición, abortar. Los servicios actuales no se tocan.
6. Guardar un manifiesto efímero en RUNNER_TEMP (permisos del usuario, sin credenciales): container ID, image ID/digest, nonce, hostname y puerto. Esperar disponibilidad con timeout acotado y credencial aleatoria; validar mediante SELECT en master SERVERPROPERTY('MachineName') = hostname, ProductMajorVersion = 16 y DB_NAME() = master. Esta comprobación de lectura debe ocurrir ANTES de cualquier CREATE DATABASE.
7. Endurecer la fixture CAT-02 EXISTENTE antes de ejecutar: requerir el manifiesto; volver a cotejarlo mediante docker inspect y comprobar identidad SQL antes de crear cada base. Construir la conexión desde el destino verificado y la credencial efímera; no aceptar destinos suministrados libremente, alias, Failover Partner, AttachDBFilename ni redirecciones de conexión. No considerar GITHUB_ACTIONS una frontera de seguridad. Sin manifiesto, contenedor o identidad coincidente, fallar antes de CREATE DATABASE. Probar estas negativas sin SQL. Reemplazar el chequeo rígido del puerto actual por el puerto dinámico comprobado. No crear infraestructura genérica ni cambiar fixtures TEMP/COMP.
8. Habilitar exclusivamente el opt-in CAT-02 en el paso de pruebas. Cada Fact reutiliza la fixture y crea su base aleatoria; migraciones y escrituras ocurren solo allí. No ejecutar scripts sobre bases habituales ni habilitar opt-ins de otras suites.

Comando de prueba propuesto en la raíz del repo (después del endurecimiento y verificación del contenedor):

```bash
dotnet test tests/Constia.API.Tests/Constia.API.Tests.csproj \
  --no-build --no-restore --configuration Release \
  --filter 'FullyQualifiedName~Constia.API.Tests.CategoriaHabitoSqlServerTests' \
  --logger 'trx;LogFileName=cat02-sqlserver.trx' \
  --results-directory "$RUNNER_TEMP/cat02-results"
```

## Validación TRX obligatoria

No aceptar solo exit code 0. El conjunto exacto esperado es:

1. BaseVacia_PrecargaExactaYMaterializacionEfConConstructorExistente
2. BaseHistorica_PreservaHabitosFechasProgramacionesPeriodosCumplimientosYSnapshot
3. Fk_PermiteOficialONullRechazaDesconocidaYNoEliminaHabitoEnCascada
4. Down_SinAsignacionesRevierteSoloCategoriaYPreservaDatosHistoricos
5. Down_ConAsignacionRechazaSinPerdidaParcialNiCambiosDeEsquema

Usar el parser XML de Python ya disponible en CI. Relacionar cada UnitTestResult.testId con TestDefinitions/UnitTest/TestMethod; exigir la clase Constia.API.Tests.CategoriaHabitoSqlServerTests y exactamente ese conjunto de cinco métodos únicos (sin duplicados ni resultados de otras clases). Exigir cinco UnitTestResult, todos Passed, y ResultSummary/Counters con total=5, executed=5, passed=5, failed=0, error=0 y notExecuted=0. Comprobar cualquier otro contador de resultados fallidos/abortados si se informa; debe ser cero. Faltar el TRX, omitir un test, fallar, abortar o descubrir cero tests hace fallar el job.

Resultado de aceptación: Passed: 5, Failed: 0, Skipped: 0. Adjuntar el TRX como artefacto aun si el job falla, sin conexiones ni variables secretas.

## Limpieza

- Cada await using ejecuta la eliminación de su base validando nombre y destino. Al endurecer la fixture, sustituir ClearAllPools por limpieza del pool de SU conexión para no afectar otras suites.
- Registrar un trap desde la creación y un paso final always() que eliminen únicamente el ID de contenedor creado por este job, cotejando su etiqueta/nonce antes de docker rm --force. No usar prune, filtros amplios, docker compose down ni detener servicios ajenos.
- Eliminar el manifiesto y temporales de credenciales al finalizar. La destrucción del contenedor elimina cualquier base residual aunque el proceso de pruebas se interrumpa; el runner hospedado también es efímero.
- Antes de declarar éxito exigir resultado de pruebas, chequeo TRX y limpieza exitosa. No borrar recursos por nombre ambiguo ni reutilizar bases preexistentes.

## Cambios sujetos a otra autorización

- Agregar el job independiente y subida de artefactos en .github/workflows/ci.yml.
- Implementar el paso de orquestación/atestación y el endurecimiento puntual de la fixture existente; añadir sus negativas de seguridad.
- Crear y destruir exclusivamente el nuevo contenedor desechable y ejecutar sus cinco pruebas SQL.

Hasta entonces CAT-02 sigue pendiente de validación SQL real: materialización, FK, preservación de datos y rollback no están certificados en SQL Server.

## Fuentes oficiales consultadas

- GitHub Actions, contenedores y puertos dinámicos: https://docs.github.com/en/actions/tutorials/use-containerized-services/use-docker-service-containers
- SQL Server Linux containers: https://learn.microsoft.com/en-us/sql/linux/containers/troubleshoot
- SQL Server table hints y duración de bloqueos: https://learn.microsoft.com/en-us/sql/t-sql/queries/hints-transact-sql-table

## CAT-02-R2 — Preparación autorizada, sin ejecución

El 19/10/2026 se prepararon .github/scripts/cat02_sql_runner.py y su suite pura, el job independiente cat02-sqlserver y el endurecimiento exclusivo de la fixture CAT-02. La propuesta anterior se conserva como antecedente. Esta sección registra lo implementado; no certifica Docker, SQL ni GitHub Actions en ejecución.

- Se preservan los jobs, servicios, fixtures y conteos TEMP/COMP. El único cambio en el job histórico es excluir explícitamente CategoriaHabitoSqlServerTests del paso general.
- El runner exige Linux github-hosted, el job correcto, run ID/attempt, contexto Docker local y ausencia de conexiones arbitrarias. Crea un contenedor nuevo con nonce completo en etiquetas, hostname de 15 caracteres, contraseña temporal enmascarada, digest oficial resuelto y puerto loopback dinámico distinto de 1433.
- La fixture específica Cat02SqlRunnerIdentity no es infraestructura genérica ni se comparte con otras suites. Exige manifiesto bajo RUNNER_TEMP, identidad de ejecución, comprobación Docker de contenedor/imagen/etiquetas/montajes/puertos, y MachineName, versión 16 y master mediante lectura SQL antes de CREATE DATABASE. No lee User Secrets ni la conexión habitual.
- El puerto solicitado vacío/0 se distingue del efectivo asignado por Docker. El manifiesto se guarda por reemplazo atómico para conservar ownership ante interrupciones. El TRX bruto queda privado; se publica solo su copia saneada. No se suben inspect, manifiestos ni variables de entorno.
- Cada base es aleatoria y la limpieza comprueba de nuevo su destino. Se limpia únicamente su pool. El runner usa finally, manejo de SIGINT/SIGTERM y un paso always() que elimina únicamente el contenedor de su ejecución después de comprobar sus etiquetas. Si hay SIGKILL, pérdida del host o fallo del daemon, no se garantiza limpieza inmediata: la segunda limpieza y la destrucción del runner hospedado son las defensas finales. No se toca ningún contenedor habitual.
- El parser TRX exige cinco métodos únicos de la clase CAT-02, exactamente el conjunto de la propuesta, todos Passed; total=5, executed=5, passed=5, failed=0, error=0, notExecuted=0 y demás contadores de fallo a cero. No se acepta solo el exit code.

### Tratamiento de fallos previsto

| Escenario | Respuesta preparada |
|---|---|
| No se crea el contenedor | El comando falla; no se usan servidores alternativos. Limpieza con ownership previo y CID/nombre único validado si llegó a crearse. |
| Motor no listo | Timeout acotado; ninguna prueba ni base se inicia; finally/always limpian el contenedor propio. |
| Conexión a otra instancia | No se acepta una cadena externa; se construye desde el puerto verificado y se exige identidad SQL antes de cualquier escritura. |
| Falta identificador/manifiesto | No se habilita la suite o falla antes de CREATE DATABASE. En CI cualquier skip/incompletitud rechaza el TRX. |
| ID/identidad discrepante | Rechazo antes de CREATE DATABASE; no se borra un contenedor ajeno. |
| Base administrativa incorrecta | El builder fija master y la consulta exige DB_NAME()=master; destinos de aplicación/variables externas prohibidos. |
| Falla migración | Falla su test; await using intenta limpiar la base y finally/always eliminan el contenedor. TRX conserva el fallo saneado. |
| Falla un test | Exit code o TRX no válido hace fallar el job, incluso si el otro pareciera correcto. |
| Una prueba omitida | Cinco Passed exactos son obligatorios; una omisión se rechaza. |
| Interrupción | Señales manejables pasan por finally; ownership atómico permite limpieza posterior. Terminación forzada depende de always/teardown del runner. |

### Verificación disponible y pendientes

Las pruebas de seguridad de C# y Python no ejecutan Docker ni SQL. La revisión independiente encontró y permitió corregir el manejo del puerto solicitado frente al efectivo y la escritura atómica de ownership. No se instalaron herramientas. El entorno local no dispone de actionlint ni parser YAML; se revisaron estructura, indentación y preservación textual del workflow, además de sintaxis Python y pruebas puras. Esto no sustituye la validación del workflow por GitHub.

Las cinco pruebas SQL continúan pendientes. Crear contenedores, ejecutar el job/SQL, hacer commits o push requieren otra autorización. No se modificaron Up, Down, Designer, snapshot, catálogo ni modelos en R2.

Fuentes oficiales adicionales consultadas:
- Contexto Docker local: https://docs.docker.com/engine/manage-resources/contexts
- Publicación de puertos: https://docs.docker.com/reference/cli/docker/container/run
- SQL Server en contenedores: https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-docker-container-deployment
- Acción de artefactos y uso vigente: https://github.com/actions/upload-artifact
