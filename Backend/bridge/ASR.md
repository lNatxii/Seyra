# ASR relay contract

The authenticated HTTPS gateway exposes Muse Voice Transcribe using polling.
Every request requires `Authorization: Bearer <device token>` and certificate
pinning as described in [GATEWAY.md](GATEWAY.md). The provider key stays on the PC.
Authentication, failure throttling and revocation follow the gateway contract.
Only the session owner can access its ID; other devices receive 410.

| Method and path | Body | Response |
| --- | --- | --- |
| POST `/_seyra/asr/start` | JSON `{}`, at most 1 KiB | 200 `{ "asrId": "<uuid>" }`; 503 `ASR_NOT_CONFIGURED`; 502 `ASR_UPSTREAM` |
| POST `/_seyra/asr/audio?asrId=...` | Binary PCM s16le, mono, 16 kHz; nonempty, even length, at most 64 KiB | 200 `{}`; 400 invalid length; 409 stopped/terminal session or backlog; 410 expired/foreign ID; 413 oversized |
| GET `/_seyra/asr/events?asrId=...&after=N&wait=10` | None | 200 snapshot below; 409 another poll pending; 410 expired/foreign ID |
| POST `/_seyra/asr/stop?asrId=...` | `{}` | 200 `{}`; 410 expired/foreign ID; idempotent |
| POST `/_seyra/asr/cancel?asrId=...` | `{}` | 200 `{}`, including missing/expired IDs; foreign sessions are unaffected |

All responses use `Content-Type: application/json` and `Cache-Control: no-store`.
Error responses carrying a code use `{ "error": "CODE" }`.

```json
{ "version": 1, "partial": "Hola", "final": null, "done": false, "error": null }
```

Polls return when `version > after`, the session is terminal, or the wait expires
(clamped to 0–10 seconds). Each cumulative partial replaces the previous one.
Final text sets `final` and `done: true`; errors set `error`. Both increase the
version. Terminal state remains available for 30 seconds, then returns 410.
Only one session exists: a new start replaces the previous session and ends its
pending poll with 410. Cancel and server shutdown release sockets and timers.
Clients must not retry requests automatically.

The relay opens `wss://api.meta.ai/v1/asr/realtime?sessionId=<uuid>`, sends the
credential-bearing handshake first, and waits at most 8 seconds for the untyped
session acknowledgement. It uses `muse-voice-transcribe-1.0`, `PCM_16KHZ`,
`PUSH_TO_TALK`, `CUMULATIVE`, no audio-progress events, language bias Spanish and
English, and keyword Seyra. The provider configuration comes from `META_API_KEY`.
`SEYRA_ASR_URL` overrides the endpoint only when `NODE_ENV=test`, for local tests.
The handshake and transcript fields follow [Meta Voice schemas](https://dev.meta.ai/docs/api-reference/voice/schemas).

Audio drains every 100 ms, up to 6400 bytes per tick to catch up. At session start
and whenever the queue empties before stop, buffering resumes: ticks send 3200
zero bytes without consuming queued audio until 9600 bytes (300 ms) accumulate.
Stop bypasses buffering, drains queued audio, sends `endStream` once,
and waits at most 15 seconds for final text. The queue holds at most 96000 bytes
(3 seconds); sessions stop automatically at 60 seconds. Five seconds without
client audio causes `ASR_IDLE`; 30 seconds without polling closes the session.
Stop/cancel bodies are limited to 1 KiB. Unknown upstream event types are ignored.

| Code | Meaning / localized client message |
| --- | --- |
| ASR_NOT_CONFIGURED | El backend no tiene META_API_KEY |
| ASR_UPSTREAM | Muse Voice Transcribe no respondió. Vuelve a intentarlo |
| ASR_RATE_LIMITED | Límite de transcripciones alcanzado. Espera un momento |
| ASR_TIMEOUT | La transcripción tardó demasiado |
| ASR_IDLE | Se cortó el audio. Vuelve a dictar |
| ASR_BACKLOG | Se cortó el audio. Vuelve a dictar |
| HTTP 410 | La sesión de voz caducó. Vuelve a dictar |
| Network/certificate failure | Client uses `ConnectionErrors.Describe(error, settings)` |

Upstream close 1013 maps to `ASR_RATE_LIMITED`; other premature closures and
upstream error events map to `ASR_UPSTREAM`. Provider errors never return raw
upstream diagnostics to the device. Logs contain only the session ID, generic code and duration; no raw provider message.
