# Backend de Seyra

OpenCode v2 sirve el chat y administra sesiones, modelos y el [MCP de escena Unity](bridge/README.md). Incluye exploración, contenedores por petición, primitivas, Transform, eliminación recuperable, deshacer y persistencia del contenido generado. Managed text panels and spatial labels are now available through complete contracts; see [panels and persistence v4](bridge/PANELS.md). Twelve complete semantic presentations are available; see [presentation contracts](bridge/PRESENTATIONS.md). Captured room inventory and room/viewer placement frames are available through [the spatial contract](bridge/SPATIAL.md); real-device room validation remains pending. Camera captures remain pending.

## Arranque

Requiere Node.js 22 o posterior, npm y opencode2 en PATH. Desde la raíz del repositorio:

```powershell
npm ci --prefix Backend/bridge
node Backend/seyra.mjs start
```

seyra.mjs start carga .env.local como datos, compila el bridge y arranca OpenCode en http://127.0.0.1:4096. OpenCode inicia el bridge por stdio; Unity conecta por WebSocket a localhost:4100. No arranques el bridge por separado durante el uso normal. Reinicia OpenCode tras modificar catálogo/configuración y reconecta el chat para crear una sesión nueva.

.env.local está excluido de Git y requiere META_API_KEY (Meta Model API, dev.meta.ai) y OPENCODE_SERVER_PASSWORD. Añade tú la clave al archivo local del PC. Si solo existe MODEL_API_KEY, seyra.mjs start la copia a META_API_KEY en el proceso. La contraseña HTTP es distinta de las claves de proveedores. El script fija también OPENCODE_PASSWORD al mismo valor, evitando discrepancias con variables heredadas. No copies credenciales a Assets ni las publiques.

La opción `node Backend/seyra.mjs start --port 4097` cambia solo el puerto HTTP: no permite ejecutar dos bridges en 4100. Si existe otra instancia/servicio OpenCode, detén esa instancia antes de iniciar la nueva o configura otro puerto WebSocket en ambos extremos. La CLI resuelve las rutas del MCP mediante `SEYRA_BACKEND_DIR` y las salidas truncadas mediante `SEYRA_OPENCODE_DATA_DIR`; mover el repositorio no requiere editar opencode.json.

## Perfil y permisos

- seyra hereda meta/muse-spark-1.3 (Muse Spark) mediante Meta Model API, autenticado por META_API_KEY. Se declara el proveedor Responses explícitamente porque el catálogo integrado de opencode2 2.1.1 no lo expone en este entorno. Se conserva el esfuerzo de razonamiento por defecto, sin modo Contributor.

Las instrucciones viven en [prompts/seyra.md](prompts/seyra.md); el campo `system` del perfil contiene solo `{file:./prompts/seyra.md}`, que OpenCode expande al cargar la configuración (ruta relativa a opencode.json). Para cambiar el comportamiento del agente se edita ese archivo y se reinicia OpenCode. Evita `opencode2 debug config` durante el servicio: puede iniciar otro bridge en 4100. El perfil usa una lista blanca: la primera regla `* deny` oculta al modelo cualquier herramienta no autorizada, incluidas las heredadas de la configuración global de OpenCode (MCP como `metavr`, Code Mode `execute`, `shell`, `question` y skills). Se permiten `seyra_*`, `websearch`, `webfetch`, y `read`/`grep` para recuperar salidas truncadas; los archivos `*.env` y `*.env.*` quedan denegados. La única excepción de `external_directory` permite recuperar salidas truncadas en `{env:SEYRA_OPENCODE_DATA_DIR}/tool-output/*`; no se concede acceso general fuera del directorio de sesión. Añadir una herramienta no `seyra_*` requiere modificar `opencode.json`, el test de permisos y este apartado. No se añaden confirmaciones para el MCP de Unity. Los argumentos se validan en el bridge y en el runtime; el éxito solo se comunica tras confirmación del runtime.

El chat consulta `/api/agent` al conectar y envía explícitamente el modelo declarado por el perfil al crear la sesión. El perfil `seyra`, sin modelo propio, hereda el valor global.

## Unity

SeyraAssistant.unity y OpencodeTestUI.unity contienen SeyraRuntime con el componente SeyraSceneRuntime y un contenedor raíz Generated asignado en el Inspector. Solo un runtime puede poseer el bridge; al cargar ambas escenas aditivamente, el segundo se desactiva. Las operaciones son de Play Mode, en memoria: no guardan cambios en los assets de escena.

Para usar el chat, abre Assets/Scenes/OpencodeTestUI.unity en el proyecto nuevo y entra en Play Mode. El verde confirma conexión con OpenCode, no la conexión del MCP con Unity. Si el runtime no está disponible, las herramientas devuelven UNITY_NOT_CONNECTED.

Si OpenCode devuelve una petición de permiso, el chat muestra su acción y los detalles completos como texto literal en un panel desplazable. «Permitir una vez» responde `once`; «Rechazar» responde `reject` y OpenCode rechaza también los demás permisos pendientes de esa sesión. Las decisiones se envían solo al pulsar el botón y no se reintentan automáticamente, ni vuelven a habilitarse al reconectar si su respuesta se perdió. La espera de la decisión pausa el presupuesto de inactividad del turno; después se sigue leyendo el mismo turno, sin reenviar el mensaje. Este panel atiende solicitudes del servidor y no cambia la regla `seyra_*: allow`. Cabecera, controles del turno, conversación, panel y compositor usan layouts.

El estado de ejecución muestra envío, ejecución, espera de permiso, cancelación, finalización, fallo y resultado desconocido. Los mensajes y las herramientas aparecen durante el turno; cada fila se actualiza por su ID, sin mostrar razonamiento interno ni volcar argumentos o respuestas arbitrarias de herramientas. «Cancelar» solicita `interrupt` al servidor: detiene las acciones siguientes de OpenCode, pero no deshace lo ya enviado o ejecutado. El resultado se confirma consultando el turno; si había finalizado antes, se muestra completado. Un turno interrumpido o fallido se cierra con «Cerrar turno» antes de enviar otro mensaje. Si quedó abierta una petición de contenido, puede consultarse y cerrarse explícitamente en el siguiente mensaje, sin deshacer sus objetos.

Reconectar inspecciona la sesión existente y conserva el turno y sus filas, también al desactivar/reactivar el componente. Un envío sin respuesta se busca entre los mensajes posteriores al límite previo y solo se identifica si hay una coincidencia única; nunca se reenvía. Tras 120 segundos sin progreso visible se muestra resultado desconocido y continúan las consultas. Una sesión desaparecida requiere cierre explícito antes de crear otra. La continuidad no sobrevive a destruir la instancia, recargar el dominio ni reiniciar la aplicación; la recuperación duradera se aborda en el roadmap 21. [Pruebas de continuidad, cancelación y transporte](../Progress/Completed/02-execution.md).

Prueba aislada de permisos: ejecuta `node Backend/bridge/test-support/permission-ui-server.mjs`, abre OpencodeTestUI en Play Mode y ejecuta `test-support/PermissionUiCheck.cs` con la entrada `PermissionUiCheck.Run` mediante `unity command script run`. Usa el puerto local 4199, sin proveedor ni herramientas reales. Comprueba aceptar, rechazar, dobles clics y continuación del turno. Detén el servidor y sal de Play Mode al terminar.

Ejemplos:

- «Lista toda la escena de forma jerárquica con sus IDs».
- «Busca Generated y consulta sus componentes».
- «Crea un contenedor Mesa dentro de Generated con posición local (0,1,1), rotación (0,0,0) y escala (1,1,1)».
- «Mueve ese contenedor a X=2 conservando el resto del Transform».
- «Crea una mesa de 1.2 × 0.8 m usando cubos dentro de un contenedor, con tablero a 0.75 m de altura».

Las primitivas se crean con `create_primitive` (cube, sphere, capsule, cylinder, plane, quad) y comparten padres, validaciones, GUID y edición de Transform con los objetos vacíos. La escala multiplica la malla nativa; por ejemplo, un plano es 10 × 10 antes de escalar. La herramienta devuelve su geometría para poder comprobar tamaños. Conserva collider nativo, sin Rigidbody, y utiliza el material compartido del render pipeline.

Los objetos gestionados usan entityId persistentes al guardar/cargar; los componentId y los IDs de objetos externos siguen siendo de runtime. Las creaciones exigen parentId=Generated o un ID confirmado de su subárbol: nunca null. La jerarquía incluye padres, profundidad e inactivos, y permite recorrer todas las páginas.

Para nuevas tareas de creación/edición, el agente abre request_begin y cierra request_end: las nuevas creaciones bajo Generated se agrupan en su contenedor y los cambios sobre contenido anterior quedan asociados a esa petición. No se abre un contenedor nuevo para consultas, deshacer o persistencia. Se conserva compatibilidad con llamadas directas sin petición abierta.

delete_object desactiva y mueve la jerarquía a una papelera; restore_object o undo permiten recuperarla con sus componentes e IDs. history_list y undo permiten deshacer la última operación o el bloque de la última petición. Los cambios externos producen un conflicto en vez de sobrescribirse. No hay redo ni historial persistente.

content_save y content_load gestionan slots locales JSON versionados. En el Editor se almacenan en `.seyra/generated/`, excluido de Git; la carga es explícita y reemplaza Generated, con posibilidad de deshacer. Se conservan IDs de objetos, jerarquías, primitivas y Transform. El formato inicial rechaza componentes/materiales no soportados para evitar pérdida silenciosa. No incluye papelera, historial, secretos ni escenas completas. Límites: 2048 objetos vivos, 128 operaciones recuperables y 5 MiB por slot. Consulta [contratos y límites](bridge/README.md).

El Editor lee la contraseña HTTP desde ../Backend/.env.local, relativo al proyecto Unity. Esta prueba no distribuye secretos ni configura red o credenciales del visor. Los mensajes y mutaciones no se reintentan automáticamente. Un timeout deja el resultado desconocido: inspecciona primero antes de volver a modificar.

## Validación

Validación funcional completada en el Editor el 27 de octubre de 2026 con OpenCode real, GLM y DeepSeek: consulta, creación, renombrado, movimiento, cambio de padre, eliminación recuperable, restauración, deshacer y guardado/carga. [Registro reproducible y evidencias](../Progress/Completed/01-validation.md). El panel se comprobó con permisos reales `read`, tanto permitiendo una vez como rechazando, y con una regresión aislada que verifica decisiones sin duplicados. Desde la entrega 02, los turnos rechazados/interrumpidos pueden cerrarse y continuar en la misma sesión, sin obligar a crear otra. No se reprodujo el incidente histórico original ni hay evidencia para atribuirlo al plan gratuito. Se mantiene `seyra_*: allow`; la validación en visor continúa pendiente en el roadmap 07.

La entrega 02 añade `chat-execution-server.mjs` y `ChatExecutionChecks.Run`: 15 comprobaciones de pérdida de respuestas, desconexión, carreras de cancelación, errores y duplicados contra la UI real de Unity, sin proveedor. `RealChatExecutionChecks.Run` valida con el modelo real que un objeto anterior permanece, el siguiente no se crea tras cancelar y se continúa en la misma conversación. La validación detectó que el transporte anterior podía repetir un POST en una conexión reutilizada; `JsonHttpClient` utiliza ahora `HttpClient`, sin reutilización de conexiones ni reintentos. [Evidencias y ejecución](../Progress/Completed/02-execution.md).

npm test --prefix Backend/bridge compila y prueba contratos, permisos del perfil, confirmaciones, timeout sin reenvío y desconexiones. npm run test:unity --prefix Backend/bridge necesita Unity en Play Mode y un puerto MCP libre; crea y transforma contenedores temporales que desaparecen al salir. No llama a un proveedor de modelos. El archivo test-support/SceneContractChecks.cs permite comprobar directamente en el runtime duplicados, caducidad, IDs y validación sin incluir pruebas en Assets.

Phase 3 adds twelve semantic presentations and persisted local interaction through the same scene service. See [presentation contracts](bridge/PRESENTATIONS.md).

## Spatial chat updates

The spatial chat composer records speech into a read-only draft and sends it only when Send is pressed. A second dictation replaces the draft; failed dictation preserves the previous draft. The spatial menu follows the viewer after a delay, pauses while grabbed, and hides while the open chat is centrally visible. Recenter restores the chat and menu placement. Editor checks cover these behaviors; physical headset validation remains pending.

## Conexión segura y Quest

El único puerto accesible desde la red es la pasarela HTTPS (8443 por defecto).
OpenCode (4096) y el bridge WS (4100) siguen escuchando exclusivamente en
`127.0.0.1`. Los dispositivos usan HTTPS con huella SHA-256 del certificado y un
token individual; el bridge remoto usa sondeo largo, nunca WSS. Contrato y lista
exacta de rutas: [bridge/GATEWAY.md](bridge/GATEWAY.md).

Desde la raíz del repositorio en PowerShell, bash o zsh:

```sh
node Backend/seyra.mjs init-tls
node Backend/seyra.mjs pair --name quest3 --url https://192.168.1.131:8443
node Backend/seyra.mjs push --name quest3 --package com.tuempresa.seyra
node Backend/seyra.mjs start
node Backend/seyra.mjs list
node Backend/seyra.mjs revoke --name quest3
```

`init-tls` crea un certificado ECDSA P-256 para dos años en `.seyra/tls/gateway.cert.pem` y una clave PKCS#8 en `gateway.key.pem`. Reemplazar requiere `--force` y volver a emparejar. La pasarela prefiere PEM y conserva compatibilidad con `gateway.pfx`/`gateway.pass` anteriores. La huella es SHA-256 del DER.

`pair` crea `.seyra/pairing/quest3.env` y almacena solo el hash del token en `devices.json`. `push` busca adb en PATH, en el SDK de Android y en el SDK de Unity; exige `--serial` si hay varios dispositivos. Verifica el paquete instalado y el archivo remoto antes de borrar la copia local. Si faltan permisos, abre la app una vez y repite. Nunca copies `Backend/.env.local`: contiene claves de proveedores y la contraseña del backend. El dispositivo solo recibe URL, token propio y huella.

`start` compila el bridge, activa automáticamente la pasarela si existe TLS y genera un token interno solo en memoria. Muestra las URLs candidatas y la huella. `--gateway` la fuerza y `--no-gateway` permite trabajar en el Editor por loopback sin pasarela. Opciones: `--port 4096 --gateway-host 0.0.0.0 --gateway-port 8443`. OpenCode conserva `--print-logs` con nivel `warn` por defecto; `node Backend/seyra.mjs start --verbose` cambia el nivel a `info`. Ctrl+C detiene los servicios y el árbol MCP de OpenCode.

Todos los comandos admiten `--root <directorio>`: allí viven `.env.local` y `.seyra`; por defecto es `Backend/`. Las pruebas usan una raíz temporal y credenciales ficticias. Los envoltorios Windows conservan `-Root`, `-Port`, `-Gateway`/`-Gateway:$false`, `-GatewayHost`, `-GatewayPort` y las opciones anteriores de dispositivo. Contrato y comprobación de OpenCode: [bridge/CLI.md](bridge/CLI.md).

Firewall (configuración manual limitada a la LAN):

- Windows, consola de administrador: `New-NetFirewallRule -DisplayName SeyraGateway -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8443 -Profile Private -RemoteAddress LocalSubnet`. Para retirarla: `Remove-NetFirewallRule -DisplayName SeyraGateway`.
- macOS: acepta el aviso del cortafuegos de aplicaciones para `node` en tu LAN de confianza.
- Linux/ufw: `sudo ufw allow from 192.168.0.0/16 to any port 8443 proto tcp`, ajustando la subred.

Windows está comprobado; macOS, Linux y el push a un dispositivo real siguen pendientes. La [guía raíz](../README.md) incluye equivalencias de IP y comandos adb para bash/zsh.

También funciona sobre VPN o con un puerto del router redirigido a la pasarela;
solo debe publicarse ese puerto HTTPS. El host de la URL puede ser cualquier IP o
nombre alcanzable. Si cambia la IP, cambiar únicamente `SEYRA_GATEWAY_URL` en el
archivo del dispositivo. No desactivar la validación del certificado. Revocar el
token de un dispositivo perdido; un dispositivo autorizado tiene acceso al agente.

En Unity:

- `Seyra/Build/Android APK`: genera `Builds/Seyra.apk`, únicamente con
  `SeyraAssistant`, y habilita XR Android durante ese build.
- `Seyra/Build/Android Phone Test`: genera `Builds/SeyraPhoneTest.apk`, únicamente
  con `MobileConnectionTest`, orientación vertical y XR desactivado. El área de
  resultados del diagnóstico tiene un mínimo de 900 unidades del CanvasScaler.
- Ambos restauran la configuración previa al terminar, mantienen el identificador
  del proyecto y no incluyen el archivo de conexión. El móvil necesita ARM64 y al
  menos la versión Android configurada en el proyecto (API 32 actualmente).
- Instalar, abrir una vez y copiar el archivo de emparejamiento a la ruta indicada.
  En el móvil pulsar **Reconectar**; en Quest cerrar y volver a abrir la app tras copiar el archivo. Comprobar por separado chat y runtime,
  conversar y crear un panel. **Probar TLS** no envía credenciales ni abre una
  sesión: una respuesta 401 confirma TLS, pero no autoriza al dispositivo.
- Para validar pausa/reanudación, apartar el visor 30 s y continuar. Se consulta la
  sesión previa y no se reenvía una mutación con resultado desconocido. Comprobar
  también que `revoke` provoca el mensaje de dispositivo no autorizado.

El modo directo solo existe en Editor, sobre HTTP/WS de loopback. En Editor, si se
configuran las tres variables de pasarela en su archivo local, se usa la misma
pasarela. Para pruebas aisladas, `SEYRA_EDITOR_ENV_PATH` puede seleccionar un archivo
alternativo en el proceso del Editor; no se lee entonces el archivo real. Esa
variable de prueba no se usa en el dispositivo.


## Spoken replies (Meta Voice SDK)

Set `WITAI_CLIENT_TOKEN` only in `Backend/.env.local` on the PC, using the client credential of an English Wit.ai app. The spatial chat uses `wit$Rebecca`. The chat and hand menu share one icon button: `volume_up` means voice is enabled and `volume_off` means it is muted. Muting stops output and persists the preference; enabling voice waits for the next reply and never replays an old one. Output continues with the chat closed. Starting dictation, sending or cancelling still stops it.

The authenticated gateway exposes `GET /_seyra/tts/config` with `Cache-Control: no-store` (see [GATEWAY.md](bridge/GATEWAY.md)). Quest keeps the returned credential only in a runtime configuration in memory. Never copy it to assets, builds, device environment files, persistentDataPath or logs. Paired devices can obtain it in memory; revoking a device prevents new reads, while invalidating an already delivered credential requires rotating it in Wit.ai. The headset needs internet access to Wit.ai for audio, in addition to access to the PC gateway. TTS uses no disk or runtime cache; spoken output is limited to 600 characters after filtering code, URLs, JSON and markdown. Voice failures leave the text chat available.

## Camera questions

`seyra_camera_look` answers a specific question about a headset photo without
creating content. `seyra_camera_segment` answers what the user is looking at
and creates a card showing the photo with coloured masks, boxes and labels.
Both show the shutter and three-second popup. The bridge sends the photo to
Muse Spark through the Meta Model API with `store: false`; segmentation also
sends it to SAM 3.1 through that API with `store: false`, once per object name.
This describes the requests and makes no claim about internal provider retention.

With camera_look, the photo stays only in memory, without disk writes or logs.
With camera_segment, the composed image (at most 512 px on its longest side,
with the photo as its background) is written on the headset to
`Media/<hex>.jpg` and referenced by saved slots. Deleting the card does not
delete that file. The full-resolution photo is not saved. OpenCode receives
text only, never image bytes or base64; the question, answer and textual tool
results can remain in session history in opencode.db. Unity retains only the
last eight capture metadata records without image bytes.

The tools share the existing META_API_KEY, one lock through the entire call
and at most 20 capture attempts per ten minutes. Capture has up to ten seconds;
camera_look gives Muse thirteen seconds. camera_segment gives Muse seven
seconds and parallel SAM requests a single shared budget of
`Math.min(8000, 21000 - elapsed)` ms, measured from call start, reserving three
seconds for composition/card operations and one second of margin. A non-positive
budget skips SAM. Failures are not retried automatically. The prompt limits
capture to explicit visual requests; this is not a technical consent check.
Turn cancellation may not interrupt a started call or prevent card creation.
See [camera tools and retention contract](bridge/CAMERA.md). End-to-end headset
validation remains pending.

`@meta-sam/parser` 0.1.0 uses the SAM License. Include a copy of that license
when redistributing the package or its derivatives; the installed copy is
`bridge/node_modules/@meta-sam/parser/LICENSE`.

### Camera image edits

`seyra_camera_edit` takes a requested photo edit or refines the latest result with previousEditId, placing a loading mediaCard before asynchronous Muse Image editing. Uses the existing META_API_KEY and shared image budget; no OpenCode configuration change. See [the contract and retention details](bridge/CAMERA.md#camera-image-edits) and [media delivery](bridge/MEDIA.md#edited-camera-images). Provider photo retention is unknown. Rebuild the bridge and restart the backend and OpenCode after deployment. An updated APK is needed only for the camera_edit activity text.
