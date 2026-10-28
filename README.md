# Seyra

Asistente espacial de realidad mixta para Meta Quest 3, con Unity, Meta XR SDK, OpenCode y un bridge MCP para consultar y modificar contenido de escena.

## Meta AI

El perfil por defecto `seyra` usa **Muse Spark** (`meta/muse-spark-1.3`) mediante **Meta Model API**, con el protocolo Responses. Integración validada en el Editor con conversación real, creación de cubos y etiquetas, colección de libros en arco y deshacer: 14 llamadas a herramientas confirmadas. La validación física en visor permanece pendiente.

La clave `META_API_KEY` se configura exclusivamente en `Backend/.env.local` en el PC; no se distribuye a Unity ni al visor. `MODEL_API_KEY` se admite como alias en el proceso de arranque. Consulta [arranque, perfiles y permisos](Backend/README.md).

Los perfiles alternativos son `seyra_glm`, `seyra_deepseek` y `seyra_zai`, con las mismas instrucciones y permisos.

Muse Voice Transcribe, visión con la cámara, SAM y Muse Image están previstos en el roadmap; no forman parte de esta integración.
