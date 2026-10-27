# P0 / 02 — Execution state, cancellation and chat continuity

Date: 2026-10-27. Verified in `OpencodeTestUI.unity`, Unity 6000.3.21f1 on Windows, using the installed OpenCode v2 server and actual DeepSeek provider. Device networking and app-restart recovery remain outside this delivery.

## Delivered behavior

- Connection and execution state are independent. The status row shows ready, sending, running, waiting for permission, cancelling, completed, interrupted, failed or unknown outcome.
- Assistant text and tool activity appear during the turn. Tool rows update by server message/tool identity and summarize confirmed results. Reasoning parts, native provider payloads and arbitrary tool arguments/output are not displayed. Text is literal, with rich text disabled.
- Cancel sends `POST /api/session/<id>/interrupt`. An in-flight prompt admission settles before cancellation is sent. An interrupt acknowledgement alone is not labeled success: the client inspects the current user message and the server's terminal outcome. If completion won the race, the UI reports completion rather than claiming it cancelled the finished work.
- Cancellation stops subsequent OpenCode actions. It does not undo actions already dispatched or executed; an already dispatched runtime action can still finish. Interrupted and failed turns retain their identity and require **Cerrar turno** before another user request.
- Reconnect first inspects the existing session. Component disable/re-enable retains the session, active user message, transcript rows and permission decisions. It neither creates a replacement session nor resends the prompt. A missing server session requires explicit closure before a new conversation is created.
- If admission's response is lost, the client searches after the pre-submission message boundary. It adopts a user message only when exactly one new candidate matches the submitted text. Otherwise it shows unknown outcome and blocks another submission. It never guesses or retries the prompt.
- If an interrupt response is lost, reads determine whether the turn stopped. If it did not, another stop requires an explicit click; no stop command is automatically retried. Submitted permission IDs also survive re-enable, preventing a lost decision response from making the same decision sendable again.
- A runtime tool result marked `outcomeUnknown` cannot produce a completed state even if the model's final prose claims success. The user must inspect the scene before repeating changes and close the turn explicitly.
- A turn without visible progress for 120 seconds is marked unresolved while read-only inspection continues. New text, completed messages and tool-state changes refresh progress; permission waiting does not consume that budget. There is no automatic resubmission.

Closing a chat turn acknowledges its interruption; it is not undo and does not silently modify a content request group. An open content request is identified by its confirmed tool row and remains queryable. The real test explicitly asks the agent to inspect and close that group in the next turn, through the existing `request_list` / `request_end` tools.

## Transport defect discovered during validation

The first fault-injection run used a server that admitted a prompt and then closed the connection without replying. The original UnityWebRequest transport delivered that POST twice on a reused connection, despite no application-level retry loop. The client correctly refused to pick between two matching user messages, exposing the duplicate rather than masking it.

`JsonHttpClient` now uses `HttpClient` with connection reuse disabled for these requests (`Connection: close`), redirects disabled and no retry loop. The same dropped-response test records exactly one admitted user message. This applies to prompts, permission decisions and interrupt commands. HTTP requests retain a 15-second timeout; provider credentials remain backend-only, and the local HTTP password is held only in memory.

## Automated fault-injection result

[Execution evidence](02-execution-checks.json) records the complete server event journal. The final run admits exactly **13 user messages**, sees **2 unconfirmed/unadmitted attempts**, receives **9 stop requests** (including deliberate second clicks authorizing another stop) and **2 permission decisions**. Exactly **7 simulated mutations** finish; stopped scenarios do not execute their deferred mutations. There are two sessions: the original and one replacement created only after explicit closure of a simulated unavailable session.

`ChatExecutionChecks.Run` drives the actual Unity input and buttons and checks:

1. Sending, permission waiting and successful completion, with one permission decision despite double invocation.
2. Cancellation while waiting for permission, no rollback claim and explicit closure.
3. Cancellation during delayed prompt admission.
4. Read outage, original-session reconnect and no resubmission.
5. Lost prompt response, exact user-message reconciliation and one local echo.
6. Lost interrupt response, terminal inspection and no automatic retry.
7. Component disable/re-enable while the turn is running.
8. Unknown runtime outcomes and provider failures; an acknowledged historical unknown result does not poison another turn after rehydration.
9. Cancellation/completion race.
10. Lost permission-decision response across re-enable.
11. Interrupt that never reaches execution, followed by a separately authorized second stop.
12. Unconfirmed admission with no server user message, without pretending a known execution result.
13. Cancellation queued during a delayed, unconfirmed admission.
14. Missing session and explicit replacement.
15. Exact request counts, transcript de-duplication, literal output and no reasoning/raw-input leakage.

The existing permission panel regression also passes with the new transport. Both `npm test --prefix Backend/bridge` suites pass; Unity compilation completes without errors.

## Real OpenCode result

[Real evidence](02-real-execution.json) records session `ses_e82ec5486ffe2DU60eYovb7mgg`, using `opencode/deepseek-v4.1-flash-free`.

1. The model opens `CancelValidation02` and creates `BeforeCancel02` through the real MCP bridge.
2. It requests `read` for the first three README lines before the instructed subsequent creation, `AfterCancel02`.
3. Reconnect and component re-enable retain the same session, permission ID and four visible rows; no prompt is resubmitted.
4. Cancel is invoked twice. The server reports `outcome=interrupted`, removes pending permissions and marks the read tool `Tool execution interrupted`.
5. Runtime inspection confirms exactly one `BeforeCancel02` and zero `AfterCancel02`. Prior content survives; the subsequent creation is absent.
6. The send control remains blocked until explicit turn closure. A second user message in the same session asks to close the open content request and inspect the scene. It completes successfully; `request_list` reports no active group. The server contains exactly two user messages.

## Reproduction

For fault injection, start `node Backend/bridge/test-support/chat-execution-server.mjs`, enter Play Mode in the test scene and run:

```powershell
unity command script run --caller plugin --skill unity-cli --format json -- --file C:/Proyectos/Seyra/Backend/bridge/test-support/ChatExecutionChecks.cs --entry ChatExecutionChecks.Run --timeout 150
```

The fixture temporarily selects port 4202, requires no model provider and leaves the component disabled afterwards. Restart the fixture between runs so event counts begin at zero. Stop the fixture after testing.

For the real-provider check, start the normal backend, enter a fresh Play Mode session and run `RealChatExecutionChecks.Run` through the same script command with a 180-second timeout. It creates only named validation content and deliberately leaves a README read waiting for permission before pressing Cancel. Its direct runtime calls are read-only inspections; scene mutations and request closure come from the actual conversation. It writes `.utmp/02-real-execution.json`.

The client currently retains continuity only while the component instance survives. Exiting Play Mode, recreating the scene, a domain reload or restarting the app does not restore conversation identity; durable workspace/conversation recovery belongs to roadmap 21. Pagination is bounded at 2000 messages and fails visibly rather than silently truncating. The transport and UI were validated in the Editor, not on a headset.
