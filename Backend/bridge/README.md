# MCP de escena

OpenCode inicia `node dist/index.js` por stdio. El bridge escucha un único runtime en `ws://127.0.0.1:4100`. Unity valida y aplica las órdenes en el hilo principal durante Play Mode; no se ejecuta código enviado por el modelo ni se editan assets.

| Herramienta | Función |
| --- | --- |
| `scene_hierarchy` | Jerarquía completa de escenas cargadas, incluidos inactivos y DontDestroyOnLoad. |
| `scene_find` | Búsqueda por nombre, ruta y/o tipo de componente. |
| `scene_get` | Identidad, Transform y componentes paginados con sus GUID. |
| `create_gameobject` | GameObject vacío con padre y Transform local explícitos. |
| `create_primitive` | Cubo, esfera, cápsula, cilindro, plano o quad con el mismo contrato de creación. |
| `set_transform` | Modificar únicamente los campos proporcionados. |
| `set_parent` | Reubicar descendientes dentro de Generated, sin ciclos. |
| `request_begin` / `request_end` / `request_list` | Contenedor y agrupación de una petición de trabajo. |
| `delete_object` / `restore_object` | Papelera recuperable, sin destruir objetos ni componentes. |
| `history_list` / `undo` | Deshacer la última operación o el último bloque de petición. |
| `content_save` / `content_load` / `content_list` | Slots JSON locales de contenido generado. |

En OpenCode se exponen con prefijo `seyra_`, cubierto por `seyra_*: allow`, sin confirmaciones adicionales.

## Identidad y jerarquía

`GeneratedObject` identifica los objetos gestionados con un GUID y su procedencia (vacío, primitiva o contenedor de petición). Su `entityId` se conserva al renombrar, reubicar, eliminar recuperablemente, restaurar y guardar/cargar. Para conservarlo entre ejecuciones hay que guardar y cargar un slot; no hay autoguardado ni carga automática. Los objetos externos y los `componentId` siguen teniendo identidad de runtime. `runtimeId` cambia al reiniciar el servicio; al cargar se reconstruyen componentes con nuevos IDs. No son GUID de assets ni IDs persistentes de Editor. Los nombres/rutas pueden repetirse; las búsquedas devuelven cada candidato.

`Generated` es un alias del contenedor asignado en el Inspector. Las creaciones requieren `parentId="Generated"` o el ID de un descendiente. Nunca se acepta null, la cadena "null", vacío, IDs de escena ni padres externos. `set_parent` solo reubica descendientes: no mueve Generated ni incorpora objetos externos. La exploración y `set_transform` sí admiten objetos existentes fuera de Generated.

`scene_hierarchy` devuelve `scenes` y `nodes`, un árbol aplanado en preorden. `entityId`, `parentId`, `sceneId`, `depth` y `siblingIndex` permiten reconstruirlo. El padre de una raíz es su ID de escena, no null; esos IDs son agrupaciones de consulta, no destinos de creación.

Las consultas incluyen inactivos por defecto y devuelven 200 nodos (máximo 500) por página. Continúa `nextOffset` con `expectedRevision` igual a `revision`. Si cambia la jerarquía, se rechaza la siguiente página: reinicia desde offset 0. Las consultas no congelan el estado de Unity. A partir de 50000 nodos se exige restringir con `rootId`, sin devolver resultados truncados silenciosamente.

`scene_find` combina filtros con AND. Nombres/rutas admiten `contains` o `exact` sin distinguir mayúsculas. `componentType` exige el nombre corto/completo exacto e incluye clases base e interfaces. No admite código ni regex.

`scene_get` devuelve 50 componentes por defecto (máximo 100) con `nextComponentOffset`. Cada uno incluye tipo, índice, GUID y `enabled` cuando procede. Un script ausente se marca como `missingScript`. No se leen propiedades arbitrarias ni se modifican componentes.

La papelera `__SeyraTrash` es un hijo interno desactivado de Generated y queda excluida de jerarquía/búsquedas normales incluso al incluir inactivos. `scene_get` puede inspeccionar un ID eliminado y muestra `deleted=true`. Un objeto inactivo normal continúa siendo visible en las consultas: inactividad y eliminación son estados distintos.

## Peticiones, eliminación y deshacer

Para una tarea de creación/edición/eliminación/recuperación:

1. `request_begin({name})` crea un contenedor y devuelve `requestGroupId` (su `entityId`). Solo hay una petición abierta por runtime.
2. Las creaciones con `parentId="Generated"` van a ese contenedor; un padre explícito se respeta para ampliar una jerarquía existente. Las modificaciones de objetos anteriores pertenecen al historial de la petición nueva, sin mover esos objetos de su contenedor original.
3. `request_end({requestGroupId})` cierra el bloque y conserva el resultado, también si algunas operaciones fallaron.

`requestGroupId` de un nodo indica procedencia; `operationRequestGroupId` indica el bloque que acaba de actuar sobre él. No son el UUID de transporte. Las llamadas directas antiguas sin petición abierta siguen funcionando y generan operaciones deshacibles individuales.

Para consultar, deshacer, guardar o cargar, no abras una petición nueva: añadirías una operación ajena al cambio que quieres deshacer. `request_list` muestra los contenedores vivos y el bloque abierto. Una petición posterior puede modificar objetos de una anterior usando sus IDs.

`delete_object` solo admite descendientes de Generated; protege la raíz, la papelera y el contenedor de la petición abierta (ciérrala primero). Desactiva el objeto y mueve su subárbol a la papelera. `restore_object` recupera la raíz de esa eliminación con su padre, índice, Transform y activeSelf originales. Los hijos y componentes permanecen intactos, por lo que mantienen sus IDs. Si el padre también está eliminado, recupéralo primero. Desactivar un objeto puede ejecutar sus callbacks de Unity; no equivale a pausar efectos externos de scripts arbitrarios.

`history_list` devuelve las operaciones más recientes primero, con `operationId`, herramienta, objeto y petición. `undo({scope:"operation",expectedOperationId})` deshace la última operación; `scope:"request"` deshace todo el bloque más reciente de la misma petición. No se salta cambios posteriores. Se registran creaciones, Transform, reparenting, eliminación, recuperación y carga de contenido. Deshacer una creación la mueve a papelera; deshacer una carga devuelve los objetos anteriores con sus componentes originales. No hay redo.

Antes de deshacer se compara el estado esperado con la escena actual. Cambios externos en Transform, jerarquía, activación, nombres o configuración de componentes producen `HISTORY_CONFLICT` sin sobrescribirlos. Esto también protege Transform de objetos externos modificados por una petición. Sistemas de tracking/animación pueden causar conflictos legítimos. Si un script destruye objetos y una reversión no puede completarse, el resultado es `ROLLBACK_FAILED` y el bridge marca `outcomeUnknown`: inspecciona la escena, no reintentes automáticamente.

El historial y los datos de recuperación solo duran el runtime; no se serializan. Hay un límite de 128 operaciones recuperables y 8192 objetos retenidos contando papelera. No se purga ni se evicta historial silenciosamente. Guarda contenido y reinicia Play Mode cuando necesites empezar otro historial. Los snapshots comparten estados inmutables no modificados para evitar duplicar el estado completo de cada objeto en cada operación.

## Persistencia del contenido generado

`content_save({slot})` guarda un JSON versionado y `content_load({slot})` sustituye el contenido vivo de Generated. Ambas requieren una petición cerrada. Guardar no añade un paso al historial de escena ni se deshace con `undo`; cargar sí. No se guardan escenas `.unity`, código, assets, credenciales, objetos externos, papelera ni historial.

En el Editor los slots están en `.seyra/generated/` de la raíz del repositorio, excluida de Git. En builds se usa `Application.persistentDataPath/Seyra/Generated`. Esta implementación no cambia la limitación actual de red a localhost. El MCP recibe únicamente nombres de slot (ASCII, números, guion/guion bajo, 1..64), nunca rutas. `content_list` devuelve hasta 1000 slots con tamaño y fecha.

El formato v1 guarda:

- IDs de objetos gestionados y contenedores de petición, procedencia y jerarquía con orden de hermanos.
- Tipos de las seis primitivas, nombre, posición/rotación/escala locales, activeSelf, tag y layer.
- Transform y estado del contenedor Generated.

Solo se reconstruyen vacíos, contenedores y primitivas nativas con su malla/material y parámetros originales. Se rechazan objetos no gestionados, scripts adicionales, materiales modificados, colliders alterados y otros datos no soportados, antes de sobrescribir un archivo. No se omiten silenciosamente. No hay todavía persistencia de colores personalizados ni componentes arbitrarios.

El guardado usa un temporal y sustitución atómica del archivo anterior. La carga valida versión, unicidad de IDs, tipos, padres, referencias a peticiones, ciclos, números y límites; construye primero una jerarquía inactiva de staging. El contenido anterior se conserva recuperable para `undo`. Un archivo inválido no reemplaza el contenido actual. Los componentId cambian al reconstruir, pero los entityId se conservan.

Límites: 2048 objetos vivos contando Generated y 5 MiB por archivo. No se cargan objetos desde rutas arbitrarias ni se ejecuta código del archivo. Los slots no son una copia completa de la escena Unity ni una serialización universal de GameObjects.

## Primitivas

`create_primitive` añade `primitiveType` al contrato de `create_gameobject`: `name`, `parentId`, `position`, `rotation` y `scale`, todos obligatorios. Ambos reutilizan el mismo esquema base y la misma ruta de creación/validación/rollback en Unity. La selección de geometría usa GameObject.CreatePrimitive; no hay una implementación independiente para cada forma.

| primitiveType | Malla nativa antes de aplicar escala |
| --- | --- |
| `cube` | 1 × 1 × 1 |
| `sphere` | Diámetro 1 |
| `capsule` / `cylinder` | Diámetro 1 y altura 2 en Y |
| `plane` | 10 × 10 en XZ, sin grosor |
| `quad` | 1 × 1 en XY, sin grosor |

Las unidades son locales al padre. `scale` multiplica esa malla; no existe otro factor `size`. Por ejemplo, un plano local de 1 × 1 usa scale=(0.1,1,0.1). Los ancestros también afectan las dimensiones globales.

Se conservan MeshFilter, MeshRenderer y collider nativos; no se añade Rigidbody. El material es compartido y procede del render pipeline activo cuando ofrece uno; no se instancia un material por objeto. Esta fase no añade color ni edición de materiales.

La respuesta de creación incluye `primitiveType`. Creación y `scene_get` devuelven además `geometry` para objetos con MeshFilter: nombre de malla, número de vértices y `meshBoundsLocal` (center/size sin Transform). Esos bounds son de la malla, no dimensiones globales ni bounds del collider. Para editar, buscar o reubicar la primitiva se reutilizan `set_transform`, `scene_get`, `scene_find` y `set_parent` con los GUID existentes.

## Transform

- Creación: posición, rotación y escala locales al padre; todos obligatorios.
- Modificación: al menos un campo; los omitidos se conservan. `space` obligatorio, `local` o `world`, para posición y rotación.
- Rotación Euler XYZ en grados mediante Quaternion.Euler; Unity puede devolver ángulos equivalentes normalizados.
- Escala siempre localScale, incluyendo cero/negativos; no representa dimensiones globales.
- Números finitos representables como float. Se rechazan matrices no finitas y operaciones globales bajo padres no invertibles. Si falla la aplicación se restaura el Transform.
- `worldPositionStays` conserva el estado global al reubicar cuando Unity puede representarlo; con escalas no uniformes puede haber aproximaciones. La respuesta incluye el estado real.

Layout, física, animación o tracking pueden sobrescribir después un Transform. El éxito confirma el estado inmediatamente después de la operación.

## Confirmación y límites

Cada orden lleva un UUID y una fecha límite. Unity descarta órdenes caducadas antes de ejecutarlas. El bridge nunca reintenta mutaciones: timeout/desconexión devuelve `outcomeUnknown`, pues la operación podría haberse aplicado. Consulta el estado antes de continuar.

El runtime conserva las respuestas de hasta 4096 peticiones mutadoras para impedir ejecuciones duplicadas del mismo UUID; argumentos distintos con el mismo UUID se rechazan. Al llegar al límite hay que reiniciar Play Mode para más mutaciones; las consultas siguen disponibles. Ese límite de protocolo es independiente del historial de 128 operaciones.

Solo se admite una conexión Unity y se rechazan conexiones con Origin de navegador. Esta fase funciona en localhost para Play Mode del Editor: no configura red del visor, autenticación remota ni capturas.

## Pruebas

Desde esta carpeta: `npm ci` y `npm test`. Se comprueban contratos/permisos y transporte con runtime simulado, incluyendo timeout sin reenvío y desconexión con resultado desconocido.

`npm run test:unity` necesita una escena configurada en Play Mode y el puerto MCP libre. Crea contenedores temporales y prueba jerarquía paginada, IDs, componentes, nombres duplicados, Transform local/global, reparenting y rechazo de ciclos, padres inválidos y revisiones caducadas. Los objetos desaparecen al salir de Play Mode. No llama al modelo.

También comprueba las seis primitivas, sus dimensiones nativas, colliders, reutilización de GUID/Transform y que los argumentos inválidos no creen objetos. `test-support/SceneContractChecks.cs` comprueba deduplicación y material/shader dentro de Unity. `test-support/RunUnityIntegration.cs`, ejecutado con `unity command script run` en Play Mode, ejecuta la misma prueba MCP en el puerto 4101 y un contenedor temporal aislado, que elimina al terminar; permite probar sin desconectar el runtime habitual de 4100.

La integración MCP incluye peticiones, papelera, recuperación, deshacer por operación/petición y guardado/carga de las seis primitivas. `test-support/LifecycleChecks.cs` prueba además conflictos por edición externa, sobrescritura atómica, rechazo de componentes no soportados, archivos inválidos, recuperación tras recrear el runtime y ausencia de acceso mediante rutas. Cada fixture usa objetos y archivos propios y los retira al terminar.

Variables: `SEYRA_UNITY_PORT` (4100) y `SEYRA_UNITY_TIMEOUT_MS` (10000, máximo 60000). Si cambias el puerto, cambia `bridgeUrl` en Unity. El timeout MCP de OpenCode debe superar el del bridge. Una sola instancia de OpenCode debe poseer ese puerto.
# Renombrado

`rename_object({ entityId, name })` renombra descendientes vivos de `Generated` (incluidos contenedores), conservando GUID, componentes, jerarquía y Transform. El nombre se recorta y debe tener 1–256 caracteres; puede repetirse porque los objetos se identifican por GUID. Rechaza la raíz `Generated`, objetos externos y eliminados. Reutiliza el historial por operación/petición y el guardado de nombres de `content_save`/`content_load`. Cubierta por el permiso existente `seyra_*: allow`.

