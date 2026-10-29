# Seyra

Asistente espacial de realidad mixta para Meta Quest 3, con Unity, Meta XR SDK, OpenCode y un bridge MCP para consultar y modificar contenido de escena.

## Meta AI

El perfil por defecto `seyra` usa **Muse Spark** (`meta/muse-spark-1.3`) mediante **Meta Model API**, con el protocolo Responses. Integración validada en el Editor con conversación real, creación de cubos y etiquetas, colección de libros en arco y deshacer: 14 llamadas a herramientas confirmadas. La validación física en visor permanece pendiente.

La clave `META_API_KEY` se configura exclusivamente en `Backend/.env.local` en el PC; no se distribuye a Unity ni al visor. `MODEL_API_KEY` se admite como alias en el proceso de arranque. Consulta [arranque, perfiles y permisos](Backend/README.md).

Los perfiles alternativos son `seyra_glm`, `seyra_deepseek` y `seyra_zai`, con las mismas instrucciones y permisos.

Muse Voice Transcribe, visión con la cámara, SAM y Muse Image están previstos en el roadmap; no forman parte de esta integración.

## Puesta en marcha: conectar Quest o un móvil Android al backend del PC

Seyra tiene dos partes:

- **El backend**, que se ejecuta en un PC con Windows: OpenCode con Muse Spark, el bridge MCP de escena y una **pasarela HTTPS**.
- **La app**, en Meta Quest 3/3S, o en un móvil Android con la escena de prueba de conexión.

La app solo habla con la pasarela:

```
Quest / móvil ──HTTPS (certificado fijado + token del dispositivo)──▶ Pasarela :8443 ──▶ OpenCode 127.0.0.1:4096
                                                                                    └──▶ Bridge   127.0.0.1:4100
```

- OpenCode y el bridge nunca escuchan fuera del PC.
- El dispositivo no recibe ninguna clave de proveedor. Solo recibe:
  - la URL de la pasarela;
  - su propio token;
  - la huella SHA-256 del certificado.
- La app solo acepta un certificado cuya huella coincida con la recibida. Así, la conexión funciona con cualquier IP o nombre de host sin depender de una autoridad de certificación.

Todos los comandos son de **PowerShell en Windows** y se ejecutan desde la raíz del repositorio, salvo que se indique otra carpeta. Los scripts del backend (`start.ps1`, `seyra-device.ps1`) solo funcionan en Windows de momento.

### 1. Requisitos

| Necesitas | Detalle |
| --- | --- |
| PC con Windows 10/11 | El PC y el dispositivo en la misma red, o con ruta entre ellos (VPN, Tailscale, puerto redirigido). |
| Node.js 22 o superior y npm | `node --version` |
| OpenCode v2 (`opencode2`) en el `PATH` | `opencode2 --version` (probado con 2.1.1) |
| Clave de Meta Model API | Se crea en [dev.meta.ai](https://dev.meta.ai) → *API keys*. |
| Unity 6000.3.21f1 con el módulo **Android Build Support** | Solo para compilar el APK. |
| `adb` (Android platform-tools) | Ver más abajo. |
| Dispositivo | Meta Quest 3/3S, o un móvil Android **ARM64 con Android 12L (API 32) o superior**. |

**`adb`.** Si `adb version` no responde, tienes dos opciones:

- usar el que trae Unity:
  ```powershell
  $env:Path += ";C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools"
  adb version
  ```
- o descargar las [platform-tools de Android](https://developer.android.com/tools/releases/platform-tools) y añadir su carpeta al `PATH`.

### 2. Configurar el backend (una vez)

1. **Instala las dependencias del bridge:**
   ```powershell
   npm ci --prefix Backend/bridge
   ```
2. **Crea `Backend/.env.local`.** Está excluido de Git y nunca sale del PC:
   ```dotenv
   META_API_KEY=<tu clave de Meta Model API>
   OPENCODE_SERVER_PASSWORD=<una contraseña larga inventada por ti>
   # Opcionales:
   # HELMCODE_API_KEY=<clave de Helmcode, solo para el perfil seyra_glm>
   # SEYRA_CLIENT_TOKEN=<token cliente de Wit.ai, solo para el dictado en el Editor>
   ```
   - `OPENCODE_SERVER_PASSWORD` protege la API local de OpenCode. Es distinta de la clave de Meta.
   - El agente por defecto `seyra` usa Muse Spark (`meta/muse-spark-1.3`).
3. **Crea el certificado de la pasarela** (una sola vez):
   ```powershell
   ./Backend/seyra-device.ps1 init-tls
   ```
   - Imprime `Gateway certificate SHA-256: <huella>`.
   - Genera un certificado ECDSA P-256 autofirmado con 2 años de validez en `Backend/.seyra/tls/`, también excluido de Git.
   - No sobrescribe uno existente salvo con `-Force`. Si lo regeneras, tendrás que volver a emparejar todos los dispositivos (paso 6).

### 3. Preparar el dispositivo (una vez)

**Meta Quest 3/3S**

1. Activa el **modo desarrollador** desde la app Meta Horizon del móvil: *Dispositivos → tu visor → Configuración del visor → Modo desarrollador*. Tu cuenta debe pertenecer a una organización de desarrollador de Meta.
2. Conecta el visor al PC por USB-C y, **dentro del visor**, acepta *Permitir depuración USB*. Marca "Permitir siempre desde este ordenador".

**Móvil Android**

1. *Ajustes → Información del teléfono* → pulsa 7 veces *Número de compilación* para activar las opciones de desarrollador.
2. *Ajustes → Sistema → Opciones de desarrollador* → activa **Depuración USB**.
3. Conecta el móvil por USB y acepta la huella RSA del PC en la pantalla del móvil.

**Comprueba que `adb` ve el dispositivo:**

```powershell
adb devices
```

Debe aparecer una línea `<serial>    device`.

- Si pone `unauthorized`, acepta el aviso en el dispositivo.
- Si hay varios dispositivos conectados, añade `-s <serial>` a **todos** los comandos `adb` de esta guía.

### 4. Compilar el APK

Abre `SeyraUnity/` con Unity 6000.3.21f1. Elige uno de los dos menús:

| Menú | Resultado | Para |
| --- | --- | --- |
| `Seyra → Build → Android APK` | `Builds/Seyra.apk` (escena `SeyraAssistant`, con XR) | Meta Quest |
| `Seyra → Build → Android Phone Test` | `Builds/SeyraPhoneTest.apk` (escena `MobileConnectionTest`, sin XR, vertical) | Móvil, para probar la conexión |

Después de compilar el APK de móvil, cierra y vuelve a abrir Unity y comprueba con `git status` que no han cambiado `ProjectSettings/` ni `Assets/XR/`. Si han cambiado, restáuralos con `git checkout -- SeyraUnity/ProjectSettings SeyraUnity/Assets/XR` **con Unity cerrado**, para que el build de Quest conserve XR.

### 5. Instalar la app y obtener su carpeta de datos

1. **Instala el APK:**
   ```powershell
   adb install -r Builds/Seyra.apk            # Quest
   adb install -r Builds/SeyraPhoneTest.apk   # móvil
   ```
2. **Averigua el identificador del paquete**, que es el *Package Name* de Unity en *Player Settings → Android*:
   ```powershell
   adb shell pm list packages | Select-String -Pattern "seyra|unity"
   ```
   En los comandos siguientes, sustituye `<paquete>` por ese valor, sin el prefijo `package:`. Por ejemplo:
   ```powershell
   $pkg = "com.tuempresa.seyra"
   ```
3. **Abre la app una vez** para que Android cree su carpeta de datos (`persistentDataPath`), y ciérrala:
   ```powershell
   adb shell monkey -p $pkg 1
   adb shell am force-stop $pkg
   ```
   En Quest también puedes abrirla desde *Biblioteca → Orígenes desconocidos*.

La carpeta de datos es `/sdcard/Android/data/<paquete>/files/`. En el móvil, la escena de prueba la muestra en la línea `Config:`.

### 6. Emparejar el dispositivo

Cada dispositivo tiene **su propio token**: si pierdes un visor, solo revocas ese.

1. **Averigua la IP del PC** en la red que comparte con el dispositivo:
   ```powershell
   Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } | Select-Object InterfaceAlias, IPAddress
   ```
   Si tienes varias interfaces, usa la de la Wi-Fi o Ethernet donde está el dispositivo. También sirve un nombre DNS o la IP de Tailscale.
2. **Genera el archivo de emparejamiento.** El nombre solo admite letras, números, `-` y `_`:
   ```powershell
   ./Backend/seyra-device.ps1 pair -Name quest3 -Url https://192.168.1.131:8443
   ```
   - Crea `Backend/.seyra/pairing/quest3.env` con `SEYRA_GATEWAY_URL`, `SEYRA_DEVICE_TOKEN` y `SEYRA_GATEWAY_CERT_SHA256`.
   - En el PC solo se guarda el *hash* del token (`Backend/.seyra/devices.json`).
3. **Copia ese archivo al dispositivo con el nombre `.env.local`:**
   ```powershell
   adb shell mkdir -p /sdcard/Android/data/$pkg/files
   adb push Backend/.seyra/pairing/quest3.env /sdcard/Android/data/$pkg/files/.env.local
   adb shell ls -la /sdcard/Android/data/$pkg/files/
   ```
   Debe aparecer `.env.local`.

   **Nunca copies `Backend/.env.local` al dispositivo:** contiene la clave de Meta y la contraseña de OpenCode.
4. **Borra la copia del PC.** Ya no hace falta, y contiene el token en claro:
   ```powershell
   Remove-Item -LiteralPath Backend/.seyra/pairing/quest3.env
   ```

### 7. Arrancar el backend

```powershell
cd Backend
./start.ps1
```

- **Qué hace:**
  - compila el bridge;
  - arranca la pasarela (porque existe el certificado) y OpenCode;
  - muestra `Gateway: https://0.0.0.0:8443`, una línea `LAN candidate: https://<IP>:8443` por interfaz y `Certificate SHA-256: <huella>`.
- **Mientras trabajas:** deja la ventana abierta. `Ctrl+C` detiene OpenCode y la pasarela.
- **Si el modelo no responde:** comprueba que no haya otro OpenCode o bridge ya en marcha, porque el puerto 4100 debe estar libre:
  ```powershell
  Get-NetTCPConnection -LocalPort 4100,4096,8443 -State Listen -ErrorAction SilentlyContinue | Select-Object LocalPort, OwningProcess
  ```
- **Si el firewall de Windows bloquea el puerto 8443:**
  1. Consulta el tipo de red:
     ```powershell
     Get-NetConnectionProfile | Select-Object InterfaceAlias, NetworkCategory
     ```
     Si tu red de casa figura como `Public`, puedes marcarla como privada (requiere administrador):
     ```powershell
     Set-NetConnectionProfile -InterfaceAlias "Wi-Fi" -NetworkCategory Private
     ```
  2. En una consola **de administrador**, permite el 8443 solo en redes privadas y solo desde tu subred local:
     ```powershell
     New-NetFirewallRule -DisplayName SeyraGateway -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8443 -Profile Private -RemoteAddress LocalSubnet
     ```
     Para quitarla: `Remove-NetFirewallRule -DisplayName SeyraGateway`.

Para trabajar solo en el Editor de Unity sin pasarela: `./start.ps1 -Gateway:$false`. El Editor se conecta en directo a `127.0.0.1`.

### 8. Abrir la app y comprobar la conexión

1. **Abre la app:**
   ```powershell
   adb shell am force-stop $pkg
   adb shell monkey -p $pkg 1
   ```
2. **Comprueba el estado:**

   | Dispositivo | Qué comprobar |
   | --- | --- |
   | **Móvil** (`MobileConnectionTest`) | El panel *Diagnóstico de conexión* muestra `Pasarela (HTTPS) · <IP>:8443`, **Chat: conectado** y **Runtime conectado (HTTPS)**. Escribe `hola` y envíalo: Muse Spark debe responder. *Probar TLS* debe dar `aceptado` en las rutas HTTPS con la huella correcta (`matching`) y `rechazado` con la incorrecta (`wrong`). WSS sale rechazado siempre; es lo esperado. |
   | **Quest** (`SeyraAssistant`) | Mira la palma izquierda (o pulsa el botón de menú del mando izquierdo) para abrir el menú de mano: los indicadores de chat y runtime deben estar en verde. Abre el chat y pide, por ejemplo, "Crea un panel que diga hola". |

3. **Si algo falla, sigue el registro de Unity en el dispositivo:**
   ```powershell
   adb logcat -s Unity
   ```

### Día a día

- **Cambia la IP del PC:** edita solo `SEYRA_GATEWAY_URL`. Lo más sencillo es descargar el archivo, editarlo y volver a subirlo:
  ```powershell
  adb pull /sdcard/Android/data/$pkg/files/.env.local device.env
  # Edita device.env y cambia la IP
  adb push device.env /sdcard/Android/data/$pkg/files/.env.local
  Remove-Item device.env
  ```
- **Dispositivo perdido o nuevo token:**
  ```powershell
  ./Backend/seyra-device.ps1 list
  ./Backend/seyra-device.ps1 revoke -Name quest3
  ```
  Para volver a emparejar con el mismo nombre, revoca antes el anterior y repite el paso 6.
- **Sin cable (ADB por Wi-Fi):** con el dispositivo conectado por USB, ejecuta lo siguiente, desconecta el cable y usa `adb` igual que antes. En Quest se pierde al reiniciar el visor.
  ```powershell
  adb shell ip -f inet addr show wlan0     # IP del dispositivo
  adb tcpip 5555
  adb connect <IP-del-dispositivo>:5555
  ```
- **Reinstalar el APK** con `adb install -r` conserva `.env.local`. Desinstalar la app lo borra.

### Problemas frecuentes

| Síntoma | Causa probable | Solución |
| --- | --- | --- |
| "Sin emparejar" | No hay `.env.local` en la carpeta de la app, o tiene otro nombre | Repite el paso 6.3 y comprueba con `adb shell ls -la /sdcard/Android/data/$pkg/files/` |
| "No se encuentra el backend en IP:8443" | Backend apagado, IP incorrecta o firewall | Comprueba que `start.ps1` está en marcha, que la IP de `LAN candidate` coincide con la de `.env.local` y la regla del firewall |
| "Certificado del backend no reconocido" | Se regeneró el certificado (`init-tls -Force`) | Revoca y vuelve a emparejar (paso 6) |
| "Dispositivo no autorizado o revocado" | Token revocado o archivo de otro dispositivo | Revoca si hace falta y vuelve a emparejar |
| "Demasiados intentos" | Más de 20 fallos por minuto desde esa IP | Espera 5 minutos y corrige la configuración |
| Chat conectado pero el modelo no responde | Falta `META_API_KEY`, no hay créditos o el agente de la escena no es `seyra` | Revisa `Backend/.env.local`, el gasto en dev.meta.ai y la ventana de `start.ps1` |
| Runtime desconectado con el chat conectado | Otra app o el Editor ocupan el runtime (solo puede haber uno), o hay un bridge huérfano en el puerto 4100 | Cierra el Play Mode del Editor; reinicia `start.ps1` tras liberar el 4100 |
| `adb: device unauthorized` | No se aceptó la depuración USB | Acepta el aviso en el dispositivo; `adb kill-server` y vuelve a conectar |
| `adb push` falla con *Permission denied* | La carpeta de la app aún no existe | Abre la app una vez (paso 5.3) y repite |

Detalles técnicos de la pasarela y sus rutas: [Backend/bridge/GATEWAY.md](Backend/bridge/GATEWAY.md). Arranque, perfiles y permisos de OpenCode: [Backend/README.md](Backend/README.md).
