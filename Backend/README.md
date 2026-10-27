# Backend de Seyra

Desarrollo incremental del backend y su integración con Unity. OpenCode v2 sirve el chat y administra sesiones, modelos y el [MCP de escena Unity](bridge/README.md). Incluye exploración, contenedores por petición, primitivas, Transform, eliminación recuperable, deshacer y persistencia del contenido generado. Todavía no incluye capturas ni UI generada.

## Arranque

Requiere Node.js 22 o posterior, npm y opencode2 en PATH. Desde la raíz del repositorio:

```powershell
npm ci --prefix Backend/bridge
cd Backend
.\start.ps1
```

start.ps1 carga .env.local como datos, compila el bridge y arranca OpenCode en http://127.0.0.1:4096. OpenCode inicia el bridge por stdio; Unity conecta por WebSocket a localhost:4100. No arranques el bridge por separado durante el uso normal. Reinicia OpenCode tras modificar catálogo/configuración y reconecta el chat para crear una sesión nueva.

.env.local está excluido de Git y requiere HELMCODE_API_KEY y OPENCODE_SERVER_PASSWORD. La contraseña HTTP es distinta de la clave de Helmcode. El script fija también OPENCODE_PASSWORD al mismo valor, evitando discrepancias con variables heredadas. No copies credenciales a Assets ni las publiques.

La opción ./start.ps1 -Port 4097 cambia solo el puerto HTTP: no permite ejecutar dos bridges en 4100. Si existe otra instancia/servicio OpenCode, detén esa instancia antes de iniciar la nueva o configura otro puerto WebSocket en ambos extremos. Al mover el repo actualiza command y cwd del MCP en opencode.json.

## Perfiles y permisos

- seyra hereda helmcode/glm5.3-flash, autenticado por HELMCODE_API_KEY.
- seyra_deepseek usa opencode/deepseek-v4.1-flash-free.

Ambos perfiles comparten instrucciones y permisos. La regla general ask seguida de seyra_* con allow autoriza las herramientas actuales y futuras de Unity sin ampliar permisos de shell, archivos u otros servicios. No se añaden confirmaciones para el MCP de Unity. Los argumentos se validan en el bridge y en el runtime; el éxito solo se comunica tras confirmación del runtime.

## Unity

SeyraAssistant.unity y OpencodeTestUI.unity contienen SeyraRuntime con el componente SeyraSceneRuntime y un contenedor raíz Generated asignado en el Inspector. Solo un runtime puede poseer el bridge; al cargar ambas escenas aditivamente, el segundo se desactiva. Las operaciones son de Play Mode, en memoria: no guardan cambios en los assets de escena.

Para usar el chat, abre Assets/Scenes/OpencodeTestUI.unity en el proyecto nuevo y entra en Play Mode. El verde confirma conexión con OpenCode, no la conexión del MCP con Unity. Si el runtime no está disponible, las herramientas devuelven UNITY_NOT_CONNECTED.

Si OpenCode devuelve una petición de permiso, el chat muestra su acción y los detalles completos como texto literal en un panel desplazable. «Permitir una vez» responde `once`; «Rechazar» responde `reject` y OpenCode rechaza también los demás permisos pendientes de esa sesión. Las decisiones se envían solo al pulsar el botón y no se reintentan automáticamente. La espera de la decisión pausa el timeout del turno; después se sigue leyendo el mismo turno, sin reenviar el mensaje. Este panel atiende solicitudes del servidor y no cambia la regla `seyra_*: allow`. Cabecera, conversación, panel y compositor usan layouts.

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

Estado de esta entrega: pendiente de prueba funcional por el usuario. Se ha reportado un bloqueo de permisos en OpenCode, atribuido provisionalmente al modo `ask` del plan gratuito; esa causa todavía no se ha verificado. La configuración del proyecto mantiene `seyra_*: allow` y no se añaden confirmaciones al bridge ni al runtime. Las pruebas técnicas de contratos, MCP y Unity realizadas durante la implementación no sustituyen la validación del flujo con el modelo y plan del usuario.

npm test --prefix Backend/bridge compila y prueba contratos, igualdad de perfiles/permisos, confirmaciones, timeout sin reenvío y desconexiones. npm run test:unity --prefix Backend/bridge necesita Unity en Play Mode y un puerto MCP libre; crea y transforma contenedores temporales que desaparecen al salir. No llama a un proveedor de modelos. El archivo test-support/SceneContractChecks.cs permite comprobar directamente en el runtime duplicados, caducidad, IDs y validación sin incluir pruebas en Assets.
