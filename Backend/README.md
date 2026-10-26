# Backend de Seyra

Desarrollo incremental del backend y su integración con Unity. OpenCode v2 sirve el chat y administra sesiones, modelos y el [MCP de escena Unity](bridge/README.md). Esta fase incluye exploración, contenedores, primitivas nativas y Transform, sin capturas ni UI generada.

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

Ejemplos:

- «Lista toda la escena de forma jerárquica con sus IDs».
- «Busca Generated y consulta sus componentes».
- «Crea un contenedor Mesa dentro de Generated con posición local (0,1,1), rotación (0,0,0) y escala (1,1,1)».
- «Mueve ese contenedor a X=2 conservando el resto del Transform».
- «Crea una mesa de 1.2 × 0.8 m usando cubos dentro de un contenedor, con tablero a 0.75 m de altura».

Las primitivas se crean con `create_primitive` (cube, sphere, capsule, cylinder, plane, quad) y comparten padres, validaciones, GUID y edición de Transform con los objetos vacíos. La escala multiplica la malla nativa; por ejemplo, un plano es 10 × 10 antes de escalar. La herramienta devuelve su geometría para poder comprobar tamaños. Conserva collider nativo, sin Rigidbody, y utiliza el material compartido del render pipeline.

Los GUID de objetos/componentes permanecen al renombrar o reubicar, pero caducan al reiniciar el runtime. Las creaciones exigen parentId=Generated o un ID confirmado de su subárbol: nunca null. La jerarquía incluye padres, profundidad e inactivos, y permite recorrer todas las páginas. El contrato y límites están en bridge/README.md.

El Editor lee la contraseña HTTP desde ../Backend/.env.local, relativo al proyecto Unity. Esta prueba no distribuye secretos ni configura red o credenciales del visor. Los mensajes y mutaciones no se reintentan automáticamente. Un timeout deja el resultado desconocido: inspecciona primero antes de volver a modificar.

## Validación

npm test --prefix Backend/bridge compila y prueba contratos, igualdad de perfiles/permisos, confirmaciones, timeout sin reenvío y desconexiones. npm run test:unity --prefix Backend/bridge necesita Unity en Play Mode y un puerto MCP libre; crea y transforma contenedores temporales que desaparecen al salir. No llama a un proveedor de modelos. El archivo test-support/SceneContractChecks.cs permite comprobar directamente en el runtime duplicados, caducidad, IDs y validación sin incluir pruebas en Assets.
