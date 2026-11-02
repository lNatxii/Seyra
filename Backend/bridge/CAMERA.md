# Camera contracts

`camera_look` is registered directly by the MCP server (OpenCode exposes it as
`seyra_camera_look`). It is not a scene batch operation and is absent from
`baseTools`, `scene_build` and `scene_edit`. Its only argument is a trimmed,
non-empty `question` of at most 1000 characters. The vision model receives this
standalone question and one photo, without the conversation.

## Runtime operations

These operations are internal to the Unity bridge and are not MCP tools. All
capture operations take `{}`; `camera_locate` takes the arguments below. They
are classified as queries, bypassing mutation history and the
`completed` response cache so JPEG bytes are not retained there.

| Operation | Result data |
| --- | --- |
| `camera_capture_start` | `{state:"started"}`, `{state:"busy"}` or `{state:"unavailable"}` |
| `camera_capture_result` | `{state:"pending"}`, `{state:"unavailable"}`, `{state:"failed",message}` or `{state:"ready",captureId,width,height,jpegBase64}` |
| `camera_capture_cancel` | `{state:"idle"}` or `{state:"unavailable"}` if no service exists |
| `camera_locate` | `{state:"ready",regions:[...]}`, `{state:"unavailable",regions:[]}` or `{state:"no_depth",regions:[]}` |

Unity locates and caches the active `CameraCaptureService`. Start calls Capture
only when it is not busy. Result consumes Ready with Take, encodes the JPEG as
base64 and clears its byte reference immediately afterward, even on failure.
The pending popup survives Take for the remainder of its three seconds, then
releases its texture. A new capture or Clear closes it immediately.

### camera_locate

This internal query reads depth for an existing capture; it takes no new photo
and creates no scene content. Input:

```json
{ "captureId": "cap_a1b2c3d4e5f6", "regions": [
  { "points": [{ "x": 100, "y": 200 }, { "x": 110, "y": 210 }, { "x": 120, "y": 220 }] }
] }
```

`captureId` is a non-empty string. At most eight regions and 32 points per
region are accepted. Empty arrays are allowed. Each point requires finite
numeric `x` and `y`, in source-photo pixels measured from the top left.
Pixels outside `[0,width)` / `[0,height)` and cells without depth are ignored.
Invalid argument shapes or limits use the normal Unity error envelope.

Example result data (inside the normal success envelope):

```json
{ "state": "ready", "regions": [
  { "position": { "x": 0.1, "y": 0.8, "z": 1.2 },
    "rotation": { "x": 10, "y": 180, "z": 0 },
    "valid": 3, "requested": 3,
    "points": [{ "x": 0.1, "y": 0.8, "z": 1.2 },
               { "x": 0.11, "y": 0.81, "z": 1.21 },
               { "x": 0.09, "y": 0.79, "z": 1.19 }] }
] }
```

`ready` preserves input region order. A region is `null` unless at least three
samples and 40% of its requested points have depth. `valid` counts successful
samples; `requested` counts input points. `position` is the per-axis median
of the valid world points, converted to local coordinates of `generated`
with `generated.InverseTransformPoint`. Returned `points` are the valid samples
in the same local coordinates, not the depth grid. `rotation` contains local
Euler angles in degrees relative to `generated`, derived from
`Quaternion.LookRotation(worldPosition - CameraPose.position, Vector3.up)`.
These coordinates can be used directly by `scene_build` without placement.

`unavailable` means no camera service exists. `no_depth` means the capture was
not found or has no depth grid; both return `regions: []`. The optional grid
is 40 × 30 with a six-metre ray limit. Depth is an estimate from capture time:
it does not track objects, and thin, shiny or transparent objects can lack
usable samples. Image Y mapping and visible label orientation still require
the full headset validation in feature 15a, step 6.

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
`camera_look` is read-only with respect to scene content, but calling it takes a
new photo; it is deliberately not marked idempotent.

## Retention and integration

With `camera_look`, JPEG/base64 lives only in memory during capture and
analysis. It is not logged, written to disk, returned to OpenCode or placed in
generated content, undo, trash or saved slots.

With `camera_segment`, the composed image (at most 512 px on its longest side,
including the camera photo as its background) is delivered to the headset and
written to `Media/<hex>.jpg`. Saved slots reference that local image. Deleting
the card does not delete the file; RemoteMediaStore does not remove it. The
full-resolution photo is not saved. A delivery can leave a file even if card
creation subsequently fails.

OpenCode receives text only, never photo bytes or base64; its session history
can retain the question, answer and textual tool results. Unity retains at most
eight recent capture records without JPEG bytes, including their optional depth
grids. The grid lives only in headset memory and is released with its recent
record; it is never saved or sent off the headset. Only calculated positions
(the region median and valid sample positions, plus label rotation and counts)
leave the headset for depth localization. `camera_segment` does not forward the
sample points to OpenCode. Muse Spark and SAM 3.1 receive the
photo through the Meta Model API with `store: false`. This describes the request
and makes no claim about the provider's internal retention.


## Segmentation tool: camera_segment

`camera_segment` is registered directly beside `camera_look` (exposed as
`seyra_camera_segment`), outside scene batches. It creates a card, so its MCP
annotations are `readOnlyHint: false`, `destructiveHint: false` and
`idempotentHint: false`. Its only argument is
`{ question: string }`: trimmed, non-empty and at most 1000 characters. The
question must be complete and standalone. Capture shows the same shutter and
three-second popup as look. The card is a flat picture; when depth is available,
the tool also creates points, labels and lines at located objects in the room.

Both tools share one in-process lock and the rolling limit of 20 capture
attempts per ten minutes. The lock lasts through the entire call, including
image composition, delivery, card creation and placement. There are no retries
of capture, model requests or mutations. Restarting the bridge resets the limit.

### Muse and SAM requests

Both use `POST https://api.meta.ai/v1/responses`, authenticated with the
bridge's `META_API_KEY`, without streaming and with `store: false`.

Muse uses `model: "muse-spark-1.3"`, `max_output_tokens: 2000`,
`reasoning: { effort: "low" }`, approved segmentation instructions and
`input: [{ role: "user", content: [{ type: "input_text", text: question },
{ type: "input_image", image_url: "data:image/jpeg;base64,<jpeg>" }] }]`.
It has a 7000 ms timeout. Instructions request English JSON
`{ answer: string, objects: string[] }`, at most six distinct separate objects
that a person could point at or pick up, smallest and most interesting first;
never surfaces, structures, people or body parts. The response must be
`completed`. Message `output_text` values are joined, JSON is extracted, and
answer must contain 1–2000 characters. Invalid object names are discarded;
accepted names match `^[A-Za-z][A-Za-z0-9 '-]{1,59}$`, are deduplicated ignoring
case and limited to six. No objects means empty, without SAM.

For each name, SAM receives:

```json
{
  "model": "sam-3.1", "store": false,
  "metadata": { "include_confidence": "true" },
  "input": [{ "type": "message", "role": "user", "content": [
    { "type": "input_text", "text": "<noun>" },
    { "type": "input_image", "image_url": "data:image/jpeg;base64,<jpeg>" }
  ] }]
}
```

SAM requests run in parallel with Promise.allSettled and one shared timeout
signal. Each response must be completed. Message output_text is adapted to
Responses events for `@meta-sam/parser`; empty text is zero matches. The parser's
mask bounds are used directly: x2 and y2 are exclusive. Mask confidence below
0.5 or missing is discarded. Remaining detections are sorted by descending
confidence and limited to eight. A decodeMaskToRaster exception leaves a null
raster and retains its box; rendering then draws the box and label only.
Individual failed SAM requests are discarded; all failed means failed.

### Result and card

```json
{ "success": true, "data": {
  "answer": "A ceramic mug is visible.",
  "captureId": "cap_a1b2c3d4e5f6", "width": 1280, "height": 960,
  "segmentation": "ok",
  "objects": [{ "label": "ceramic mug", "confidence": 0.93,
    "box": { "x1": 910, "y1": 600, "x2": 1151, "y2": 841 },
    "position": { "x": 0.1, "y": 0.8, "z": 1.2 } }],
  "panel": { "entityId": "<runtime-id>", "placed": true },
  "labels": { "rootId": "<composition-root-id>", "located": 1, "total": 1 }
} }
```

Width and height describe the captured photo. Boxes use source pixels and
half-open bounds. Results contain text and metadata only, never JPEG or raster.
`objects[].position` is an estimated position in local coordinates of
`generated`, or `null` when not located. `labels` is `null` unless creation of
the label composition is confirmed; otherwise `rootId` identifies that
composition, `located` counts its marked detections and `total` counts all
retained detections. A position can remain non-null even if label creation
fails. No internal sample `points` are returned by this MCP tool.

| segmentation | Meaning | panel |
| --- | --- | --- |
| ok | Detections exist and card creation is confirmed | {entityId, placed} |
| empty | Muse lists no objects, or successful SAM requests yield no retained detections | null |
| failed | No SAM time remains, all SAM requests fail, or composition, delivery or card creation fails | null |

A failure after Muse preserves answer and any detections already available.
`placed: false` means the card was created but placement failed, was not
confirmed or returned applied false; it remains at its initial pose.

The bridge renders a JPEG of at most 512 px, with coloured masks, boxes and
labels, and at most 1 MiB. It calls `media_deliver` first, then `ui_present`
with a mediaCard and an imageRef (never a media field), then `spatial_place`
with destination air, allowAirFallback true and minScale 1. Each invoke must
confirm success. Delivery/creation failures yield failed; placement failures
yield placed false. No mutation is retried. Closing, grabbing, listening,
undo and saving reuse the card's existing behavior. Creation and placement are
two undo operations. Logs never include base64, keys or provider response bodies.

After card creation and the placement attempt, the bridge calls `camera_locate`
with up to 24 mask samples per detection (or a 3 × 3 grid in the central 50%
of its box without a mask). Valid regions produce one `scene_build` composition:
a 3 cm sphere, a label 10 cm above it and a connecting line per object. Their
colours use the same original detection index as the image. No placement or
retries are applied to this composition. Missing depth, localization failures
and label creation failures preserve the card and its segmentation result.

The label composition is one additional undo operation, so the first undo
removes its points, labels and lines together. Labels have no Close button;
deletion uses the existing chat operations. They can be saved as normal scene
content, but have no persistent spatial anchors: after restart they can be
misaligned. Moving a physical object does not move its label.

### Errors and timing

Capture uses the same CAMERA_NOT_CONFIGURED, CAMERA_LIMIT, CAMERA_BUSY,
CAMERA_UNAVAILABLE, CAMERA_FAILED and CAMERA_TIMEOUT codes listed above;
Unity invoke errors during capture are returned unchanged. VISION_FAILED means
Muse failed, timed out, was incomplete or returned invalid JSON, so no answer
is available. A SAM or card failure is a successful tool envelope with
segmentation failed and the Muse answer, rather than VISION_FAILED.

| Phase | Budget |
| --- | --- |
| Capture | Up to 10 s, with the existing one-second cancellation allowance on capture timeout |
| Muse | Up to 7 s |
| Parallel SAM | Math.min(8000, 21000 - elapsed) ms from the start of the tool call |
| Composition, delivery, creation and placement | 3 s reserved |
| Margin within the 25 s MCP execution budget | 1 s |

If the SAM budget is not positive, no SAM request is sent and segmentation is
failed. Its one AbortSignal.timeout uses the computed budget. The reservation
is a budget plan, not a measured or enforced three-second composition/mutation
deadline; invoke uses the existing transport timeouts. camera_look retains its
13 s vision timeout. Canceling a turn can still allow an initiated segment
call to finish and create a card.
