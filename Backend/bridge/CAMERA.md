# Camera look contract

`camera_look` is registered directly by the MCP server (OpenCode exposes it as
`seyra_camera_look`). It is not a scene batch operation and is absent from
`baseTools`, `scene_build` and `scene_edit`. Its only argument is a trimmed,
non-empty `question` of at most 1000 characters. The vision model receives this
standalone question and one photo, without the conversation.

## Runtime operations

These operations are internal to the Unity bridge and are not MCP tools. All
take `{}`. They are classified as queries, bypassing mutation history and the
`completed` response cache so JPEG bytes are not retained there.

| Operation | Result data |
| --- | --- |
| `camera_capture_start` | `{state:"started"}`, `{state:"busy"}` or `{state:"unavailable"}` |
| `camera_capture_result` | `{state:"pending"}`, `{state:"unavailable"}`, `{state:"failed",message}` or `{state:"ready",captureId,width,height,jpegBase64}` |
| `camera_capture_cancel` | `{state:"idle"}` or `{state:"unavailable"}` if no service exists |

Unity locates and caches the active `CameraCaptureService`. Start calls Capture
only when it is not busy. Result consumes Ready with Take, encodes the JPEG as
base64 and clears its byte reference immediately afterward, even on failure.
The pending popup survives Take for the remainder of its three seconds, then
releases its texture. A new capture or Clear closes it immediately.

## Vision request and result

The bridge polls sequentially every 250 ms for at most 10 seconds including
start/result waits. A timeout invokes cancel once, allowing at most one second
for that response. It does not retry a capture or a Meta request. Lost result
responses lose the photo; there is no implicit recapture.

Meta receives `POST https://api.meta.ai/v1/responses`, authenticated with the
bridge's `META_API_KEY`: `muse-spark-1.3`, `store:false`,
`max_output_tokens:2000`, `reasoning:{effort:"low"}`, the approved vision
instructions, and one user message containing `input_text` and an `input_image`
JPEG data URL. The request has `AbortSignal.timeout(13000)`.

A valid reply must have `status:"completed"` and non-empty text from
`output[type=message].content[type=output_text].text`. Text is joined with
newlines. A root `output_text` field is never read.

Success is `{success:true,data:{answer,captureId,width,height}}`. Failures use
the existing `{success:false,error:{code,message}}` envelope:

| Code | Meaning |
| --- | --- |
| `CAMERA_NOT_CONFIGURED` | Missing Meta key |
| `CAMERA_LIMIT` | 20 capture attempts already started within the last 10 minutes |
| `CAMERA_BUSY` | A bridge call or Unity capture is already running |
| `CAMERA_UNAVAILABLE` | No active camera service in Unity |
| `CAMERA_FAILED` | Capture failed; message comes from Unity |
| `CAMERA_TIMEOUT` | Capture deadline expired; accept any permission prompt and ask again |
| `VISION_FAILED` | Meta failed, timed out, returned incomplete status or no answer text |

Unity invoke errors are returned unchanged. One call runs at a time in each
bridge process; that lock lasts through Meta processing and is released in
`finally`. The rolling budget is also in process memory and resets on restart.
The camera is read-only with respect to scene content, but calling it takes a
new photo; it is deliberately not marked idempotent.

## Retention and integration

JPEG/base64 lives only in memory during capture and analysis. It is not logged,
written to disk, returned to OpenCode or placed in generated content, undo,
trash or saved slots. OpenCode stores the question and textual answer only.
Unity keeps at most eight metadata records without bytes. Meta receives the
photo with `store:false`; this flag is not a claim about all provider-side
operational retention.

The scene must contain a root CameraCapture object with CameraCaptureService
and CameraCaptureFeedback, with the shutter clip and popup assigned. Closing
the chat must not deactivate this object. Scene setup is a separate manual
step. A model instruction restricts use to explicit visual requests; this is
not a technical permission gate. Canceling an OpenCode turn may not reach the
bridge, so an already-started call can still complete and send one photo.
