# P0 / 01 — Functional validation

Date: 2026-10-27. Environment: Windows, Unity 6000.3.21f1, `OpencodeTestUI.unity` in Play Mode, actual OpenCode v2 on port 4096 and its MCP bridge on port 4100. This validates the Editor integration; headset validation belongs to roadmap 07.

## Results

Both real providers completed a new-session conversation covering query, creation, rename, Transform, reparenting, recoverable deletion, restoration, undo, save and load. Checks use tool results confirmed by Unity, independently of assistant prose. No scene mutation was submitted directly by the conversation driver.

| Run | Actual model reported by server | Confirmed scene calls | Evidence |
| --- | --- | --- | --- |
| Initial run | `helmcode/glm5.3-flash` | 28 | [GLM record](01-glm-evidence.json) |
| After model-selection fix | `opencode/deepseek-v4.1-flash-free` | 22 | [DeepSeek record](01-deepseek-evidence.json) |

The JSON records contain the exact user turns, message IDs, actual model IDs, turn outcomes, tool arguments and selected runtime result fields. Internal reasoning, credentials and unrelated tool output are excluded. Full filtered local transcripts are in `.utmp/conversation-<sessionId>.json`; those temporary files are not required to read the committed records.

### GLM sequence

1. Query the hierarchy in a new session; Generated is empty.
2. Create `Validacion01`, cube `Prueba01` and empty `Destino01`. Rename the cube to `CuboValidado01`, move it to `(2,1,3)`, reparent it to Destino01 with world position preserved, close the request and inspect the cube.
3. In another request, delete the cube, inspect `deleted=true`, restore it, inspect again and close the request. Object and component IDs remain unchanged.
4. Inspect history and undo restoration using the latest `expectedOperationId`; inspect `deleted=true`. Inspect history again and undo deletion; inspect the live cube. Save and list `validation01_20261027` (format v1, 4 nodes).
5. In a temporary request, move the cube to `(9,9,9)` and close the request. Load the slot and inspect the original ID: name, parent and `(2,1,3)` restored. Components have new IDs, as specified for reconstruction.

### DeepSeek sequence

1. Restart Play Mode and create a new session with the corrected client. Query Generated and create `DeepSeek01` in a closed request.
2. Rename it to `DeepSeekValidado01`, move it to `(3,2,1)`, create `DestinoDeepSeek01` and reparent with world position preserved. Delete, inspect, restore, inspect, close the request and save `validation01_deepseek_20261027` (v1, 4 nodes).
3. Inspect history, undo the last restoration with the inspected operation ID, inspect `deleted=true`, load the saved slot and inspect the original object ID. Name, parent and `(3,2,1)` return; component IDs change. List both saved slots.

## Blocker found and fixed

The scene selects `seyra_deepseek`, but the original session-creation request only sent the agent. This installed API selected the global GLM model despite the agent's DeepSeek model declaration. `/api/agent` reported the correct profile model; creating a session with that explicit model returned it in `/api/session/<id>` and assistant messages actually used DeepSeek.

`OpenCodeTestChat.ConnectAsync` now resolves the selected profile from `/api/agent` and forwards its declared model when creating the session. A profile without a model continues to inherit the server default. A missing profile produces a visible connection error. No model credential is stored in Unity and permissions are unchanged.

## Permissions and ask investigation

No `seyra_*` call in either functional run requested a permission. The deliberate real-server panel test requested `read` for `README.md`, covered by the general `ask` rule rather than `seyra_*: allow`. `once` resumed the actual read; requesting the same read again produced a new permission. The second request was rejected from the panel. See [permission evidence](01-permissions-evidence.json).

The real rejection returned `The user declined this tool call` on the read and `Step interrupted` on the assistant message. Unity displayed the latter, hid the panel, blocked further sends in that unresolved turn and enabled Reconnect; it did not retry. The isolated server's successful continuation after rejection is a UI regression fixture, not a claim that the actual server returns a successful idle after rejection. Session continuity remains roadmap 02.

Historical sessions inspected had no outstanding permissions. Two older interrupted sessions contained `question` tools asking how to proceed when primitives were unavailable, followed by `Interaction cancelled because the location shut down`; they do not prove a provider-plan restriction or identify the original reported permission request. The original incident cannot be retrospectively attributed to the free plan. Both current providers successfully executed the Unity tools.

The pre-existing local configuration includes `websearch: allow`; this validation did not introduce that rule or change configuration/profile permissions. Shell and file permissions were not broadened. The read tests use individual panel decisions, not saved permissions.

## Technical checks

- `npm test --prefix Backend/bridge`: both suites pass, including invalid arguments, confirmation, timeout without retry and disconnection with unknown outcome. Restricted execution initially denied local sockets; the same tests passed with local networking available.
- `SceneContractChecks.Run`: duplicate/conflicting transport IDs, expired mutation, stable IDs, inactive filtering, invalid parent/scale/range, destroyed IDs, primitive deduplication and invalid-primitive rollback pass.
- `LifecycleChecks.Run`: request/operation undo, rename/undo/persistence, subtree recovery, component identity, external-edit conflict, atomic overwrite, load undo, unsupported components, invalid-file rollback and slot path validation pass. Save/load into a newly constructed runtime also preserves object IDs and hierarchy.
- `RunUnityIntegration.Run`: all 20 MCP/runtime checks pass, including six primitive types, native dimensions/colliders, paging, local/world transforms, reparenting, cycles and stale revisions.
- Unity compilation completes without errors. Frame counters advanced throughout testing; entering Play Mode alone was not treated as evidence.
- `PermissionUiCheck.Run` uses an isolated server and checks literal permission text, model forwarding, allow/reject, continuation, hidden panel and restored send control. It also verifies the server received exactly one decision per request despite double invocation of each button.

## Reproduction

1. Start the backend with `Backend/start.ps1` and open `OpencodeTestUI.unity` in Play Mode. Ensure the runtime bridge is connected. Do not run another bridge on port 4100.
2. Use the exact turns in either evidence JSON, or send them through the actual input/send controls with `ConversationCheck.Send(string prompt)` using `unity command script run --caller plugin --skill unity-cli --file C:/Proyectos/Seyra/Backend/bridge/test-support/ConversationCheck.cs --entry ConversationCheck.Send --args '["<prompt>"]' --format json`.
3. Use `ConversationCheck.Inspect` between turns; do not submit while `sending=true`. Use `ConversationCheck.Record` after completion to export every page without internal reasoning.
4. Run `node Backend/bridge/test-support/verify-conversation.mjs <export.json> <summary.json>`. It rejects missing/failed tool confirmations, identity loss, wrong undo preconditions, incorrect deletion/restoration and incorrect post-load state.
5. For isolated permission regression, start `node Backend/bridge/test-support/permission-ui-server.mjs`, run `PermissionUiCheck.Run`, then stop that test server. Run this only when no real turn is active: it temporarily reconnects the test chat to port 4199 and leaves it disabled afterwards.

Slots created by validation are local ignored test content. History/trash remain runtime-only. This result does not close roadmap 02 (cancellation/continuity), 06 (spatial UI) or 07 (device connectivity).
