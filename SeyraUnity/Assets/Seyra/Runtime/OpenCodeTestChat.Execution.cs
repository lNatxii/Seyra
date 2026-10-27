using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Seyra.UI
{
    public sealed partial class OpenCodeTestChat
    {
        async void Send()
        {
            var text = input.text.Trim();
            if (!connected || connecting || HasTurn || submissionInFlight || text.Length == 0) return;
            var token = lifetime.Token;
            submissionInFlight = true;
            pendingText = text;
            pendingEchoKey = "pending-" + Guid.NewGuid().ToString("N");
            cancelRequested = interruptAttempted = interruptAcknowledged = terminalConfirmed = unknownToolOutcome = false;
            activeUserCreated = 0;
            progressSignature = null;
            lastProgressUtc = DateTime.UtcNow;
            input.text = "";
            SetState(TurnState.Sending, "Enviando el mensaje una sola vez…");
            UpsertMessage(pendingEchoKey, "Tú", text);
            try
            {
                while (syncing) await Task.Delay(20, token);
                var before = await ReadMessagesAsync(token);
                submissionBaselineId = (string)before.LastOrDefault()?["id"];
                if (cancelRequested)
                {
                    terminalConfirmed = true;
                    SetState(TurnState.Cancelled, "Cancelado antes de enviar. No se ejecutaron acciones de este turno.");
                    return;
                }
                // Never retry admission: the server may execute tools even if its response is lost.
                var admission = await http.RequestAsync("POST", SessionPath + "/prompt", new JObject { ["text"] = text }, token);
                activeUserId = (string)admission["data"]?["id"];
                activeUserCreated = (long?)admission["data"]?["time"]?["created"] ?? 0;
                if (string.IsNullOrEmpty(activeUserId)) throw new InvalidOperationException("Submission was not confirmed. Reconnect to inspect the session; do not resend.");
                AdoptEcho(activeUserId);
                SetState(cancelRequested ? TurnState.Cancelling : TurnState.Running, cancelRequested ? "Esperando detener el turno…" : "El asistente está trabajando.");
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!token.IsCancellationRequested) ConnectionFailed(error); }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    submissionInFlight = false;
                    RefreshControls();
                    if (cancelRequested && connected && !terminalConfirmed) await InterruptAsync(token);
                }
            }
        }

        async Task SyncAsync(CancellationToken token, bool hydrate = false)
        {
            if (syncing) return;
            syncing = true;
            try
            {
                // Read session after messages so a newly admitted prompt cannot inherit an old idle.
                var messages = await ReadMessagesAsync(token, hydrate ? null : activeUserId);
                var session = (await http.RequestAsync("GET", SessionPath, null, token))["data"] as JObject
                    ?? throw new InvalidOperationException("Invalid API v2 session.");
                sessionMissing = false;
                SetConnected(true, "Conectado · " + agent);
                if (pendingText != null && activeUserId == null && !terminalConfirmed && !submissionInFlight)
                {
                    var baselineIndex = submissionBaselineId == null ? -1 : messages.FindIndex(m => (string)m["id"] == submissionBaselineId);
                    if (submissionBaselineId != null && baselineIndex < 0) throw new InvalidOperationException("Submission baseline is no longer available; outcome unknown.");
                    var candidates = messages.Skip(baselineIndex + 1).Where(m => (string)m["type"] == "user").ToArray();
                    if (candidates.Length == 1 && (string)candidates[0]["text"] == pendingText)
                    {
                        activeUserId = (string)candidates[0]["id"];
                        activeUserCreated = (long?)candidates[0]["time"]?["created"] ?? 0;
                        AdoptEcho(activeUserId);
                    }
                    else SetState(TurnState.Unresolved, "No se puede confirmar qué ocurrió con el envío. No se repetirá. Cancela para detener cualquier trabajo pendiente.");
                }
                RenderMessages(messages);
                if (activeUserId != null)
                {
                    var index = messages.FindIndex(m => (string)m["id"] == activeUserId);
                    if (index < 0) throw new InvalidOperationException("Active message is missing; no mutation will be retried.");
                    activeUserCreated = (long?)messages[index]["time"]?["created"] ?? activeUserCreated;
                    var turn = messages.Skip(index + 1).ToList();
                    var signature = string.Join("|", turn.Select(m => (string)m["id"] + ":" + (string)m["time"]?["completed"] + ":" +
                        string.Join(",", (m["content"] as JArray ?? new JArray()).OfType<JObject>()
                            .Where(p => (string)p["type"] != "reasoning").Select(p => (string)p["type"] + ":" + ((string)p["text"])?.Length + ":" + (string)p["state"]?["status"]))));
                    if (signature != progressSignature) { progressSignature = signature; lastProgressUtc = DateTime.UtcNow; }
                    var idle = turn.LastOrDefault(m => (string)m["type"] == "idle");
                    var sessionIdle = (long?)session["time"]?["idle"] ?? 0;
                    var outcome = (string)idle?["outcome"];
                    if (outcome == null && activeUserCreated > 0 && sessionIdle >= activeUserCreated) outcome = (string)session["outcome"];
                    if (outcome != null && !terminalConfirmed) FinishTurn(outcome, turn);
                    if (!terminalConfirmed)
                    {
                        await RefreshPermissionsAsync(token);
                        if (pendingPermission != null) lastProgressUtc = DateTime.UtcNow;
                        if (cancelRequested && interruptAttempted && !interruptInFlight && !interruptAcknowledged)
                            SetState(TurnState.Unresolved, "Cancelación sin confirmar. Puedes volver a pulsar Cancelar para solicitar la detención; no se repetirán las herramientas.");
                        else if (cancelRequested) SetState(TurnState.Cancelling, "Cancelación solicitada. Esperando confirmación del servidor; las acciones anteriores permanecen.");
                        else if (pendingPermission != null) SetState(TurnState.WaitingForPermission, "El turno espera tu decisión en el panel de permisos.");
                        else if ((DateTime.UtcNow - lastProgressUtc).TotalSeconds >= 120)
                            SetState(TurnState.Unresolved, "Sin progreso confirmado durante 120 s. Inspeccionando el mismo turno; no se reenviará nada.");
                        else SetState(TurnState.Running, "El asistente está trabajando. Puedes cancelar las acciones siguientes.");
                    }
                }
                FlushLayout();
            }
            finally { if (!token.IsCancellationRequested) { syncing = false; RefreshControls(); } }
            if (cancelRequested && !interruptAttempted && !terminalConfirmed) await InterruptAsync(token);
        }

        void FinishTurn(string outcome, List<JObject> messages)
        {
            terminalConfirmed = true;
            ClearPermissions();
            if (outcome == "succeeded" && !unknownToolOutcome)
            {
                SetState(TurnState.Completed, cancelRequested ? "El turno ya había terminado antes de la cancelación." : "Turno completado. Resultados confirmados por el servidor.");
                ResetTurn();
            }
            else if (outcome == "interrupted" || outcome == "cancelled")
                SetState(TurnState.Cancelled, "Turno interrumpido. Las acciones ya ejecutadas permanecen. Cierra el turno antes de continuar.");
            else
            {
                var failed = messages.FirstOrDefault(m => m["error"] != null);
                SetState(TurnState.Failed, unknownToolOutcome ? "Hay resultados de herramientas sin confirmar. Inspecciona la escena antes de repetir cambios y cierra el turno." : "El turno terminó con un error. Cierra el turno para continuar.");
                UpsertMessage("turn-error-" + activeUserId, "Sistema", unknownToolOutcome ? "Resultado de herramienta sin confirmar. Inspecciona la escena antes de repetir cambios." : (string)failed?["error"]?["message"] ?? "Turn outcome: " + outcome);
            }
        }

        async void CancelTurn()
        {
            if (!HasTurn || terminalConfirmed || interruptInFlight || (cancelRequested && (!interruptAttempted || interruptAcknowledged))) return;
            interruptAttempted = false; // A further click explicitly authorizes another stop, never a tool replay.
            cancelRequested = true;
            SetState(TurnState.Cancelling, "Solicitando cancelación. No se deshacen las acciones ya ejecutadas.");
            if (submissionInFlight) return; // Wait for admission before interrupting it.
            if (connected) await InterruptAsync(lifetime.Token);
        }

        async Task InterruptAsync(CancellationToken token)
        {
            if (interruptAttempted || interruptInFlight || terminalConfirmed || !HasTurn) return;
            interruptInFlight = interruptAttempted = true;
            RefreshControls();
            try
            {
                // A lost response is inspected through GET; this command is never automatically repeated.
                var result = await http.RequestAsync("POST", SessionPath + "/interrupt", new JObject(), token);
                if (result["interrupted"]?.Type != JTokenType.Boolean) throw new InvalidOperationException("Invalid interrupt acknowledgement; cancellation is unconfirmed.");
                interruptAcknowledged = true;
                if (activeUserId == null)
                {
                    // Admission is already settled locally. Inspect once more before allowing explicit closure.
                    while (syncing) await Task.Delay(20, token);
                    await SyncAsync(token);
                    if (activeUserId == null)
                    {
                        terminalConfirmed = true;
                        SetState(TurnState.Cancelled, "El servidor confirmó la detención. El resultado del envío sigue sin conocerse: inspecciona la escena antes de repetirlo.");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!token.IsCancellationRequested) ConnectionFailed(error); }
            finally { if (!token.IsCancellationRequested) { interruptInFlight = false; RefreshControls(); } }
        }

        void CloseTurn()
        {
            if ((!HasTurn && !sessionMissing) || (!terminalConfirmed && !sessionMissing) || submissionInFlight || interruptInFlight) return;
            UpsertMessage("closed-" + (activeUserId ?? pendingEchoKey), "Sistema", "Turno cerrado explícitamente. No se ha repetido ni revertido ninguna acción.");
            ResetTurn();
            ClearPermissions();
            SetState(TurnState.Ready, "Puedes enviar una nueva petición.");
            if (sessionMissing) { sessionId = null; sessionMissing = false; Reconnect(); }
        }

        void ResetTurn()
        {
            activeUserId = pendingText = pendingEchoKey = submissionBaselineId = null;
            activeUserCreated = 0;
            cancelRequested = interruptAttempted = interruptAcknowledged = unknownToolOutcome = false;
            RefreshControls();
        }
    }
}
