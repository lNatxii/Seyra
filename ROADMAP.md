# Seyra — Roadmap de desarrollo

Propuesta de referencia · 27 de octubre de 2026.

## Objetivo y criterio de prioridad

Seyra debe permitir conversar en realidad mixta, crear y modificar contenido espacial con herramientas verificables y utilizar imágenes del entorno para ofrecer explicaciones y anotaciones útiles.

El orden numérico es el orden recomendado de desarrollo. P0 entrega un asistente espacial funcional; P1 incorpora comprensión del entorno físico; P2 mejora la experiencia multimodal. Las dependencias indicadas son condiciones de entrada. No se considera terminada una feature solo porque compile: debe cumplir sus criterios de aceptación en el flujo real.

Se desarrolla en `Seyra/`.

Este documento fija prioridad, orden, dependencias y criterios de aceptación. El diseño detallado de cada feature, su estado de implementación, limitaciones y validación están en [Progress/](Progress/README.md): un archivo por feature que se mueve entre `Future/`, `InProgress/` y `Completed/`. La línea **Estado** de cada sección enlaza a ese archivo.

## Punto de partida

Estas capacidades ya están implementadas y deben reutilizarse. Su existencia no implica que todo el recorrido esté validado por el usuario ni en un visor:

| Área | Estado actual | Trabajo pendiente |
| --- | --- | --- |
| XR | Escena con passthrough y rig de interacción completo. | Validación de la experiencia y conectividad en dispositivo. |
| Chat | Cliente básico de OpenCode, conexión, mensajes y panel para aceptar/rechazar permisos. | Validación con el servidor real, presentación de actividad y recuperación de sesión. |
| Tools | MCP, schemas, validación, bridge y confirmación desde Unity. | Ampliar capacidades usando el mismo circuito. |
| Escena | Jerarquía paginada, búsquedas, IDs, componentes y Transform. | Contexto compacto y selección desde la UI. |
| Contenido | Contenedores, primitivas, renombrado, reparenting, eliminación recuperable y deshacer. | Colores, colocación relativa, paneles espaciales e interacción manual. |
| Persistencia | Slots explícitos con IDs de objetos, jerarquía, primitivas y Transform. | Nuevos tipos de contenido y controles accesibles al usuario. |
| Modelos | Perfiles GLM y DeepSeek mediante OpenCode. | Integración y validación de Muse Spark. |

El historial y la papelera duran el runtime. Guardar/cargar contenido no equivale a persistir una conversación ni a recuperar posiciones físicas entre sesiones. Los componentes tienen IDs de runtime; los IDs de objetos generados se conservan mediante guardado/carga explícitos.

## P0 — Asistente espacial funcional

### 01. Validación del recorrido actual y resolución de bloqueos

**Estado:** completado en el Editor el 27 de octubre de 2026 con OpenCode real, GLM y DeepSeek. [Registro, pasos y resultados](Progress/Completed/01-validation.md).

**Resultado:** ambas conversaciones completaron consulta, creación, renombrado, movimiento, cambio de padre, eliminación, recuperación, deshacer y guardado/carga, con 50 llamadas confirmadas por el runtime. Corregida la selección de modelo del perfil al crear la sesión. Panel comprobado con permisos reales `read`: permitir una vez ejecuta la lectura y rechazar interrumpe el paso con error visible, sin reenvío. `seyra_*: allow` se conserva; no se ampliaron permisos de shell ni archivos. El incidente histórico original no pudo reproducirse ni atribuirse al plan gratuito. La validación en visor sigue correspondiendo a 07.

**Entrega:** comprobar con el OpenCode real que una conversación puede consultar, crear, renombrar, mover, eliminar, recuperar, deshacer y guardar/cargar contenido. Investigar la solicitud concreta que está entrando en `ask`; no atribuir el fallo al plan gratuito sin evidencia.

**Aceptación:** recorrido reproducible desde una sesión nueva; decisiones del panel confirmadas por el servidor cuando corresponda; errores visibles y sin reenvíos de mutaciones ambiguas; registro de los pasos y resultados. La regla `seyra_*: allow` permanece y no se amplían permisos de shell ni archivos.

**Dependencias:** ninguna. Es el siguiente trabajo recomendado.

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

**Entrega:** componente de lazy follow propio para el menú (el SDK 207.0.1 no trae uno): zona cómoda bajo la mirada, movimiento amortiguado, respeto del agarre y de la posición elegida al soltar, y "Reubicar" que coloca chat y menú.

**Aceptación:** el menú vuelve a su sitio tras girar o desplazarse, no se mueve con giros breves ni mientras se agarra; el chat no se mueve solo.

**Dependencias:** 06. Base del seguimiento de 22d.

### 06d. Ocultar el menú espacial cuando el chat está a la vista

**Estado:** Completed · Revisada (28 de octubre de 2026). Validado en el Editor el 27 de octubre de 2026: 4 comprobaciones de visibilidad, 12 grupos de seguimiento y 11 de chat aprobados. Pendientes la prueba manual del usuario con Meta XR Simulator y la validación física en visor (07). [Implementación y evidencias](Progress/Completed/06d-menu-hide-when-chat-visible.md).

**Entrega:** el menú con seguimiento perezoso se oculta mientras el chat abierto está en la zona central de la vista y reaparece en su pose de reposo al apartar la mirada o cerrar el chat. Histéresis y retardos para evitar parpadeo; el seguimiento de 06c sigue activo mientras está oculto. El menú en la mano queda descartado en esta iteración.

**Aceptación:** con el chat delante, el menú no se ve ni bloquea rayos hacia el chat; al girar o mirar al suelo reaparece en menos de 0,5 s; al cerrar el chat aparece de inmediato; mirar el borde del chat no lo hace parpadear.

**Dependencias:** 06c. Se implementa en secuencia con 22c/22d (mismos prefabs y escena).

### 06e. Menú de mano

**Estado:** Completed · Revisada (28 de octubre de 2026). Menú de mano en la izquierda (palma o botón de menú del mando, acción QuestButtons/Menu): una fila de tres botones de icono (Chat, Voz, Traer chat) de unos 0,15 × 0,08 m. El escaneo de habitación sale del menú: la habitación debe estar escaneada antes de abrir Seyra. HandMenuChecks (5) y SpatialFollowChecks (12) aprobados en Editor. Validación física en 07. [Implementación y revisión](Progress/Completed/06e-hand-menu.md).

**Entrega:** el menú del asistente pasa a la mano izquierda: aparece al mostrar la palma o con el botón de menú del mando izquierdo (acción de Input System asignable en el inspector). Fila compacta de tres botones de icono (Chat, Voz, Traer chat) con línea de estado; cada botón da una respuesta visible. Sustituye el dock con seguimiento de 06c/06d para el menú.

**Aceptación:** el menú aparece junto a la mano en menos de 0,3 s y desaparece sin parpadeo; mide entre 0,13 y 0,17 m de ancho; "Traer chat" coloca el chat delante o explica por qué no puede; el menú no reserva espacio ni bloquea la colocación.

**Dependencias:** 06, 06b, 22d/22e.

### 07. Conexión y ejecución verificadas en Quest

**Entrega:** configurar desde la aplicación la dirección del backend, autenticar la conexión sin incluir secretos de proveedores en assets o builds y verificar el enlace Unity–bridge fuera del Editor. El backend sigue ejecutándose en un equipo accesible por red.

**Aceptación:** build instalado en el dispositivo objetivo que conecta con el backend y completa conversación, herramientas y recuperación tras pausa/reanudación; estado independiente de conexión al chat y al runtime; errores de red comprensibles. La conexión no depende de `127.0.0.1` del visor ni de leer `.env.local` desde Android.

**Dependencias:** 06. La solución de transporte y credenciales debe cerrarse antes de considerar entregado el MVP.

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

## P1 — Comprensión e interacción con el entorno

### 22. Habitación, muebles y colocación espacial

**Estado:** 22e Completed · Revisada (28 de octubre de 2026): integridad tras agarre corregida, chat y menú con seguimiento independiente y menú visible más allá de 2,1 m. Validado en Editor: WindowSnappingChecks 23, SpatialPlacementChecks 50, SpatialFollowChecks 14, LazyFollowChecks 12 grupos y MenuVisibilityChecks 5 grupos; prueba manual en simulador/visor pendiente. 22a implementada, validada en Editor y revisada el 28 de octubre de 2026 (correcciones en feat/22a-review-fixes: contrato de room_objects, RoomContext como único cargador de la habitación, también la del simulador en Editor, y omisión de anclas defectuosas; RoomChecks 689): contexto e inventario MRUK, referencias físicas de solo lectura, bounds y colocación relativa en marcos world/viewer/reference con orientación y deshacer. 570 comprobaciones de habitación, 5.165 de regresión de bounds y recorrido MCP real aprobados. 22b implementada en Editor (planificador, reservas, tools e historial; conversación manual pendiente). 22d implementada en Editor: seguimiento del grupo chat/menú, fijado/reanudar, bloqueos, recolocación planificada y escaneo explícito; 11 comprobaciones básicas aprobadas, regresión completa del chat aprobada (11 grupos). 22c implementada en Editor (acople al soltar ventanas, previsualización y revalidación por revisión de habitación) · Completed · Revisada (28 de octubre de 2026): 22 comprobaciones de acople y regresiones de colocación (50), layouts (16) y seguimiento (12) aprobadas; prueba manual con Meta XR Simulator pendiente del usuario. Validación física pendiente de 07. Correcciones de revisión 22b/22d (puntos 1–5) aplicadas y revisadas el 28 de octubre de 2026 (commit ffa8862 en feat/22-room-placement, sin merge a main). 22a revisada el 28 de octubre de 2026 con cambios solicitados; aplicados el mismo día en feat/22a-review-fixes (contrato de room_objects, RoomContext único dueño de la carga con la habitación del simulador en Editor, anclas singulares o cizalladas omitidas sin invalidar la habitación) · Completed · Pendiente de revisión: 689 comprobaciones de habitación y 50 de colocación aprobadas. Compilación y suites solicitadas aprobadas: 15 de planificación, 50 de colocación, 12 de seguimiento y 11 grupos de chat. [Diseño](Progress/Completed/22-room-spatial-placement.md).

**Orden:** después de 07 y antes de 10. La parte verificable en Editor con habitaciones de prueba puede avanzar en paralelo a 07; la validación física depende de 07.

**Entrega:** cargar la habitación capturada con MRUK y exponer al modelo paredes, aberturas y muebles con etiqueta, tamaño y posición respecto al usuario, como referencias de solo lectura. Ampliar `scene_bounds` y `place_relative` a referencias físicas y a direcciones relativas al usuario o a la referencia. Planificador que elige pared, mueble o aire con descartes y puntuación, aplicado mediante herramientas con historial. Acople de ventanas al soltarlas y seguimiento perezoso del chat. Guía en el prompt para usar el espacio solo cuando aporta valor.

**Aceptación:** sin permiso o sin mapa el chat sigue funcionando y la colocación lo declara; peticiones como «encima de la mesa», «a la derecha del sofá» o «en la pared» se resuelven en una conversación real y se pueden deshacer; los anclajes de la habitación no se pueden modificar; el chat no se mueve durante la interacción ni por giros breves. Los resultados se presentan como compatibles con la geometría capturada, no como espacio libre garantizado.

**Dependencias:** 03–06; validación en visor tras 07. Sirve de fuente espacial para 13, 15 y 16.

### 23. Layouts espaciales para colecciones

**Estado:** 23b Completed · Revisada (28 de octubre de 2026: cambio 1 de 11.4 aplicado, perWall informa capacidad de paredes no usadas; LayoutPlannerChecks ejecutada una vez, 1304 aprobadas): wall/surface, reservas y persistencia 5 implementados; validación básica aprobada (1303 de layouts puros, 44 de integración, 15 de planificación, 50 de colocación, 689 de habitación y 9 pruebas npm). 23a Completed · Revisada (28 de octubre de 2026). 23c, parte flotante, aplicada por Claude en el prompt (feat/23c-layout-prompt, commit de85636); ejemplos de pared y mesa añadidos tras 23b (feat/23c-room-layout-prompt, commit cca737e); pendiente la evaluación del catálogo con GLM y DeepSeek. Corregidos los tres cambios de revisión: MOTION_DRIVEN, rechazo de layout_apply a anchor desde otra disposición y arranque del fixture de chat. Compilación y LayoutChecks aprobados (16 comprobaciones); ChatExecutionChecks con el fixture corregido: 15 grupos aprobados en una única ejecución. Layouts flotantes y persistencia 5 implementados; 23c y validación física pendientes. [Diseño](Progress/Completed/23-spatial-layouts.md).

**Orden:** 23a después de 22b; 23b después de 23a; 23c (prompt) al cerrar 23a, con la parte de pared tras 23b.

**Entrega:** herramientas `layout_plan`, `layout_create` y `layout_apply` que crean o reorganizan N tarjetas del mismo tamaño en una llamada, con las disposiciones arc, plane, hub, surround, anchor, wall y surface, y los comportamientos fijo, alineado, billboard y lazy follow. El sistema calcula las poses, también el reparto entre paredes. El prompt sustituye la receta manual de colecciones por una guía de decisión con ejemplos.

**Aceptación:** una petición como «muéstrame los libros de Isabel Allende» produce tarjetas distribuidas en una sola llamada; las preguntas simples siguen en el chat; sin solapes; si no cabe no se crea nada y se informa de la capacidad; deshacer, guardado y carga conservan el layout.

**Dependencias:** 05, 06c, 22a y 22b.

### 24. Movimiento paramétrico de contenido generado

**Estado:** In progress · diseño listo. [Diseño](Progress/InProgress/24-parametric-motion.md).

**Orden:** después de 23a (comparten el mecanismo de Transforms controlados, el historial y la persistencia) y antes de 10a.

**Entrega:** `motion_set`, `motion_remove` y `motion_control` para girar sobre sí mismo (spin), orbitar alrededor de un objeto o punto (orbit) y oscilar (bob), con pausa, reinicio, deshacer y persistencia. Pensado para maquetas vivas como un sistema solar.

**Aceptación:** planetas que orbitan y giran configurados en pocas llamadas; una luna orbita un planeta en órbita; deshacer sin conflictos de historial; guardado y carga conservan las animaciones; las mutaciones sobre un canal animado se rechazan con un mensaje claro.

**Dependencias:** 05 y 23a. Lo usa 10a.

### 10. Selección e interacción bidireccional con contenido virtual

**Estado:** 10a (objetos interactivos con panel de detalle: selección por rayo, agarre opcional con vuelta al soltar, ficha que sigue al objeto) In progress · diseño listo, después de 24. 10b (selección como contexto del mensaje y manipulación integrada en el historial) sin diseñar. [Diseño 10a](Progress/InProgress/10a-interactive-objects.md).

**Entrega:** seleccionar objetos o paneles y enviar su `entityId` como contexto explícito del siguiente mensaje. Añadir indicación visual de selección y manipulación manual básica mediante el rig existente.

**Aceptación:** «mueve esto» afecta al objeto seleccionado; seleccionar no cambia su identidad; moverlo manualmente se integra con el historial o queda marcado como cambio externo, sin sobrescribirlo al deshacer. Una selección eliminada o de otro runtime se invalida.

**Dependencias:** cierre P0. Se adelanta respecto a la segmentación porque ya aporta valor con objetos virtuales.

### 11. Captura puntual de cámara del entorno

**Entrega:** obtener una imagen RGB con la Passthrough Camera API, con permiso, previsualización y envío explícito. Asociar `captureId`, cámara de origen, resolución, timestamp y metadatos de pose/calibración disponibles en el instante de captura.

**Aceptación:** captura y orientación correctas en el visor; permiso denegado o cámara no disponible no bloquean el chat; pausar/reanudar libera y recupera recursos. No iniciar streaming continuo ni presentar una captura de la escena virtual como foto real.

**Dependencias:** 07. Verificar compatibilidad del SDK instalado y dispositivo antes de implementar; la API de cámara es distinta del passthrough visual y de la captura del framebuffer.

### 12. Preguntas visuales con Muse Spark

**Entrega:** adjuntar una captura identificada al turno y permitir preguntas sobre ella, conservando la relación imagen–respuesta y controlando resolución y tamaño del envío.

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

**Entrega:** pulsar para hablar, detener/cancelar y convertir audio a texto mediante Muse Voice Transcribe si el acceso está disponible. Mostrar el texto editable antes de enviarlo.

**Aceptación:** estados claros de micrófono y subida; permiso denegado y errores recuperables; cancelar no envía un turno. La transcripción utiliza el mismo envío de mensajes que el teclado.

**Dependencias:** 06–08. Se puede adelantar después de P0 si introducir texto en el visor resulta el principal obstáculo de uso.

### 18. Respuestas habladas

**Entrega:** TTS mediante un servicio sencillo, con reproducir, detener y silenciar. Leer la respuesta final o un resumen indicado, sin narrar logs ni payloads de tools.

**Aceptación:** puede interrumpirse; no se acumulan audios de turnos antiguos; un fallo de voz no impide recibir el texto. Seleccionar proveedor según acceso, latencia y coste comprobados al implementar.

**Dependencias:** 02 y 17. No exige voz conversacional continua.

### 19. Imágenes generadas en paneles espaciales

**Entrega:** generar una ilustración o diagrama con Muse Image y mostrarlo como contenido espacial con referencia al recurso, petición de origen y estado de carga.

**Aceptación:** tamaño y memoria acotados; recurso guardado y recuperable con el panel; errores o enlaces caducados no producen contenido vacío sin explicación. Sin generar modelos 3D ni editar texturas de objetos físicos en esta entrega.

**Dependencias:** 05, 08 y 09. Amplía la persistencia para gestionar archivos asociados.

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
