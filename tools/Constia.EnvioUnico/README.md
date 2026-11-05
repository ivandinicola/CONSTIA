# Envío único controlado de CONSTIA

La simulación sigue siendo el modo predeterminado. El modo real solo se selecciona con
el argumento inicial `--real`, seguido de ensayo GUID, operación GUID y destinatario exacto.
No hay activación por variables de entorno ni fallback de SQL real a autorización simulada.
Esta implementación no constituye autorización operativa para enviar un correo.

## Instalación y admisión
El archivo `constia-envio-unico.instalacion.json` debe existir junto al ejecutable y declarar
InstalacionId y Raiz. La aplicación no lo crea ni busca otra raíz. La raíz debe ser absoluta,
canónica, local, NTFS en unidad fija y preexistente, con nombre `Constia-C127-<identificador>`.
Se rechazan UNC, unidades remotas y reparse points en la raíz o sus antecesores.
Custodiar una sola instalación autoritativa, su configuración y todos sus directorios.
La comprobación de rutas no elimina carreras de sustitución ni protege frente a un administrador.

La simulación usa AutorizacionSimulada, TransporteFalso y una clave efímera; no abre SQL ni HTTP.
Anuncia LISTO y acepta INICIAR por stdin dentro de 30 segundos.
El modo real anuncia LISTO REAL con instalación, ensayo y operación; requiere la señal distinta
ENVIAR-UNA-VEZ dentro de 30 segundos. No proporcionar esa señal sin autorización expresa.
Una selección o configuración inválida no crea el marcador de inicio.

## Configuración del modo real
Variables del proceso, inyectadas mediante un mecanismo local protegido. No usar secretos
en argumentos CLI, archivos del repositorio, capturas, logs, evidencias o historial de comandos.

| Variable | Contrato |
|---|---|
| CONSTIA_ENVIO_INSTALACION_ID | GUID idéntico al archivo de instalación |
| CONSTIA_ENVIO_DESTINATARIO | Dirección exactamente igual al destinatario CLI y al preparado |
| CONSTIA_ENVIO_SQL | Conexión a la base autorizada con C109 ya instalado, nunca a una base del sistema |
| CONSTIA_ENVIO_HMAC_BASE64 | La misma clave HMAC de al menos 32 bytes empleada para preparar la autorización |
| Resend__Enabled | Habilitación explícita del transporte, además de seleccionar el modo real |
| Resend__ApiKey | Credencial exclusiva y protegida, nunca impresa |
| Resend__FromAddress | Remitente autorizado en Resend |
| Resend__FromName | CONSTIA; es el valor predeterminado |
| Resend__RequestTimeoutSeconds | Entre 1 y 30 segundos; predeterminado 10 |

La conexión SQL debe declarar servidor y base; su timeout de conexión debe estar entre
1 y 30 segundos. No se modifican políticas TLS ni se omite validación de certificados:
configurar una conexión adecuada al entorno. Se desactivan los reintentos de conexión SQL,
no se activa EnableRetryOnFailure y cada comando SQL tiene límite de 15 segundos.
No se requiere PublicOrigin para este correo técnico sin enlaces.

## Preparación independiente de C109
Un operador autorizado debe preparar separadamente una única fila Preparada con EnsayoId,
OperacionId e identidad HMAC del destinatario exacto. No usar otra clave al ejecutar.
El ejecutable no crea bases, aplica migraciones, inserta filas o rearma autorizaciones.
El consumidor debe tener los permisos mínimos para consultar y actualizar el estado,
sin permisos administrativos, creación de autorizaciones o borrado de historial.
La barrera C135 es un fixture de tests; no se presume instalada por esta aplicación.

Acordar previamente remitente, destinatario, identificadores y contenido técnico.
El payload real contiene from, un único to, asunto fijo y texto fijo con OperacionId.
La clave Idempotency-Key es la operación en formato N. El contenido y la identidad
se vuelven a validar antes del inicio. Nunca reutilizar identificadores ni cambiar
destinatario para repetir un intento.

## Secuencia y cierre
1. Validación de configuración, instalación y contenido.
2. CreateNew exclusivo de <EnsayoId>.inicio, WriteThrough y Flush(true).
3. Consumo condicional C109 y confirmación del commit.
4. A lo sumo una invocación de ResendHttpTransport; sin workers, outboxes o reintentos.
5. Registro terminal con un token independiente limitado a cinco segundos.
6. Salida con estado, motivo, HTTP, ID de proveedor y confirmación de registro, sin secretos.

Incluso un archivo vacío o parcial bloquea el ensayo. No borrar marcadores tras fallo.
Un consumo fallido o ambiguo impide HTTP. Un timeout o fallo posterior no rearma SQL.
Aceptación válida se registra como Aceptada. Los demás resultados se registran
conservadoramente como Incierta, conservando su clasificación exacta en la salida.
Si Resend aceptó pero falló el registro, la salida conserva esa aceptación y declara
el fallo de persistencia por separado. Ningún resultado autoriza reenvío.
Aceptación por Resend no acredita recepción en el buzón.
Código 0 real requiere aceptación y registro confirmado; 1 representa otro resultado
controlado y 2 una entrada, admisión o preparación interrumpida. Inspeccionar siempre
el resultado completo, no solamente el código.

## Límites
No hay garantía de entrega exactamente una vez. La idempotencia del proveedor complementa
el consumo SQL y el registro local; no reemplaza su custodia.
El registro debe permanecer fuera de restauraciones SQL. Restaurar la base, perder/borrar
la raíz, copiar la instalación o usar otro host exige reconciliación externa antes de continuar.
No protege frente a administradores SQL, modificaciones directas o almacenamiento defectuoso.
Una interrupción brusca posterior al consumo puede dejar Consumida sin resultado terminal;
eso bloquea nuevos intentos y exige investigación, nunca rearme automático.

## Pruebas
Los tests del ejecutable usan credenciales sintéticas, dobles de autorización y HTTP interceptado.
La caracterización de composición real construye el adaptador C109 sin conectarlo y prueba
el transporte directamente contra un handler en memoria; no consume autorizaciones SQL.
Los tests de identidad alterada cierran el coordinador antes de acceder a SQL o HTTP.
Las pruebas multiproceso siguen siendo exclusivamente simuladas.
El cleanup de tests borra únicamente directorios temporales propios. No probar con raíces históricas.
No ampliar dependencias, tocar políticas de Windows ni usar claves reales para validar esta suite.
