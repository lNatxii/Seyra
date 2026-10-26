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

En OpenCode se exponen con prefijo `seyra_`, cubierto por `seyra_*: allow`, sin confirmaciones adicionales.

## Identidad y jerarquía

`entityId` y `componentId` son GUID en memoria: permanecen al renombrar, reubicar o reconectar el WebSocket. Reiniciar Play Mode o el componente runtime cambia el `runtimeId` y los IDs. No son GUID de assets ni IDs persistentes de Editor. Los nombres/rutas pueden repetirse; las búsquedas devuelven cada candidato.

`Generated` es un alias del contenedor asignado en el Inspector. Las creaciones requieren `parentId="Generated"` o el ID de un descendiente. Nunca se acepta null, la cadena "null", vacío, IDs de escena ni padres externos. `set_parent` solo reubica descendientes: no mueve Generated ni incorpora objetos externos. La exploración y `set_transform` sí admiten objetos existentes fuera de Generated.

`scene_hierarchy` devuelve `scenes` y `nodes`, un árbol aplanado en preorden. `entityId`, `parentId`, `sceneId`, `depth` y `siblingIndex` permiten reconstruirlo. El padre de una raíz es su ID de escena, no null; esos IDs son agrupaciones de consulta, no destinos de creación.

Las consultas incluyen inactivos por defecto y devuelven 200 nodos (máximo 500) por página. Continúa `nextOffset` con `expectedRevision` igual a `revision`. Si cambia la jerarquía, se rechaza la siguiente página: reinicia desde offset 0. Las consultas no congelan el estado de Unity. A partir de 50000 nodos se exige restringir con `rootId`, sin devolver resultados truncados silenciosamente.

`scene_find` combina filtros con AND. Nombres/rutas admiten `contains` o `exact` sin distinguir mayúsculas. `componentType` exige el nombre corto/completo exacto e incluye clases base e interfaces. No admite código ni regex.

`scene_get` devuelve 50 componentes por defecto (máximo 100) con `nextComponentOffset`. Cada uno incluye tipo, índice, GUID y `enabled` cuando procede. Un script ausente se marca como `missingScript`. No se leen propiedades arbitrarias ni se modifican componentes.

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

El runtime conserva las respuestas de hasta 4096 peticiones mutadoras para impedir ejecuciones duplicadas del mismo UUID; argumentos distintos con el mismo UUID se rechazan. Al llegar al límite hay que reiniciar Play Mode para más mutaciones; las consultas siguen disponibles.

Solo se admite una conexión Unity y se rechazan conexiones con Origin de navegador. Esta fase funciona en localhost para Play Mode del Editor: no configura red del visor, autenticación remota, eliminación ni capturas.

## Pruebas

Desde esta carpeta: `npm ci` y `npm test`. Se comprueban contratos/permisos y transporte con runtime simulado, incluyendo timeout sin reenvío y desconexión con resultado desconocido.

`npm run test:unity` necesita una escena configurada en Play Mode y el puerto MCP libre. Crea contenedores temporales y prueba jerarquía paginada, IDs, componentes, nombres duplicados, Transform local/global, reparenting y rechazo de ciclos, padres inválidos y revisiones caducadas. Los objetos desaparecen al salir de Play Mode. No llama al modelo.

También comprueba las seis primitivas, sus dimensiones nativas, colliders, reutilización de GUID/Transform y que los argumentos inválidos no creen objetos. `test-support/SceneContractChecks.cs` comprueba deduplicación y material/shader dentro de Unity. `test-support/RunUnityIntegration.cs`, ejecutado con `unity command script run` en Play Mode, ejecuta la misma prueba MCP en el puerto 4101 y un contenedor temporal aislado, que elimina al terminar; permite probar sin desconectar el runtime habitual de 4100.

Variables: `SEYRA_UNITY_PORT` (4100) y `SEYRA_UNITY_TIMEOUT_MS` (10000, máximo 60000). Si cambias el puerto, cambia `bridgeUrl` en Unity. El timeout MCP de OpenCode debe superar el del bridge. Una sola instancia de OpenCode debe poseer ese puerto.
