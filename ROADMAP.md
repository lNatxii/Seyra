# Seyra — Roadmap de desarrollo

Propuesta de referencia · 27 de octubre de 2026.

## Objetivo y criterio de prioridad

Seyra debe permitir conversar en realidad mixta, crear y modificar contenido espacial con herramientas verificables y utilizar imágenes del entorno para ofrecer explicaciones y anotaciones útiles.

El orden numérico es el orden recomendado de desarrollo. P0 entrega un asistente espacial funcional; P1 incorpora comprensión del entorno físico; P2 mejora la experiencia multimodal. Las dependencias indicadas son condiciones de entrada. No se considera terminada una feature solo porque compile: debe cumplir sus criterios de aceptación en el flujo real.

Se desarrolla en `Seyra/`, con una arquitectura y un sistema visual propios.

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

**Estado:** completado en el Editor el 27 de octubre de 2026 con OpenCode real, GLM y DeepSeek. [Registro, pasos y resultados](Backend/validation/01-validation.md).

**Resultado:** ambas conversaciones completaron consulta, creación, renombrado, movimiento, cambio de padre, eliminación, recuperación, deshacer y guardado/carga, con 50 llamadas confirmadas por el runtime. Corregida la selección de modelo del perfil al crear la sesión. Panel comprobado con permisos reales `read`: permitir una vez ejecuta la lectura y rechazar interrumpe el paso con error visible, sin reenvío. `seyra_*: allow` se conserva; no se ampliaron permisos de shell ni archivos. El incidente histórico original no pudo reproducirse ni atribuirse al plan gratuito. La validación en visor sigue correspondiendo a 07.

**Entrega:** comprobar con el OpenCode real que una conversación puede consultar, crear, renombrar, mover, eliminar, recuperar, deshacer y guardar/cargar contenido. Investigar la solicitud concreta que está entrando en `ask`; no atribuir el fallo al plan gratuito sin evidencia.

**Aceptación:** recorrido reproducible desde una sesión nueva; decisiones del panel confirmadas por el servidor cuando corresponda; errores visibles y sin reenvíos de mutaciones ambiguas; registro de los pasos y resultados. La regla `seyra_*: allow` permanece y no se amplían permisos de shell ni archivos.

**Dependencias:** ninguna. Es el siguiente trabajo recomendado.

### 02. Estado de ejecución, cancelación y continuidad del chat

**Estado:** completado en el Editor el 27 de octubre de 2026. [Implementación, límites y pruebas](Backend/validation/02-execution.md).

**Resultado:** estados de conexión y turno separados, actividad de herramientas con resultados breves y sin razonamiento interno, cancelación confirmada por OpenCode, cierre explícito de turnos interrumpidos y reconexión a la misma sesión sin reenviar mensajes. Verificados 15 controles de fallos/carreras, decisiones de permisos sin duplicados y un recorrido real de creación–cancelación–continuación con DeepSeek. Corregido un reenvío implícito de POST en el transporte anterior. La continuidad dura mientras sobrevive la instancia del chat; no persiste conversaciones tras reiniciar la aplicación (21). La validación en visor permanece en 07.

**Entrega:** mostrar estados de envío, ejecución, espera de permiso, finalización y fallo; presentar herramientas ejecutadas y resultados breves, sin razonamiento interno. Añadir cancelación del turno y reconexión a la sesión existente cuando sea posible.

**Aceptación:** cancelar impide continuar con nuevas acciones del turno, sin afirmar que revierte las ya realizadas. Una desconexión no duplica mensajes ni herramientas; los resultados desconocidos se inspeccionan antes de repetir. Una petición de trabajo interrumpida queda identificada y puede cerrarse explícitamente.

**Dependencias:** 01. No implica implementar otro planificador: se aprovecha el bucle de herramientas de OpenCode.

### 03. Colores y apariencia básica de objetos

**Entrega:** consultar y cambiar un color RGBA uniforme de primitivas gestionadas. Establecer un contrato pequeño para apariencia compatible con URP, historial y persistencia, sin edición arbitraria de materiales.

**Aceptación:** cambiar un objeto no altera otros que comparten material; deshacer restaura su aspecto; guardar/cargar reproduce el color; recursos creados se liberan correctamente. La transparencia, si no se soporta en esta entrega, se rechaza expresamente.

**Dependencias:** 01. Se amplía la persistencia existente, que actualmente rechaza materiales personalizados.

### 04. Dimensiones y colocación relativa fiables

**Entrega:** consultar dimensiones y bounds globales; colocar contenido gestionado encima, debajo o junto a otro objeto con una separación explícita. Usar geometría real, no deducir el tamaño únicamente de `localScale`.

**Aceptación:** casos con rotaciones y padres escalados colocan los objetos según el contrato documentado; los casos no representables se rechazan; toda modificación reutiliza validación e historial. No incluye reconstrucción de muebles físicos a partir de una foto.

**Dependencias:** 01; compatible con 03.

### 05. Paneles de texto y etiquetas espaciales

**Entrega:** crear y editar texto en paneles y etiquetas dentro de `Generated`: contenido, dimensiones, tamaño tipográfico y pose. Consultarlos y manipularlos mediante las herramientas existentes siempre que sea posible.

**Aceptación:** texto legible, ajuste de líneas y layouts sin solapamientos; límites de longitud y tamaño; eliminación, deshacer y guardado/carga completos. No interpretar HTML o markup arbitrario enviado por el modelo.

**Dependencias:** 03 y 04. Extender el formato versionado de contenido con migración o rechazo claro de versiones incompatibles.

### 06. Menú y chat funcionales en SeyraAssistant

**Entrega:** llevar las funciones del chat de prueba a una UI espacial mínima: conversación, entrada, conexión, actividad, permisos, cancelar y abrir/cerrar panel. Tomar SeyraChat como referencia de comportamiento, creando una estructura propia con layouts.

**Aceptación:** se puede completar una conversación en el visor con los métodos de entrada elegidos, sin depender del Inspector ni del teclado del PC; los paneles no se superponen y pueden recolocarse. `OpencodeTestUI` se conserva como banco de pruebas.

**Dependencias:** 02 y 05. No incluye copiar la suite de estilos anterior ni un rediseño visual completo.

### 07. Conexión y ejecución verificadas en Quest

**Entrega:** configurar desde la aplicación la dirección del backend, autenticar la conexión sin incluir secretos de proveedores en assets o builds y verificar el enlace Unity–bridge fuera del Editor. El backend sigue ejecutándose en un equipo accesible por red.

**Aceptación:** build instalado en el dispositivo objetivo que conecta con el backend y completa conversación, herramientas y recuperación tras pausa/reanudación; estado independiente de conexión al chat y al runtime; errores de red comprensibles. La conexión no depende de `127.0.0.1` del visor ni de leer `.env.local` desde Android.

**Dependencias:** 06. La solución de transporte y credenciales debe cerrarse antes de considerar entregado el MVP.

### 08. Muse Spark como proveedor utilizable

**Entrega:** integrar Muse Spark mediante Meta Model API, preferentemente como proveedor de OpenCode si su versión y la API lo permiten. Conservar schemas, executors y bridge. Si falta soporte, resolver primero esa compatibilidad con una prueba pequeña antes de crear otra arquitectura de agentes.

**Aceptación:** acceso real a la API y modelo configurado explícitamente; conversación y llamadas estructuradas completan el mismo recorrido de 01; errores de autenticación, cuota y proveedor se muestran sin duplicar acciones; claves solo en backend. No crear variantes de perfil que difieran accidentalmente en permisos o capacidades.

**Dependencias:** 01 y 02; puede investigarse antes de terminar 07, pero no bloquea el resto del contenido espacial. Esta entrega valida texto y tools; la visión se valida en 12.

### 09. Contenido generado utilizable sin comandos técnicos

**Entrega:** lista de contenedores de peticiones y controles básicos para seleccionar contenido, deshacer la última operación/petición, recuperar eliminaciones y guardar/cargar slots.

**Aceptación:** el usuario puede identificar qué creó cada petición y recuperar un trabajo guardado tras reiniciar; la UI usa los mismos servicios de dominio que las herramientas. La carga explica que sustituye el contenido generado actual. No se pierde contenido no soportado silenciosamente.

**Dependencias:** 05–07. La persistencia incluye colores y paneles, pero todavía no anclajes físicos ni historial entre sesiones.

**Cierre P0:** en el visor, pedir una composición de primitivas con colores y explicación espacial; corregirla en varios turnos, renombrarla, borrar y deshacer; guardar y volver a cargar tras reiniciar. Validar el flujo con Muse Spark para cerrar el objetivo de integración original, conservando el proveedor existente como alternativa de desarrollo.

## P1 — Comprensión e interacción con el entorno

### 10. Selección e interacción bidireccional con contenido virtual

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
- Mantener la equivalencia de capacidades y permisos entre perfiles existentes. La incorporación de Muse Spark requiere decidir explícitamente la configuración de proveedor/perfil.
- UI con layouts, referencias serializadas y recursos compartidos cuando proceda.
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
