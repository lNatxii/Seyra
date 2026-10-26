# Backend de Seyra

Base de la conexión del backend con Unity. Ejecuta la API v2 de `opencode2`; no hay un servidor HTTP propio ni bridge de Unity todavía.

## Arranque

Requiere el CLI `opencode2` instalado en PATH. Desde PowerShell:

```powershell
cd C:\Proyectos\Seyra\Backend
.\start.ps1
```

El servidor escucha solo en `http://127.0.0.1:4096`. Deténlo con Ctrl+C. Para otra instancia de prueba usa `./start.ps1 -Port 4097`, y cambia también Server Url en el componente de la escena. No ejecutes dos servidores en el mismo puerto.

`start.ps1` lee `.env.local` como datos, sin ejecutar su contenido, y exige:

```dotenv
HELMCODE_API_KEY=tu_clave_del_proveedor
OPENCODE_SERVER_PASSWORD=tu_contraseña_http
```

Las credenciales se configuran localmente. `.env.local` está excluido por el `.gitignore` de la raíz; nunca lo copies a Assets ni lo publiques. La contraseña HTTP y la clave de Helmcode son distintas. El script fija también `OPENCODE_PASSWORD` al mismo valor, pues tiene prioridad en OpenCode 2.

## Configuración mínima

`opencode.json` conserva dos perfiles con las mismas instrucciones y permisos:

- `seyra`: hereda `helmcode/glm5.3-flash`, con `HELMCODE_API_KEY` por entorno.
- `seyra_deepseek`: usa `opencode/deepseek-v4.1-flash-free`.

Los prompts son inline para que esta base no dependa de carpetas aún sin portar. No hay servidores MCP registrados. `bridge/` queda vacío y reservado para la siguiente fase (Git no conserva directorios vacíos). La autorización general `ask`, seguida de `seyra_*` con `allow`, queda preparada para las herramientas futuras de Unity y no amplía los permisos de otros servicios.

## Prueba desde Unity

Abre `SeyraUnity/Assets/Scenes/OpencodeTestUI.unity` y entra en Play Mode con el backend iniciado. Es un canvas de escritorio para comprobar la API, separado de la escena XR `SeyraAssistant`.

El componente `OpenCodeTestChat` permite cambiar Server Url y Agent. En el Editor resuelve `../Backend` desde la raíz del proyecto Unity y lee su contraseña al conectar. La contraseña no se serializa en la escena ni se incluye en builds. Esta prueba está pensada para el Editor; no configura credenciales ni red del visor.

El estado verde confirma que la sesión de OpenCode responde; rojo significa desconectado o autenticación fallida. Se comprueba periódicamente y el botón de reconexión permite crear una sesión nueva. El botón de enviar usa el icono de envío; Enter envía y Shift+Enter añade una línea. El historial se mantiene durante la sesión de Play Mode. La respuesta se consulta mediante la API v2 hasta recibir el evento `idle`; los envíos no se reintentan automáticamente. La UI comunica errores y solicitudes de permisos, pero esta primera fase solo implementa chat, sin ejecutar acciones sobre Unity.
