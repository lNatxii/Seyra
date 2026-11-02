# Seyra

Seyra is a mixed-reality assistant for Meta Quest 3 that sees your space and acts in it. You talk to it with your voice. It can answer questions about what you are looking at, mark real objects, build 3D models on your table, place information on your walls and reimagine your room.

The app runs on the headset. A backend on your PC runs the agent and holds the API keys, so the PC must stay on while you use Seyra.

## Meta SDKs

All of these come from the Meta XR All-in-One SDK (`com.meta.xr.sdk.all` 207.0.1).

| Package | Version | Use in Seyra |
| --- | --- | --- |
| Core SDK (`com.meta.xr.sdk.core`) | 207.0.1 | Camera rig and mixed-reality runtime |
| Interaction SDK (`com.meta.xr.sdk.interaction`, `.interaction.ovr`) | 207.0.1 | Hand and controller interaction, grabbing, panels |
| MR Utility Kit (`com.meta.xr.mrutilitykit`) | 207.0.1 | Captured room, walls and furniture for placement. Passthrough Camera API (`PassthroughCameraAccess`) for photos, and environment raycasting (`EnvironmentRaycastManager`) to find the 3D position of real objects. |
| Voice SDK (`com.meta.xr.sdk.voice`) | 85.0.1 | Spoken replies through Wit.ai |
| Haptics SDK (`com.meta.xr.sdk.haptics`) | 207.0.1 | Included, not used directly |
| Audio SDK (`com.meta.xr.sdk.audio`) | 85.0.0 | Included, not used directly |
| Platform SDK (`com.meta.xr.sdk.platform`) | 207.0.0 | Included, not used directly |

The backend uses `@meta-sam/parser` 0.1.0 to read SAM 3.1 masks.

## Meta models

All models are called through the [Meta Model API](https://dev.meta.ai).

| Model | ID | What it enables |
| --- | --- | --- |
| Muse Spark | `muse-spark-1.3` | Runs the agent: conversation, tool use and spatial planning. Answers questions about camera photos and names the objects in them. |
| SAM 3.1 | `sam-3.1` | Finds the objects named by Muse Spark in a photo and returns their boxes and masks. Seyra uses them to mark real objects in 3D. |
| Muse Image | `muse-image-1.0` | Generates images for cards, and edits photos of your room, for example "paint my living room blue". |
| Muse Voice Transcribe | `muse-voice-transcribe-1.0` | Turns your speech into a text draft in real time. |

## Setup

Install these once.

| Requirement | Purpose |
| --- | --- |
| [Node.js](https://nodejs.org/en/download) 22 or later, with npm | Runs the backend. |
| [OpenCode](https://www.npmjs.com/package/@opencode/cli) 2.1.1 | Runs the agent. Install with `npm install -g @opencode/cli@2.1.1`. |
| [Meta Model API key](https://dev.meta.ai) | Calls the Meta models. Your key pays for the calls. |
| [Unity Hub](https://unity.com/download) with [Unity 6000.3.21f1](https://unity.com/releases/editor/archive) and Android Build Support | Builds the APK. |
| [Android platform-tools](https://developer.android.com/tools/releases/platform-tools) | Provides `adb`. Unity's Android SDK also includes it. |
| [Quest developer mode](https://developers.meta.com/horizon/documentation/native/android/mobile-device-setup/) | Lets you install the APK. Accept USB debugging in the headset. |
| Room scan on the headset | Lets Seyra place content in your room. Do it in the Quest settings. |
| [Wit.ai](https://wit.ai) client token (optional) | Enables spoken replies. Use an English app. |

The PC and the headset must be on the same network.

## Backend commands

Run all commands from the repository root. Replace these values:

- `<PC_IP>`: your PC's address on the network that it shares with the headset.
- `<PACKAGE>`: the Android package ID from Unity's Player Settings. In the project as it stands, it is `com.Ignashe.Seyra`.

**1. Install the dependencies:**

```sh
npm ci --prefix Backend/bridge
```

**2. Create `Backend/.env.local`.** Never copy this file to the headset:

```dotenv
META_API_KEY=<your Meta Model API key>
OPENCODE_SERVER_PASSWORD=<a long password you make up>
# Optional, for spoken replies:
# WITAI_CLIENT_TOKEN=<your Wit.ai client token>
```

**3. Create the gateway certificate:**

```sh
node Backend/seyra.mjs init-tls
```

**4. Build the APK.** Open `SeyraUnity/` in Unity and run **Seyra → Build → Android APK**.

**5. Connect the headset by USB and install the APK:**

```sh
adb install -r Builds/Seyra.apk
```

Open the app once in the headset, then close it. This creates its data folder.

**6. Pair the headset:**

```sh
node Backend/seyra.mjs pair --name quest3 --url https://<PC_IP>:8443
```

```sh
node Backend/seyra.mjs push --name quest3 --package <PACKAGE>
```

**7. Start the backend.** Keep the window open. `Ctrl+C` stops it:

```sh
node Backend/seyra.mjs start
```

**8. Open Seyra in the headset.** Accept the permission requests. Show your left palm, or press the menu button on the left controller, to open the hand menu. The chat and runtime indicators must be green.

**Other commands:**

```sh
node Backend/seyra.mjs list
```

```sh
node Backend/seyra.mjs revoke --name quest3
```

## Common problems

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| App says it is not paired | The pairing file is missing | Repeat the `pair` and `push` commands. |
| App cannot find the backend | Backend stopped, wrong IP address or firewall | Compare `<PC_IP>` with the `LAN candidate` lines that `start` prints. Check that the firewall allows port 8443. |
| Windows Firewall blocks port 8443 | No inbound rule | In an administrator console, run `New-NetFirewallRule -DisplayName SeyraGateway -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8443 -Profile Private -RemoteAddress LocalSubnet`. |
| Your PC's IP address changed | New network address | Run `revoke`, then repeat `pair` and `push` with the new address. |
| Certificate not recognized | The certificate was regenerated | Run `revoke`, then repeat `pair` and `push`. |
| Device not authorized | Revoked token, or a pairing file from another device | Run `revoke`, then repeat `pair` and `push`. |
| Chat connected, but no model reply | Missing key or no API credits | Check `Backend/.env.local`, your usage at [dev.meta.ai](https://dev.meta.ai) and the backend window. |
| Runtime disconnected, chat connected | The Unity Editor or an orphaned bridge is using port 4100 | Exit Play Mode, free port 4100 and restart the backend. |
| `adb` shows `unauthorized` | USB debugging not accepted | Accept the prompt in the headset. Then run `adb kill-server` and reconnect. |
| `push` fails with a permission error | The app data folder does not exist yet | Open the app once and repeat `push`. |
| No spoken replies | Missing Wit.ai token, or no internet on the headset | Set `WITAI_CLIENT_TOKEN`, restart the backend and check the headset's connection. |

To see the app logs, run:

```sh
adb logcat -s Unity
```
