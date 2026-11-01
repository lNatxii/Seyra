# Seyra — Roadmap de desarrollo

Propuesta de referencia · 27 de octubre de 2026 · Revisada el 31 de octubre de 2026.

## Objetivo y criterio de prioridad

Seyra debe permitir conversar en realidad mixta, crear y modificar contenido espacial con herramientas verificables y utilizar imágenes del entorno para ofrecer explicaciones y anotaciones útiles.

El número de cada feature es su identificador, no su orden. El orden de trabajo vigente está en "Orden recomendado". P0 entrega un asistente espacial funcional; P1 incorpora comprensión del entorno físico; P2 mejora la experiencia multimodal. Las dependencias indicadas son condiciones de entrada. No se considera terminada una feature solo porque compile: debe cumplir sus criterios de aceptación en el flujo real.

Se desarrolla en `Seyra/`.

Este documento fija prioridad, orden, dependencias y criterios de aceptación. El diseño detallado de cada feature, su estado de implementación, limitaciones y validación están en [Progress/](Progress/README.md): un archivo por feature que se mueve entre `Future/`, `InProgress/` y `Completed/`. La línea **Estado** de cada sección enlaza a ese archivo.

## Estado actual (31 de octubre de 2026)

Capacidades implementadas que deben reutilizarse. El recorrido principal (chat, herramientas, habitación, `scene_build` y layouts) está probado en Quest 3 con el APK independiente (07).

| Área | Implementado | Pendiente |
| --- | --- | --- |
| Conexión | Pasarela HTTPS con emparejamiento por dispositivo, CLI multiplataforma (`seyra.mjs`) y logs reducidos a `warn`; APK independiente probado en Quest 3. | Revisión de 08. |
| Chat | Chat espacial en inglés (31) con voz Muse a borrador, respuestas habladas con botón único de voz (18, 18b), menú de mano, actividad transitoria y sin detalle técnico (06f); usado en Quest 3. | Medir la latencia de voz. |
| Modelo | Muse Spark como perfil principal (`seyra`); GLM como alternativa. | Visión con capturas (11–12). |
| Herramientas | Circuito MCP único con bridge, validación y confirmación de Unity; errores estructurados; catálogo sin esquemas recursivos. | — |
| Contenido | Primitivas con apariencia, paneles, trece presentaciones sin imágenes web (retiradas en 26c y 26d), layouts de tarjetas, `scene_build`, `scene_edit` y movimiento paramétrico. | Probar en Quest 3 líneas y puntos (29), tarjetas marcables (27) y movimiento (24). |
| Espacio | Habitación MRUK, colocación en pared, mesa o aire con legibilidad por panel (22f), mesa sin rechazos por comodidad (34), acople y seguimiento del chat. | Probar 34 y 28 en Quest 3. |
| Persistencia | Slots v5 con primitivas, apariencia, paneles, presentaciones y layouts. | Controles para el usuario (09) y anclajes entre sesiones (16). |

El historial y la papelera duran el runtime. Guardar/cargar contenido no equivale a persistir una conversación ni a recuperar posiciones físicas entre sesiones. Los componentes tienen IDs de runtime; los IDs de objetos generados se conservan mediante guardado/carga explícitos.

## Orden recomendado

Revisado el 31 de octubre de 2026, con el hackathon abierto hasta el 4 de noviembre. La puntuación (0–100) combina el valor para el usuario y la demo, el esfuerzo y el riesgo; el detalle está en cada sección.

| # | Feature | Puntuación | Motivo |
| --- | --- | --- | --- |
| 1 | 26. Tarjetas con imagen de internet | 85 | Completed · imágenes web retiradas en 26c y 26d (normas del hackathon); 26b con Pexels pausada sin claves. |
| 2 | 31. Seyra en inglés | — | Completed · Revisada. La demo será en inglés. Recorrido manual pendiente. |
| 2b | 18. Respuestas habladas | 75 | Completed · Cerrada. Voz `wit$Rebecca` probada en Quest 3; latencia sin medir. |
| 2c | 18b. Botón único de voz | — | Completed · Cerrada. Probada por el usuario en Quest 3; SpatialChatChecks sin repetir. |
| 2d | 34. Colocación en mesa sin rechazos por comodidad | 90 | Completed · Revisada; prueba en Quest 3 pendiente. Corrige un fallo visible en la demo: Seyra dejaba en el aire objetos que caben en la mesa y decía que no había sitio. |
| 3 | 29. Líneas guía y puntos de interés | 74 | Completed · Revisada; prueba en Quest 3 pendiente. Maquetas con etiquetas señaladas (planetas, piezas). |
| 4 | 27. Tarjetas que se pueden marcar | 72 | Completed · Revisada; pruebas con Muse y en Quest 3 pendientes. Convierte las tarjetas en listas de seguimiento. |
| 5 | 24. Movimiento paramétrico | 70 | Completed · Revisada; pruebas con Muse y en Quest 3 pendientes. Maquetas vivas muy vistosas. |
| 6 | 19. Imágenes generadas con Muse Image | 68 | Implementada; solo falta probarla (casos «No», calidad, latencia y Quest 3) y revisarla. |
| 7 | 28. Contenido dentro de la habitación | 66 | Completed · Revisada; pruebas con Muse y en Quest 3 pendientes. Evita paneles detrás de las paredes. |
| 8 | 32. Descarte de paneles y contenido agarrable | 65 | Quitar contenido sin pedirlo al chat. 32a (botón) Completed · Pendiente de revisión, con ambos cambios solicitados aplicados; 32b (papelera 3D) se diseña al cerrar 32a. |
| 8b | 33. Escuchar paneles y presentaciones | 63 | Reutiliza la voz de 18 y amplía su valor en la demo. Después de 32a. |
| 9 | 11–12. Captura y preguntas visuales | 64 | Completed · Revisada; por voz con `seyra_camera_look`. Captura confirmada en Quest 3; falta probar el recorrido completo. |
| 10 | 30. Indicador de scroll | 60 | Completed · Revisada. Pista y pulgar ocultos por alfa; PanelChecks 229 aprobadas, sin errores de rebuild. Prueba Quest pendiente. |
| 11 | 10a. Objetos interactivos | 50 | Diseño listo; después de 24. |
| 12 | 09. Gestión de contenido sin comandos | 42 | Útil, poco visible en la demo. |
| 13 | 13–16, 20, 21 | 15–30 | Dependen de captura y segmentación; fuera del alcance del hackathon. |

## P0 — Asistente espacial funcional

### 01. Validación del recorrido actual y resolución de bloqueos

**Estado:** completado en el Editor el 27 de octubre de 2026 con OpenCode real, GLM y DeepSeek. [Registro, pasos y resultados](Progress/Completed/01-validation.md).

**Resultado:** ambas conversaciones completaron consulta, creación, renombrado, movimiento, cambio de padre, eliminación, recuperación, deshacer y guardado/carga, con 50 llamadas confirmadas por el runtime. Corregida la selección de modelo del perfil al crear la sesión. Panel comprobado con permisos reales `read`: permitir una vez ejecuta la lectura y rechazar interrumpe el paso con error visible, sin reenvío. `seyra_*: allow` se conserva; no se ampliaron permisos de shell ni archivos. El incidente histórico original no pudo reproducirse ni atribuirse al plan gratuito. La validación en visor sigue correspondiendo a 07.

**Entrega:** comprobar con el OpenCode real que una conversación puede consultar, crear, renombrar, mover, eliminar, recuperar, deshacer y guardar/cargar contenido. Investigar la solicitud concreta que está entrando en `ask`; no atribuir el fallo al plan gratuito sin evidencia.

**Aceptación:** recorrido reproducible desde una sesión nueva; decisiones del panel confirmadas por el servidor cuando corresponda; errores visibles y sin reenvíos de mutaciones ambiguas; registro de los pasos y resultados. La regla `seyra_*: allow` permanece y no se amplían permisos de shell ni archivos.

**Dependencias:** ninguna.

### 01b. Superficie de herramientas cerrada para los perfiles Seyra

**Estado:** Completed · Revisada (28 de octubre de 2026). Lista blanca validada con OpenCode real en ambos perfiles mediante GLM, lectura env denegada y salida truncada permitida con excepción acotada. Recorrido Allende completado en Unity sin execute, question ni permisos. Repetición con modelo DeepSeek pendiente por HTTP 429 de cuota. [Implementación y evidencias](Progress/Completed/01b-agent-tool-allowlist.md).

**Entrega:** los perfiles `seyra` y `seyra_deepseek` pasan a lista blanca con `* deny` como primera regla: solo ven `seyra_*`, `websearch`, `webfetch` y `read`/`grep` para salidas truncadas, con `*.env` y `*.env.*` denegados. Desaparecen para el asistente el Code Mode (`execute`), los MCP heredados de la configuración global (`metavr`), `shell`, `question` y las skills.

**Aceptación:** `execute` no está disponible con el servidor real; un archivo `*.env.*` no se puede leer; el recorrido real «libros de Isabel Allende» se completa sin `execute`, `question` ni peticiones de permiso; ambos perfiles conservan permisos idénticos.

**Dependencias:** 01, 02.

### 02. Estado de ejecución, cancelación y continuidad del chat

**Estado:** completado en el Editor el 27 de octubre de 2026. [Implementación, límites y pruebas](Progress/Completed/02-execution.md).

**Resultado:** estados de conexión y turno separados, actividad de herramientas con resultados breves y sin razonamiento interno, cancelación confirmada por OpenCode, cierre explícito de turnos interrumpidos y reconexión a la misma sesión sin reenviar mensajes. Verificados 15 controles de fallos/carreras, decisiones de permisos sin duplicados y un recorrido real de creación–cancelación–continuación con DeepSeek. Corregido un reenvío implícito de POST en el transporte anterior. La continuidad dura mientras sobrevive la instancia del chat; no persiste conversaciones tras reiniciar la aplicación (21). La validación en visor permanece en 07.

**Entrega:** mostrar estados de envío, ejecución, espera de permiso, finalización y fallo; presentar herramientas ejecutadas y resultados breves, sin razonamiento interno. Añadir cancelación del turno y reconexión a la sesión existente cuando sea posible.

**Aceptación:** cancelar impide continuar con nuevas acciones del turno, sin afirmar que revierte las ya realizadas. Una desconexión no duplica mensajes ni herramientas; los resultados desconocidos se inspeccionan antes de repetir. Una petición de trabajo interrumpida queda identificada y puede cerrarse explícitamente.

**Dependencias:** 01. No implica implementar otro planificador: se aprovecha el bucle de herramientas de OpenCode.

### 03. Colores y apariencia básica de objetos

**Estado:** implementado y validado en el Editor el 27 de octubre de 2026; seis shaders compilados para Android (Vulkan y OpenGL ES 3). [Contrato, implementación y pruebas](Progress/Completed/03-appearance.md). La validación en visor permanece en 07.

**Resultado:** seis estilos cerrados (`matte_opaque`, `colored_translucent`, `unlit`, `ui_unlit`, `plastic`, `transparent`) con color RGBA uniforme. `set_appearance` reutiliza MCP, bridge, validación e historial; `scene_get` consulta el aspecto y su integridad. Material propio reutilizable por objeto, deshacer, papelera y persistencia v2 con carga compatible de v1. Alfa permitido solo en los tres estilos transparentes. Shader de UI preparado para Canvas; esta entrega no crea paneles ni incorpora herramientas genéricas para modificar UI existente.

**Entrega:** consultar y cambiar un color RGBA uniforme de primitivas gestionadas. Establecer un contrato pequeño para apariencia compatible con URP, historial y persistencia, sin edición arbitraria de materiales.

**Aceptación:** cambiar un objeto no altera otros que comparten material; deshacer restaura su aspecto; guardar/cargar reproduce el color; recursos creados se liberan correctamente. La transparencia se admite en los estilos declarados; los opacos rechazan alfa distinto de 1. Alfa 0 no contribuye al color visible y no desactiva colliders. Se verifican el recorte de UI y los límites del blending transparente convencional.

**Dependencias:** 01. Persistencia ampliada a apariencia gestionada; se siguen rechazando materiales arbitrarios.

### 04. Dimensiones y colocación relativa fiables

**Estado:** implementado y validado en el Editor el 27 de octubre de 2026. [Contrato y pruebas](Backend/bridge/BOUNDS.md). [Resultados](Progress/Completed/04-bounds.md). La validación en visor permanece en 07.

**Resultado:** `scene_bounds` calcula AABB globales del subárbol completo con matrices completas y esquinas de RectTransform world space. `place_relative` coloca en seis direcciones globales con separación explícita, independientemente del pivote; reutiliza validación, deduplicación, historial y persistencia de Transform. Comprobadas las seis primitivas, rotaciones, padres con escalas negativas/no uniformes y UI world space. Se rechazan geometría vacía/no soportada, UI de pantalla, referencias relacionadas y colocaciones no representables. Las envolventes no garantizan contacto exacto de superficies curvas/inclinadas; creación y persistencia de paneles permanecen en 05.

**Entrega:** consultar dimensiones y bounds globales; colocar contenido gestionado encima, debajo o junto a otro objeto con una separación explícita. Usar geometría real, no deducir el tamaño únicamente de `localScale`.

**Aceptación:** casos con rotaciones y padres escalados colocan los objetos según el contrato documentado; los casos no representables se rechazan; toda modificación reutiliza validación e historial. No incluye reconstrucción de muebles físicos a partir de una foto.

**Dependencias:** 01; compatible con 03.

### 05. Paneles de texto y etiquetas espaciales

**Plan de UI acordado:** cuatro fases: (1) biblioteca y ventanas; (2) paneles con historial y persistencia; (3) doce presentaciones completas mediante contratos semánticos; (4) chat espacial. La colocación mediante reconocimiento de habitaciones es una ampliación posterior, no un requisito del chat espacial.

**Fase 1:** implementada y validada en el Editor el 27 de octubre de 2026: 20 componentes semánticos, 39 controles nativos de Meta, ventanas manipulables y escenas de catálogo/demostración independientes. 436 comprobaciones en Edit Mode y 14 en Play Mode aprobadas. [Resultado y límites](Progress/Completed/05-ui-phase-1.md). Pendiente la prueba física de lectura y manipulación en visor; esta fase no añade herramientas MCP, contenido persistible ni modifica SeyraAssistant.

**Fase 2:** implementada y validada en el Editor el 27 de octubre de 2026. `create_panel` y `update_panel` crean y editan paneles de texto o etiquetas con contratos completos. Integrados con identidad, peticiones, límites geométricos, colocación, papelera y deshacer existentes. Persistencia v3 con lectura de v1/v2; los controles internos se reconstruyen y cada panel ocupa un nodo lógico. 217 comprobaciones de paneles aprobadas, regresiones de ciclo de vida/materiales/bounds y recorrido MCP real verificados. [Contrato](Backend/bridge/PANELS.md) y [resultados](Progress/Completed/05-ui-phase-2.md). Pendiente la validación física en visor. El chat permanece en fase 4.

**Entrega:** crear y editar texto en paneles y etiquetas dentro de `Generated`: contenido, dimensiones, tamaño tipográfico y pose. Consultarlos y manipularlos mediante las herramientas existentes siempre que sea posible.

**Aceptación:** texto legible, ajuste de líneas y layouts sin solapamientos; límites de longitud y tamaño; eliminación, deshacer y guardado/carga completos. No interpretar HTML o markup arbitrario enviado por el modelo.

**Dependencias:** 03 y 04. Extender el formato versionado de contenido con migración o rechazo claro de versiones incompatibles.

**Fase 3:** implementada y validada en Editor: doce presentaciones completas, nueve herramientas UI, respuestas y navegación locales persistibles, reemplazo y deshacer compatibles con el historial existente. Persistencia v4 con lectura v1–v3. 209 comprobaciones específicas y recorrido MCP real para las doce variantes aprobados. [Contrato](Backend/bridge/PRESENTATIONS.md) y [validación](Progress/Completed/05-ui-phase-3.md). Pendiente la validación física en visor; chat espacial corresponde a fase 4.

**Fase 4:** implementada y validada en el Editor el 27 de octubre de 2026. Ventana de chat espacial (`SeyraSpatialChatWindow`) y menú de asistente (`SeyraSpatialMenu`) funcionales en `SeyraAssistant.unity`, con manipulación espacial nativa (`UIXRWindow`), botón de pulsar para hablar con dictado por voz mediante Meta Voice SDK (`PushToTalkButton` y `VoiceDictation`), soporte de entrada de texto (`TMP_InputField`), proveedor unificado de credenciales locales (`SeyraEnvironmentCredentials`), estado de conexión a OpenCode diferenciado del runtime de escena, visualización de turnos, actividades y solicitudes de permiso legibles con acciones explícitas (`once`/`reject`), cancelación y cierre explícito sin reclamar reversiones. Motor unificado de conversación (`OpenCodeConversationSession`) compartido sin duplicación entre el testbed `OpenCodeTestChat` y la ventana espacial. Ocultar y reabrir la ventana conserva la sesión sin duplicar conexiones ni controladores. 11 grupos de comprobaciones en Play Mode (`SpatialChatChecks.Run`) y 15 comprobaciones de fallos del chat base (`ChatExecutionChecks.Run`) aprobados. [Validación y límites](Progress/Completed/05-ui-phase-4.md). Pendiente la validación física en visor (07).

### 06. Menú y chat funcionales en SeyraAssistant

**Estado:** completado en el Editor el 27 de octubre de 2026. [Informe y evidencias](Progress/Completed/05-ui-phase-4.md).

**Entrega:** llevar las funciones del chat de prueba a una UI espacial mínima: conversación, entrada, conexión, actividad, permisos, cancelar y abrir/cerrar panel. Tomar SeyraChat como referencia de comportamiento, creando una estructura propia con layouts.

**Aceptación:** se puede completar una conversación en el visor con los métodos de entrada elegidos, sin depender del Inspector ni del teclado del PC; los paneles no se superponen y pueden recolocarse. `OpencodeTestUI` se conserva como banco de pruebas.

**Dependencias:** 02 y 05. Reutiliza la biblioteca visual de las fases de UI; no requiere un rediseño visual completo ni reconocimiento de habitaciones.

### 06b. Compositor por voz: dictado a borrador y envío explícito

**Estado:** Completed · Revisada (28 de octubre de 2026). Compositor de solo voz con borrador de solo lectura y envío explícito, validado en el Editor el 27 de octubre de 2026: 11 grupos de chat espacial y 15 de regresión aprobados. Prueba manual con micrófono del PC confirmada por el usuario el 27 de octubre de 2026: al dictar dos veces antes de enviar se utiliza el segundo dictado. Pendiente la validación física en visor (07). [Implementación y evidencias](Progress/Completed/06b-voice-draft-composer.md).

**Entrega:** el compositor del chat espacial pasa a ser solo voz: la transcripción queda como borrador de solo lectura y se envía con Enviar. Sin envío automático tras dictar ni botón de borrar.

**Aceptación:** dictar nunca envía un mensaje; Enviar envía el borrador una vez; un dictado fallido conserva el borrador anterior; se puede dictar durante un turno.

**Dependencias:** 06. Adelanta la parte de "texto antes de enviar" de 17; la edición y Muse Voice Transcribe siguen en 17. Se implementa en secuencia con 06c (mismos prefabs y escena).

### 06c. Seguimiento perezoso del menú espacial

**Estado:** Completed · Revisada (28 de octubre de 2026). Implementación validada en el Editor y funcionamiento manual confirmado por el usuario el 27 de octubre de 2026. Pendientes la validación física en visor (07); amortiguación unificada (0,2 s, 1,8 m/s) con llegada medida en 0,914 s. [Implementación, validación y limitaciones](Progress/Completed/06c-menu-lazy-follow.md).

**Nota (29 de octubre de 2026):** el seguimiento del menú quedó sustituido por el menú de mano (06e); el componente de lazy follow sigue en uso para el chat y los layouts.

**Entrega:** componente de lazy follow propio para el menú (el SDK 207.0.1 no trae uno): zona cómoda bajo la mirada, movimiento amortiguado, respeto del agarre y de la posición elegida al soltar, y "Reubicar" que coloca chat y menú.

**Aceptación:** el menú vuelve a su sitio tras girar o desplazarse, no se mueve con giros breves ni mientras se agarra; el chat no se mueve solo.

**Dependencias:** 06. Base del seguimiento de 22d.

### 06d. Ocultar el menú espacial cuando el chat está a la vista

**Estado:** Completed · Revisada (28 de octubre de 2026). Validado en el Editor el 27 de octubre de 2026: 4 comprobaciones de visibilidad, 12 grupos de seguimiento y 11 de chat aprobados. Pendientes la prueba manual del usuario con Meta XR Simulator y la validación física en visor (07). [Implementación y evidencias](Progress/Completed/06d-menu-hide-when-chat-visible.md).

**Nota (29 de octubre de 2026):** sustituida por 06e. El menú ya no flota delante del usuario, así que esta ocultación no se aplica.

**Entrega:** el menú con seguimiento perezoso se oculta mientras el chat abierto está en la zona central de la vista y reaparece en su pose de reposo al apartar la mirada o cerrar el chat. Histéresis y retardos para evitar parpadeo; el seguimiento de 06c sigue activo mientras está oculto. El menú en la mano queda descartado en esta iteración.

**Aceptación:** con el chat delante, el menú no se ve ni bloquea rayos hacia el chat; al girar o mirar al suelo reaparece en menos de 0,5 s; al cerrar el chat aparece de inmediato; mirar el borde del chat no lo hace parpadear.

**Dependencias:** 06c. Se implementa en secuencia con 22c/22d (mismos prefabs y escena).

### 06e. Menú de mano

**Estado:** Completed · Revisada (28 de octubre de 2026). Menú de mano en la izquierda (palma o botón de menú del mando, acción QuestButtons/Menu): una fila de tres botones de icono (Chat, Voz, Traer chat) de unos 0,15 × 0,08 m. El escaneo de habitación sale del menú: la habitación debe estar escaneada antes de abrir Seyra. HandMenuChecks (5) y SpatialFollowChecks (12) aprobados en Editor. Validación física en 07. [Implementación y revisión](Progress/Completed/06e-hand-menu.md).

**Entrega:** el menú del asistente pasa a la mano izquierda: aparece al mostrar la palma o con el botón de menú del mando izquierdo (acción de Input System asignable en el inspector). Fila compacta de tres botones de icono (Chat, Voz, Traer chat) con línea de estado; cada botón da una respuesta visible. Sustituye el dock con seguimiento de 06c/06d para el menú.

**Aceptación:** el menú aparece junto a la mano en menos de 0,3 s y desaparece sin parpadeo; mide entre 0,13 y 0,17 m de ancho; "Traer chat" coloca el chat delante o explica por qué no puede; el menú no reserva espacio ni bloquea la colocación.

**Dependencias:** 06, 06b, 22d/22e.

### 06f. Chat sin maquinaria visible

**Estado:** Completed · Revisada (29 de octubre de 2026). Chat con actividad transitoria y detalle técnico oculto por defecto; compilación C#, SpatialChatChecks (12 grupos) y ChatExecutionChecks (15 grupos) aprobados. Prueba manual en Quest pendiente. [Implementación y validación](Progress/Completed/06f-transparent-chat-activity.md).

**Entrega:** el chat espacial muestra solo la conversación. Las llamadas a herramientas y los pasos intermedios del modelo se sustituyen por una línea de actividad transitoria ("Pensando…", "Mirando tu habitación…", "Colocando el contenido…"). Los fallos se explican sin JSON. Un interruptor de depuración recupera el detalle técnico. El prompt del agente ya pide no narrar la maquinaria (29 de octubre de 2026).

**Aceptación:** durante un turno con herramientas solo se ve la línea de actividad; al terminar queda una única respuesta; ningún error muestra JSON; el banco de pruebas conserva el detalle técnico.

**Dependencias:** 02, 06, 06e.

### 07. Conexión y ejecución verificadas en Quest

**Estado:** Completed · Revisada (29 de octubre de 2026). APK independiente en Quest 3 conectado por la pasarela HTTPS emparejada: chat, herramientas reales, habitación MRUK real, `scene_build` en la mesa y layouts en pared probados por el usuario. Limitación: el build de prueba para móvil puede dejar modificada la configuración XR; comprobar `git status` después. [Implementación y validación](Progress/Completed/07-quest-connection.md).

**Entrega:** configurar desde la aplicación la dirección del backend, autenticar la conexión sin incluir secretos de proveedores en assets o builds y verificar el enlace Unity–bridge fuera del Editor. El backend sigue ejecutándose en un equipo accesible por red.

**Aceptación:** build instalado en el dispositivo objetivo que conecta con el backend y completa conversación, herramientas y recuperación tras pausa/reanudación; estado independiente de conexión al chat y al runtime; errores de red comprensibles. La conexión es cifrada y autenticada por dispositivo y funciona con cualquier IP o puerto; OpenCode y el bridge nunca se exponen fuera de `127.0.0.1`; el build no contiene secretos y el visor solo guarda la URL, su token y la huella del certificado.

**Dependencias:** 06. La solución de transporte y credenciales debe cerrarse antes de considerar entregado el MVP.

### 07b. CLI multiplataforma del backend

**Estado:** Completed · Revisada (29 de octubre de 2026). CLI y envoltorios validados en Windows con raíz temporal, MCP y HTTPS conectados y puertos libres tras Ctrl+C. Suite ejecutada una vez: 12/13, fallo inicial del polyfill corregido y recorrido PEM/emparejamiento comprobado después sin repetir la suite. Pendientes push con dispositivo real, macOS y Linux. [Implementación y límites](Progress/Completed/07b-backend-cli.md).

**Entrega:** `node Backend/seyra.mjs` con `start`, `init-tls`, `pair`, `list`, `revoke` y `push` (copia el emparejamiento al dispositivo con adb) en Windows, macOS y Linux; `opencode.json` sin rutas del PC del usuario; `start.ps1` y `seyra-device.ps1` como envoltorios.

**Aceptación:** el backend arranca desde cualquier carpeta del repositorio; Ctrl+C no deja procesos huérfanos; certificados y dispositivos anteriores siguen funcionando.

**Dependencias:** 07 y 08.

### 08. Muse Spark como proveedor utilizable

**Estado:** Completed · Pendiente de revisión (28 de octubre de 2026). Muse Spark configurado mediante proveedor Meta Responses explícito; GLM conservado en seyra_glm. Compilación y 8/9 pruebas correctas; transporte pendiente por EACCES local. Recorrido real con Muse Spark completado en Editor: chat, cubos con etiquetas anchor, libros en arc y undo; 14 herramientas confirmadas. [Implementación, desviaciones y validación](Progress/Completed/08-muse-spark.md).

**Entrega:** integrar Muse Spark mediante Meta Model API, preferentemente como proveedor de OpenCode si su versión y la API lo permiten. Conservar schemas, executors y bridge. Si falta soporte, resolver primero esa compatibilidad con una prueba pequeña antes de crear otra arquitectura de agentes.

**Aceptación:** acceso real a la API y modelo configurado explícitamente; conversación y llamadas estructuradas completan el mismo recorrido de 01; errores de autenticación, cuota y proveedor se muestran sin duplicar acciones; claves solo en backend. No crear variantes de perfil que difieran accidentalmente en permisos o capacidades.

**Dependencias:** 01 y 02; puede investigarse antes de terminar 07, pero no bloquea el resto del contenido espacial. Esta entrega valida texto y tools; la visión se valida en 12.

### 09. Contenido generado utilizable sin comandos técnicos

**Entrega:** lista de contenedores de peticiones y controles básicos para seleccionar contenido, deshacer la última operación/petición, recuperar eliminaciones y guardar/cargar slots.

**Aceptación:** el usuario puede identificar qué creó cada petición y recuperar un trabajo guardado tras reiniciar; la UI usa los mismos servicios de dominio que las herramientas. La carga explica que sustituye el contenido generado actual. No se pierde contenido no soportado silenciosamente.

**Dependencias:** 05–07. La persistencia incluye colores y paneles, pero todavía no anclajes físicos ni historial entre sesiones.

**Cierre P0:** en el visor, pedir una composición de primitivas con colores y explicación espacial; corregirla en varios turnos, renombrarla, borrar y deshacer; guardar y volver a cargar tras reiniciar. Validar el flujo con Muse Spark para cerrar el objetivo de integración original, conservando el proveedor existente como alternativa de desarrollo.

### 31. Seyra en inglés

**Estado:** Completed · Revisada (30 de octubre de 2026). Tres suites repetidas y aprobadas: ChatExecutionChecks (15 grupos), SpatialChatChecks (12 grupos) y PresentationChecks (242 aserciones, Meta XR Simulator). Contrato ASR corregido y renombrado WITAI_CLIENT_TOKEN confirmado por separado. Recorrido manual pendiente. [Implementación y revisión](Progress/Completed/31-english-experience.md).

**Entrega:** interfaz, escena, prefabs, errores, textos de respaldo, respuestas del modelo y dictado en inglés, sin capa de localización.

**Aceptación:** ningún texto visible en español en el recorrido principal; Seyra responde en inglés aunque le hablen en español; referencias serializadas conservadas.

**Dependencias:** ninguna. Requisito de 18.

## P1 — Comprensión e interacción con el entorno

### 22. Habitación, muebles y colocación espacial

**Estado:** 22a–22e Completed · Revisada (28 de octubre de 2026), en `main`. Inventario MRUK de solo lectura, colocación relativa en marcos world/viewer/reference, planificador pared/mesa/aire con reservas, acople al soltar ventanas y seguimiento del chat. Validado en Editor (habitación 689, colocación 50, acople 23, seguimiento 14 comprobaciones). Pendiente la validación física en Quest (07). [Diseño e historial de revisiones](Progress/Completed/22-room-spatial-placement.md). 22f Completed · Revisada (29 de octubre de 2026): legibilidad por panel, motivos en `PLACEMENT_REJECTED` y guía de `billboard`. Prueba Muse: sistema solar en la mesa con un `scene_build` y 3 llamadas (23 s, antes 125 s y 13), sin rechazos; layouts sin `billboard`. Pendiente probar `scene_edit` con Muse. [Implementación y límites 22f](Progress/Completed/22f-readable-placement-fixes.md).

**Orden:** después de 07 y antes de 10. La parte verificable en Editor con habitaciones de prueba puede avanzar en paralelo a 07; la validación física depende de 07.

**Entrega:** cargar la habitación capturada con MRUK y exponer al modelo paredes, aberturas y muebles con etiqueta, tamaño y posición respecto al usuario, como referencias de solo lectura. Ampliar `scene_bounds` y `place_relative` a referencias físicas y a direcciones relativas al usuario o a la referencia. Planificador que elige pared, mueble o aire con descartes y puntuación, aplicado mediante herramientas con historial. Acople de ventanas al soltarlas y seguimiento perezoso del chat. Guía en el prompt para usar el espacio solo cuando aporta valor.

**Aceptación:** sin permiso o sin mapa el chat sigue funcionando y la colocación lo declara; peticiones como «encima de la mesa», «a la derecha del sofá» o «en la pared» se resuelven en una conversación real y se pueden deshacer; los anclajes de la habitación no se pueden modificar; el chat no se mueve durante la interacción ni por giros breves. Los resultados se presentan como compatibles con la geometría capturada, no como espacio libre garantizado.

**Dependencias:** 03–06; validación en visor tras 07. Sirve de fuente espacial para 13, 15 y 16.

### 23. Layouts espaciales para colecciones

**Estado:** 23a y 23b Completed · Revisada (28 de octubre de 2026); 23c (guía del prompt) aplicada. Layouts flotantes, de pared y de mesa con persistencia v5. Validado en Editor (LayoutChecks 44 el 29 de octubre de 2026 con simulador; planificador 1304). Uso real con Muse Spark comprobado el 29 de octubre de 2026 (libros, recetas en pared, películas). Pendiente la validación física en Quest. [Diseño](Progress/Completed/23-spatial-layouts.md).

**Entrega:** herramientas `layout_plan`, `layout_create` y `layout_apply` que crean o reorganizan N tarjetas del mismo tamaño en una llamada, con las disposiciones arc, plane, hub, surround, anchor, wall y surface, y los comportamientos fijo, alineado, billboard y lazy follow. El sistema calcula las poses, también el reparto entre paredes. El prompt sustituye la receta manual de colecciones por una guía de decisión con ejemplos.

**Aceptación:** una petición como «muéstrame los libros de Isabel Allende» produce tarjetas distribuidas en una sola llamada; las preguntas simples siguen en el chat; sin solapes; si no cabe no se crea nada y se informa de la capacidad; deshacer, guardado y carga conservan el layout.

**Dependencias:** 05, 06c, 22a y 22b.

### 25. Composición y edición de escena en una llamada

**Estado:** Completed · Revisada (29 de octubre de 2026). Pasos 1–7 en `main`; SceneBatchChecks 40, LayoutChecks 44 y 23 tests del bridge aprobados. Contrato plano con `parentKey` por el rechazo de esquemas recursivos de Meta. Prueba Muse en Editor (`swift-moon`, perfil `seyra`, `muse-spark-1.3`): maqueta correcta en la mesa tras 125 s y 13 llamadas; rechazo de maquetas con etiquetas y falta de motivos en `PLACEMENT_REJECTED` derivados a [22f](Progress/Completed/22f-readable-placement-fixes.md). Marvel correcto con una llamada a `layout_create`. Pendiente la validación física en Quest (07). [Implementación y validación](Progress/Completed/25-scene-build-batch.md).

**Orden:** antes de 24 (comparten `SceneHistory.cs` y `SceneTools.cs`).

**Entrega:** `scene_build` crea una composición de contenedores, primitivas con apariencia y etiquetas (lista plana de nodos con `parentKey`), con colocación relativa entre nodos y colocación del conjunto (aire, mesa o pared), en una llamada atómica. `scene_edit` aplica hasta 64 cambios sobre objetos existentes en una llamada. Ambas se deshacen como una operación.

**Aceptación:** una maqueta del sistema solar con etiquetas se crea con 3 llamadas como máximo (antes, 36 llamadas y 131 s el 29 de octubre de 2026); cambiar varias paredes es una llamada; un error no deja nada creado ni modificado; deshacer, guardado y carga conservan la composición.

**Dependencias:** 03, 04, 05, 22 y 23.

### 34. Colocación en mesa sin rechazos por comodidad

**Estado:** Completed · Revisada (31 de octubre de 2026). C# sin errores; SpatialPlannerChecks 23, LayoutPlannerChecks 1304, SpatialPlacementChecks 50 con Meta XR Simulator y SceneBatchChecks 45 aprobadas. Pendiente la prueba del plato en Quest 3 mirando a la mesa y de espaldas. [Implementación y validación](Progress/Completed/34-tabletop-placement-relaxed.md).

**Orden:** antes de 28, que modifica el mismo planificador.

**Entrega:** si el usuario pide una mesa y el objeto cabe físicamente, se coloca allí aunque quede fuera del ángulo cómodo; se elige el mejor punto posible y solo la geometría real lo impide. Rejilla de puntos ajustada al tamaño del objeto, resumen de rechazos por superficie, reservas sin hijos inactivos y guía del prompt para explicar el motivo real y medir la mesa con su polígono.

**Aceptación:** un plato de 0,22 m en una mesa de 0,945 × 1,414 m queda en la mesa aunque el usuario mire a otro lado, y Seyra lo avisa; si hay un punto cómodo se usa ese; un objeto mayor que la mesa o una mesa ocupada no se fuerzan; paredes, layouts, acople y seguimiento no cambian. Pendiente la prueba del usuario en Quest 3.

**Dependencias:** 22, 22f y 25.

### 24. Movimiento paramétrico de contenido generado

**Estado:** Completed · Revisada (1 de noviembre de 2026), en `main`. Persistencia v7. MotionChecks 95, SpatialPlacementChecks 50, bridge 33 y C# aprobados. Pendientes Muse y Quest 3. [Implementación y revisión](Progress/Completed/24-parametric-motion.md).

**Orden:** después de 25 y 29 (comparten historial, `scene_build` y persistencia) y antes de 10a.

**Entrega:** `motion_set`, `motion_remove` y `motion_control` para girar sobre sí mismo (spin), orbitar alrededor de un objeto o punto (orbit) y oscilar (bob), con pausa, reinicio, deshacer y persistencia v7. `scene_build` crea la maqueta animada en una llamada, y la colocación reserva todo el recorrido. Las etiquetas acompañan al objeto mediante un contenedor portador. Pensado para maquetas vivas como un sistema solar.

**Aceptación:** planetas que orbitan y giran creados en una llamada a `scene_build`, con etiqueta y línea que los acompañan; una luna orbita un planeta en órbita sin temblar; mover o escalar la raíz conserva las órbitas; deshacer sin conflictos ni saltos; guardado y carga conservan las animaciones; las mutaciones sobre un canal animado se rechazan con un mensaje claro.

**Dependencias:** 05, 23a, 25 y 29. Lo usa 10a.

### 26. Tarjetas con imagen de internet

**Estado:** Completed · imágenes web retiradas (30 de octubre de 2026). 26c y 26d quitan la búsqueda, `og:image`, `imageSearch`, `imageUrl` y el resolver, por las normas del hackathon; `mediaCard` y `RemoteMediaStore` siguen. Los cambios 11.3/11.4 se cierran sin revisión final de código. [Implementación y revisión](Progress/Completed/26-web-image-cards.md).

**Estado 26b:** Future · pausada (30 de octubre de 2026). Pexels ha pausado la emisión de claves. Las herramientas solo aceptan imágenes del catálogo registrado; el prompt no usa imágenes web, para cumplir la norma del hackathon sobre assets sin derechos. [Diseño](Progress/Future/26b-pexels-stock-images.md).

**Estado 26c:** Completed · Revisada (30 de octubre de 2026). Retiradas la extracción de imágenes HTML y las fuentes web del schema; conservados buscador interno, miniatura de YouTube y transportes. Suite del bridge 33/33. Prueba manual pendiente. [Implementación](Progress/Completed/26c-remove-web-image-sources.md).

**Estado 26d:** Completed · Revisada (30 de octubre de 2026). Retirados búsqueda web, miniaturas y resolver de imágenes; conservado el cargador seguro por URL sin llamadas en producción. Suite del bridge 28/28. Prueba manual pendiente. [Implementación](Progress/Completed/26d-remove-web-image-resolver.md).

**Entrega:** tarjeta `mediaCard` con imagen fija y texto desplazable, sola con `ui_present` o en `layout_create`. `imageSearch` es una consulta web resuelta por el buscador de Meta; `imageUrl` permite una imagen o página ya conocida. El bridge descarga, valida y reduce a JPEG de hasta 512 px; Unity recibe bytes por el transporte autenticado y los guarda en caché, sin conexiones web desde el visor.

**Aceptación:** «muéstrame las películas de la fase tres» crea tarjetas con su póster y su resumen; una URL caída, no permitida o demasiado grande muestra la tarjeta sin imagen y lo indica en `missingMedia`, sin bloquear la creación; memoria de texturas acotada y liberada al borrar; guardar/cargar conserva la referencia y recupera la imagen de la caché. Unity nunca descarga directamente de internet.

**Dependencias:** 05 (presentaciones), 07 (pasarela) y 23 (layouts). Base de 19. Contrato vigente: [medios web](Backend/bridge/MEDIA.md) y [presentaciones](Backend/bridge/PRESENTATIONS.md).

### 27. Tarjetas que se pueden marcar

**Estado:** Completed · Revisada (1 de noviembre de 2026). Tarjetas marcables y card_checks implementados; compilación C#, bridge 30/30, CardCheckChecks 35, PanelChecks 229, PresentationChecks 242 y LayoutChecks 44 aprobadas. Pruebas con Muse Spark y en Quest 3 pendientes para el usuario. [Implementación](Progress/Completed/27-checkable-cards.md).

**Entrega:** un interruptor opcional (`check`) en los paneles de texto y en `infoPanel`, `technicalSheet` y `mediaCard`, con un texto que elige el modelo según el contenido («Got it» en ingredientes, «Watched» en películas, «Done» en pasos), con estado local persistente. La guía del prompt decide cuándo no hay interruptor (fases de Marvel, normas de un deporte). El modelo lee todas las marcas en una llamada con `card_checks` cuando el usuario pregunta («¿qué pelis me quedan?») y puede fijarlas al crear las tarjetas.

**Aceptación:** pulsar el interruptor cambia su estado al momento sin abrir un turno de chat; el estado se conserva al ocultar, mover, guardar y cargar; las tarjetas sin `check` y los slots anteriores no cambian; el modelo consulta las marcas en lugar de preguntar al usuario; `update_panel`/`ui_update` conservan la marca si omiten `completed`.

**Dependencias:** 05 y 23. Comparte la mecánica de interacción local de las presentaciones.

### 28. Contenido dentro de la habitación

**Estado:** Completed · Revisada (1 de noviembre de 2026). Layouts en el aire buscan distancia, filas y giro dentro del suelo y las paredes capturados, con 5 cm de margen. SpatialPlannerChecks 25, LayoutPlannerChecks 1309, SpatialPlacementChecks 50, LayoutChecks 44, SceneBatchChecks 45 y bridge 37/37 aprobadas. Pendientes Muse y Quest 3. [Implementación y revisión](Progress/Completed/28-content-inside-room.md).

**Entrega:** ninguna colocación automática (layouts en el aire, `spatial_place` en aire, `scene_build` con `placement`, seguimiento del chat) deja contenido detrás de una pared o fuera del suelo capturado. MRUK 207 cierra la habitación aunque las paredes tengan aberturas: `MRUKRoom.GetRoomOutline()` devuelve el contorno del suelo e `IsPositionInRoom()` comprueba un punto. El planificador acerca el contenido, reduce el radio de los layouts o lo coloca en otra dirección; si no cabe, lo informa.

**Aceptación:** en una habitación pequeña, un arco de tarjetas o una maqueta en el aire quedan dentro con un margen comprobado; sin habitación capturada se mantiene el comportamiento actual y se declara; la validación usa las habitaciones de prueba de MRUK en el Editor.

**Dependencias:** 22, 23 y 25. Comprobar en el diseño el comportamiento de `GetRoomOutline` con varias plantas o habitaciones (High Fidelity scene).

### 29. Líneas guía y puntos de interés

**Estado:** Completed · Revisada (1 de noviembre de 2026). Líneas y puntos con persistencia v6, en `main`. Compilación C#, LineChecks 34 y SceneBatchChecks 45 aprobadas. Pendiente la prueba en Quest 3. 24 pasa a persistencia v7. [Implementación y revisión](Progress/Completed/29-guide-lines-points.md).

**Entrega:** dos elementos generados nuevos. (1) Línea: `LineRenderer` entre dos o más puntos, con material sencillo sin iluminación, color de la paleta de 03 y grosor por defecto de 0,01 m. Cada extremo puede ser un punto fijo o una referencia a un objeto generado (`targetId`, o `key`/`parentKey` dentro de `scene_build`), de modo que la línea sigue al objeto si este se mueve. (2) Punto de interés: esfera pequeña con radio elegible por el modelo (1 cm por defecto) colocada sobre la superficie de otro objeto. Caso de uso principal: al generar planetas, un punto de interés sobre cada planeta y una línea desde el punto hasta su etiqueta de texto. Disponible como herramienta individual y como tipo de nodo en `scene_build`/`scene_edit` (`Backend/bridge/BATCH.md`).

**Aceptación:** «crea el sistema solar con etiquetas» produce puntos de interés sobre los planetas unidos por líneas a sus etiquetas, en la misma llamada de `scene_build`; las líneas se actualizan si el planeta o la etiqueta se mueven (incluido el movimiento de 24); grosor y radio tienen límites validados; borrar un extremo elimina o desactiva la línea de forma declarada; deshacer, papelera y guardado/carga completos.

**Dependencias:** 03, 05 y 25. Compatible con 24 (líneas que siguen objetos en órbita). Hoy solo existe un `LineRenderer` interno de previsualización en `SpatialWindowSnapping.cs`.

### 30. Indicador de scroll en paneles, tarjetas y chat

**Estado:** Completed · Revisada (2026-10-30). Pista y pulgar se ocultan mediante alfa del CanvasRenderer, sin SetActive; geometría, colores, eventos y Ensure conservados. Compilación con 0 errores; PanelChecks 229 aprobadas y consola sin errores de rebuild de ScrollIndicator. Prueba física en Quest pendiente. [Implementación y revisión](Progress/Completed/30-scroll-indicator.md).

**Entrega:** barra fina con el pulgar y degradado con flecha cuando hay más texto abajo, en paneles de texto, presentaciones (incluida `mediaCard`) y chat espacial. Solo visual, añadido en tiempo de ejecución sin regenerar prefabs.

**Aceptación:** visible solo cuando el contenido no cabe; el degradado desaparece al llegar al final; no interfiere con el agarre ni los clics.

**Dependencias:** 05 y 06. Después de 26 o coordinado con ella (comparten `UIPresentationView.cs`).

### 32. Descarte de paneles y contenido agarrable

**Estado 32a:** Completed · Pendiente de revisión (1 de noviembre de 2026). Aplicados ambos cambios de revisión: Gap desactivado en botones de solo icono sin regenerar autoría; compilación con 0 errores y 0 advertencias, PresentationChecks 251 y CardCheckChecks 35 aprobados. Validación visual y con Muse en Quest pendiente. [Implementación y revisión](Progress/Completed/32a-panel-close-button.md).

**Estado 32b:** Future · esbozo. Diseñar al cerrar 28 y 32a. [Esbozo](Progress/Future/32b-floor-trash-bin.md).

**Entrega:** el usuario retira paneles y presentaciones directamente, sin pedirlo al chat, en dos pasos:

- **32a. Botón «Close»:** cada panel de texto y cada presentación muestran un botón que envía la ventana a la papelera de runtime. Sustituye el cierre actual de las presentaciones, que solo las oculta.
- **32b. Papelera 3D:** aparece en el suelo al agarrar una ventana, siempre dentro de la habitación, y desaparece al soltarla. Al meter la ventana dentro se elimina con una pequeña animación que la encoge desde una distancia N (por ejemplo, a partir de 50 cm).

Solo las ventanas son agarrables hoy: las etiquetas y las primitivas se retiran por chat.

**Aceptación:** el descarte usa el mismo circuito que `delete_object`, de modo que se puede deshacer y recuperar y el guardado no conserva el contenido retirado. La ventana de chat no se puede descartar. Con papelera 3D: soltar fuera de la zona no elimina nada, la animación se revierte si la ventana sale antes de soltarla, `undo` la devuelve a su escala original, y sin habitación capturada no aparece la papelera y sigue disponible el botón.

**Dependencias:** 05, 22 y 25. 32a después de 31. 32b después de 28, que aporta la comprobación de posición dentro de la habitación, y de 32a. Si se implementa 10a antes, coordinar el agarre.

### 33. Escuchar paneles y presentaciones

**Estado:** sin diseñar (30 de octubre de 2026). Prioridad media (puntuación 63).

**Entrega:** cada panel de texto y cada presentación muestran un botón «Listen» junto a «Close» (32a). Al pulsarlo se lee su contenido con la misma voz de 18 (`wit$Rebecca`), y pasa a «Stop» mientras suena. Nunca se lee un panel sin que el usuario lo pida.

**Aceptación:** pulsar «Listen» lee el panel sin abrir un turno de chat; solo hay una voz a la vez, de modo que una lectura sustituye a la anterior, también a la del chat, y una respuesta nueva del chat detiene la del panel; «Stop», dictar o retirar el panel la cortan; «Listen» funciona aunque la lectura automática del chat esté silenciada con el botón de voz (18b); un fallo de voz deja el panel intacto con un aviso breve; el botón no se guarda como contenido ni afecta a deshacer, papelera o slots.

**Al diseñar, decidir:** qué texto se lee en cada una de las trece presentaciones; cómo se recorta un panel largo, porque el aviso actual de `SpeechText` («The rest is in the chat.») no vale aquí; y cómo acceden los paneles a `SpokenReplies`, que desde 18b no depende de que el chat esté abierto.

**Dependencias:** 05, 18 y 32a (misma zona de botones; implementar después).

### 10. Selección e interacción bidireccional con contenido virtual

**Estado:** 10a (objetos interactivos con panel de detalle: selección por rayo, agarre opcional con vuelta al soltar, ficha que sigue al objeto) In progress · diseño listo, después de 24. 10b (selección como contexto del mensaje y manipulación integrada en el historial) sin diseñar. [Diseño 10a](Progress/InProgress/10a-interactive-objects.md).

**Entrega:** seleccionar objetos o paneles y enviar su `entityId` como contexto explícito del siguiente mensaje. Añadir indicación visual de selección y manipulación manual básica mediante el rig existente.

**Aceptación:** «mueve esto» afecta al objeto seleccionado; seleccionar no cambia su identidad; moverlo manualmente se integra con el historial o queda marcado como cambio externo, sin sobrescribirlo al deshacer. Una selección eliminada o de otro runtime se invalida.

**Dependencias:** cierre P0. Se adelanta respecto a la segmentación porque ya aporta valor con objetos virtuales.

### 11. Captura puntual de cámara del entorno

**Estado:** Completed (Editor; Quest pendiente) · Revisada (1 de noviembre de 2026), junto con 12. Por voz, sin botones, con `seyra_camera_look`. Captura, orientación, sonido y panel confirmados en Quest 3; recorrido completo pendiente. [Diseño e implementación](Progress/Completed/11-12-camera-visual-questions.md).

**Entrega:** obtener una imagen RGB con la Passthrough Camera API, con permiso, cuando el usuario lo pida por voz, con sonido de obturador y panel que muestra la foto. Asociar `captureId`, cámara de origen, resolución, timestamp y metadatos de pose/calibración disponibles en el instante de captura.

**Aceptación:** captura y orientación correctas en el visor; permiso denegado o cámara no disponible no bloquean el chat; pausar/reanudar libera y recupera recursos. No iniciar streaming continuo ni presentar una captura de la escena virtual como foto real.

**Dependencias:** 07. Verificar compatibilidad del SDK instalado y dispositivo antes de implementar; la API de cámara es distinta del passthrough visual y de la captura del framebuffer.

### 12. Preguntas visuales con Muse Spark

**Estado:** Completed (Editor; Quest pendiente) · Revisada (1 de noviembre de 2026), junto con 11. El bridge pregunta a Muse Spark con la foto y devuelve solo texto: 42/42 en el bridge y 7/7 en CameraCaptureChecks. Recorrido con Muse en Quest 3 pendiente. [Diseño e implementación](Progress/Completed/11-12-camera-visual-questions.md).

**Entrega:** responder a preguntas sobre lo que el usuario tiene delante mediante una llamada de visión aparte desde el bridge, que devuelve texto al agente, controlando resolución y tamaño del envío.

**Aceptación:** el modelo responde sobre la captura correcta, distingue observación de inferencia y solicita otra imagen cuando no hay información suficiente. La imagen no se reenvía en cada turno sin necesidad; las referencias a capturas caducadas se identifican.

**Dependencias:** 08 y 11. Entrega visión útil antes de introducir segmentación o localización 3D.

### 13. Anotaciones físicas colocadas por el usuario

**Entrega:** permitir que el usuario marque una superficie o posición y coloque una etiqueta/panel asociado a una observación. Usar una fuente espacial explícita disponible en el proyecto o una colocación manual confirmada.

**Aceptación:** la anotación mantiene su posición durante la sesión al mover la cabeza; puede seleccionarse, editarse y eliminarse; indica cómo se obtuvo su posición. Una detección 2D sin profundidad no se transforma en una posición física inventada.

**Dependencias:** 05, 10 y 12. Esta entrega no promete reconocimiento persistente del objeto físico ni relocalización tras reiniciar.

### 14. Segmentación puntual con SAM 3.1

**Entrega:** enviar una captura y una descripción o selección compatible con la API para obtener regiones y máscaras. Vincular los resultados a `captureId`, dimensiones y transformación de imagen utilizados.

**Aceptación:** máscara alineada con la captura previsualizada; varias coincidencias pueden distinguirse y seleccionarse; ausencia de coincidencias, errores y tiempos de espera no generan anotaciones falsas. Limitar payloads y liberar texturas/máscaras.

**Dependencias:** 11 y 12. No incluye vídeo, tracking continuo ni reconstrucción 3D.

### 15. Anotaciones asistidas sobre objetos segmentados

**Entrega:** proponer una posición para una anotación usando la región seleccionada, la pose/calibración de captura y una fuente válida de geometría o profundidad. Pedir colocación manual cuando falte información espacial.

**Aceptación:** la correspondencia entre imagen y mundo se verifica en varios puntos del entorno; se descartan resultados obsoletos; la tolerancia de colocación se acuerda con mediciones del prototipo. No confundir estabilidad de un anclaje con seguimiento de un objeto que se mueve.

**Dependencias:** 13 y 14. El seguimiento de objetos móviles queda fuera de esta versión.

### 16. Continuidad del contenido anclado entre sesiones

**Entrega:** guardar y recuperar anotaciones con identidad de contenido y referencias a anclajes espaciales compatibles. Mostrar estado de localización y permitir recolocar o retirar lo que no pueda recuperarse.

**Aceptación:** tras reiniciar en el mismo entorno, las anotaciones se recuperan cuando el sistema logra localizar sus anclajes; los fallos no colocan paneles en coordenadas arbitrarias. Versionado compatible con los slots existentes.

**Dependencias:** 13 y 15. Persistencia local inicialmente; sin compartir anclajes entre usuarios o dispositivos.

**Cierre P1:** capturar el entorno, preguntar sobre un objeto, seleccionar una región segmentada, colocar una explicación espacial y continuar la conversación seleccionando esa anotación. Recuperar el contenido anclado tras reiniciar cuando se localice el entorno.

## P2 — Experiencia multimodal y continuidad

### 17. Entrada por voz con transcripción

**Estado:** Completed · Revisada (29 de octubre de 2026). Dictado Muse mediante pasarela implementado en Unity, con captura PCM, certificado fijado, parciales y final como borrador. Relé validado por el usuario; compilación C#, 3 grupos Muse y 18 tests Node aprobados. La regresión de SpatialChatChecks se corrigió en 06f (12 grupos aprobados), incluido el fin de dictado vacío sin sondeo. Funcionamiento en Quest confirmado por el usuario el 31 de octubre de 2026; latencia sin medir. [Implementación y validación](Progress/Completed/17-muse-voice-transcribe.md).

**Entrega:** pulsar para hablar, detener/cancelar y convertir audio a texto mediante Muse Voice Transcribe si el acceso está disponible. Mostrar el texto editable antes de enviarlo.

**Aceptación:** estados claros de micrófono y subida; permiso denegado y errores recuperables; cancelar no envía un turno. La transcripción utiliza el mismo envío de mensajes que el teclado.

**Dependencias:** 06–08. Se puede adelantar después de P0 si introducir texto en el visor resulta el principal obstáculo de uso.

### 18. Respuestas habladas

**Estado:** Completed · Cerrada (30 de octubre de 2026). Probada por el usuario en Quest 3 junto con 18b; latencia sin medir. wit$Rebecca, lectura final y controles Listen/Stop/Mute implementados; bridge 29/29, filtro/estados simulados, ChatExecutionChecks (15) y SpatialChatChecks (12) aprobados. Los controles Listen/Stop/Mute se sustituyen en 18b. [Implementación](Progress/Completed/18-spoken-replies.md).

**Entrega:** TTS con Meta Voice SDK, con reproducir, detener y silenciar. Leer la respuesta final o un resumen indicado, sin narrar logs ni payloads de tools.

**Aceptación:** puede interrumpirse; no se acumulan audios de turnos antiguos; un fallo de voz no impide recibir el texto. Al diseñar, comprobar voces en español, latencia, límites y gestión del token de Wit.ai.

**Dependencias:** 02 y 17. No exige voz conversacional continua.

### 18b. Botón único de voz

**Estado:** Completed · Cerrada (30 de octubre de 2026). Botón único en chat y menú, iconos sincronizados y voz independiente del cierre del chat. Compilación, estados simulados y HandMenuChecks aprobados; probada por el usuario en Quest 3. SpatialChatChecks sin repetir tras un timeout del Editor sin avanzar frames. [Implementación](Progress/Completed/18b-voice-toggle-button.md).

**Entrega:** un solo botón de icono (`volume_up`/`volume_off`), en chat y menú de mano, que activa o silencia la voz. Sustituye a Listen/Stop y Mute/Unmute; se deja de poder volver a escuchar una respuesta. La voz sigue sonando con el chat cerrado.

**Aceptación:** ambos botones muestran siempre el mismo estado; cerrar el chat no corta la lectura; silenciar sí la corta; activar no lee nada hasta la siguiente respuesta; el menú de cuatro botones sigue entre 0,13 y 0,17 m de ancho.

**Dependencias:** 06e y 18.

### 19. Imágenes generadas con Muse Image

**Estado:** Completed · Pendiente de revisión (1 de noviembre de 2026). Pasos 1–7 implementados; C# compilado y bridge 37/37 tests aprobados. LayoutChecks aprobado con 44 comprobaciones, ejecutado por Claude mediante Unity CLI en Play Mode. PresentationChecks aprobado con 251 aserciones, tras corregir una aserción obsoleta de versión 6 a 7. Paso 8 (system prompt) aplicado. Primera prueba real correcta del usuario con la carbonara (1 de noviembre de 2026). Solo falta probar: casos «No», otros casos «Sí», calidad, latencia, reutilización, guardado y fallos, y Quest 3. [Implementación y validación](Progress/Completed/19-muse-image-cards.md).

**Entrega:** herramienta para que el agente genere imágenes con Muse Image y las muestre en tarjetas (`mediaCard`, sola o en `layout_create`) o en paneles, con estado de carga. La llave y la llamada viven en el backend; el bridge entrega los bytes a Unity por la pasarela autenticada, ya que 26d retiró el resolver anterior. El agente decide si genera, con una guía en el prompt:

- **Genera** cuando la imagen ayuda a reconocer un objeto físico genérico: ingredientes, platos, herramientas, piezas técnicas, animales y plantas.
- **No genera** cuando basta con texto: preguntas simples, datos, listas, respuestas en el chat, o cuando el usuario pide no usar imágenes.
- **No genera** imágenes con derechos o que puedan pasar por reales: portadas de libros, pósteres de películas, personas reales, marcas o ubicaciones. Esas tarjetas siguen solo con texto.

**Aceptación:** «dame los ingredientes de una carbonara» crea tarjetas con una imagen generada de cada ingrediente; «¿cuánta pasta por persona?» responde en el chat sin generar nada; «muéstrame los libros de Isabel Allende» crea tarjetas sin portadas inventadas. Las tarjetas aparecen sin esperar a las imágenes o con un estado de carga visible; un fallo o una cuota agotada deja la tarjeta con su texto y lo indica, sin reintentos ni mutaciones duplicadas. Número de imágenes por turno, tamaño y memoria acotados; la imagen se guarda y se recupera con el slot; deshacer y borrar liberan las texturas. Sin modelos 3D ni edición de texturas de objetos físicos.

**Dependencias:** 05, 08, 23 y 26 (reutiliza `mediaCard` y `RemoteMediaStore`). Sin cambio de formato de slot: se guardan referencias y los archivos quedan en disco.

### 20. Memoria contextual compacta de sesión

**Entrega:** conservar objetivos activos, objetos seleccionados, referencias a capturas/anotaciones y un resumen de resultados relevantes. Recuperar detalles de escena mediante consultas cuando hagan falta.

**Aceptación:** conversaciones largas mantienen referencias útiles sin reenviar toda la jerarquía; el runtime sigue siendo la autoridad sobre el estado real; eliminar/cargar/reiniciar invalida referencias obsoletas. La memoria no contiene claves ni conserva imágenes indefinidamente.

**Dependencias:** 02, 10 y 12. No sustituye la persistencia de contenido. Antes de esta entrega se mantiene solo el contexto mínimo necesario para cada turno.

### 21. Reabrir espacios de trabajo

**Entrega:** agrupar un slot de contenido, una referencia de conversación y su resumen, con fecha y nombre; permitir reabrirlos desde el menú. Manejar de forma explícita conversaciones o recursos que ya no estén disponibles.

**Aceptación:** el usuario reabre un trabajo, inspecciona su contenido y continúa sin confundir IDs de runtime antiguos con los actuales. No se promete restaurar procesos o turnos interrumpidos como si siguieran ejecutándose.

**Dependencias:** 09, 16 y 20.

## Reglas comunes de implementación

- Un único circuito de schemas, validación y ejecución para herramientas; reutilizar servicios de dominio desde la UI, sin duplicar lógica de escena.
- Todas las creaciones tienen contenedor. El padre nunca es `null`; se utiliza `Generated`, un contenedor de petición o un padre válido confirmado.
- Una feature que modifica contenido generado debe declarar su comportamiento ante deshacer, eliminación y guardado/carga antes de cerrarse.
- Confirmar el resultado del runtime antes de anunciar éxito. Los timeouts y desconexiones no autorizan a repetir mutaciones.
- Mantener `seyra_*: allow`; el panel atiende permisos que solicite OpenCode, sin introducir aprobaciones nuevas en bridge o runtime. No atribuir bloqueos a un plan o proveedor sin comprobarlos.
- Los perfiles Seyra usan lista blanca con `* deny` como primera regla. Ninguna herramienta heredada de la configuración global llega al asistente; añadir una herramienta no `seyra_*` es una decisión explícita que actualiza `opencode.json`, su test y el README del backend.
- Mantener la equivalencia de capacidades y permisos entre perfiles existentes. La incorporación de Muse Spark requiere decidir explícitamente la configuración de proveedor/perfil.
- UI con layouts, referencias serializadas y recursos compartidos. Reutilizar la biblioteca visual conservando sus referencias serializadas; mantener un único circuito de herramientas, historial y persistencia.
- Pruebas técnicas en cada entrega y validación en dispositivo para funciones XR. Medir latencia, volumen de datos y recursos sobre casos de uso concretos; los objetivos cuantitativos se fijan con la primera medición, no con cifras inventadas.
- La captura y la voz son explícitas. Claves de proveedores en backend; nunca en assets ni builds del visor. Definir retención y eliminación de archivos al introducir recursos persistentes.

## Fuera del alcance inicial

Colaboración multiusuario, sincronización cloud, agentes paralelos, ejecución arbitraria de C# o shell desde el asistente, edición genérica de componentes, importación de modelos 3D, tracking continuo de objetos físicos, segmentación de vídeo y reconstrucción 3D automática. Solo se incorporarán cuando un caso de uso lo justifique y el recorrido anterior esté validado.

## Fuentes y decisiones pendientes

Documentación consultada el 27 de octubre de 2026. La disponibilidad documental no confirma acceso con la cuenta del proyecto ni compatibilidad con su versión de OpenCode.

- [Meta Model API: modelos](https://dev.meta.ai/docs/models): referencia de Muse Spark, Muse Image y Muse Voice Transcribe. Confirmar modelo, acceso, coste y formatos durante 08, 17 y 19.
- [Meta Model API: introducción y SAM 3.1](https://dev.meta.ai/docs/overview): referencia para la segmentación. Validar el contrato real en 14.
- [Passthrough Camera API: visión general](https://developers.meta.com/vr/documentation/unity/unity-pca-overview/): distingue captura RGB del passthrough visual y de Media Projection; identifica Quest 3/3S como dispositivos compatibles.
- [Passthrough Camera API: uso en Unity](https://developers.meta.com/vr/documentation/unity/unity-pca-documentation/): referencia de captura, calibración, timestamps y pose. En 11 comprobar el SDK instalado, sin copiar APIs de una versión distinta.
- La consulta por `metavr docs search` no pudo completarse por acceso local/red; las referencias anteriores se comprobaron mediante la documentación web oficial. No se ha validado hardware en esta tarea de planificación.

No se fijan fechas de entrega sin medir la integración real. Cada sección es una unidad funcional de planificación; se divide en tareas técnicas pequeñas al empezar, conservando sus criterios de aceptación.
