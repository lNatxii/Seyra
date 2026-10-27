using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace Seyra.UI
{
    public sealed partial class OpenCodeTestChat
    {
        readonly Dictionary<string, TMP_Text> messageRows = new Dictionary<string, TMP_Text>();
        bool layoutDirty;

        void SetState(TurnState state, string detail)
        {
            executionState = state;
            executionDetail = detail;
            sending = state == TurnState.Sending || state == TurnState.Running || state == TurnState.WaitingForPermission || state == TurnState.Cancelling;
            turnUnresolved = state == TurnState.Cancelled || state == TurnState.Failed || state == TurnState.Unresolved;
            RefreshControls();
        }

        void RefreshControls()
        {
            sendButton.interactable = connected && !connecting && !submissionInFlight && !HasTurn;
            reconnectButton.interactable = !connecting && !submissionInFlight && !interruptInFlight;
            if (cancelButton != null)
            {
                cancelButton.gameObject.SetActive(HasTurn && !terminalConfirmed && !sessionMissing);
                cancelButton.interactable = !connecting && connected && !interruptInFlight &&
                    (!cancelRequested || (interruptAttempted && !interruptAcknowledged));
            }
            if (closeTurnButton != null)
            {
                closeTurnButton.gameObject.SetActive(sessionMissing || (HasTurn && terminalConfirmed));
                closeTurnButton.interactable = !connecting && !submissionInFlight && !interruptInFlight;
            }
            if (executionLabel != null)
            {
                string title;
                switch (executionState)
                {
                    case TurnState.Sending: title = "Enviando"; break;
                    case TurnState.Running: title = "En ejecución"; break;
                    case TurnState.WaitingForPermission: title = "Esperando permiso"; break;
                    case TurnState.Cancelling: title = "Cancelando"; break;
                    case TurnState.Completed: title = "Completado"; break;
                    case TurnState.Cancelled: title = "Interrumpido"; break;
                    case TurnState.Failed: title = "Fallo"; break;
                    case TurnState.Unresolved: title = "Resultado desconocido"; break;
                    default: title = "Listo"; break;
                }
                executionLabel.richText = false;
                executionLabel.text = title + "\n" + executionDetail;
            }
            if (pendingPermission != null)
                allowPermissionButton.interactable = rejectPermissionButton.interactable = connected && !cancelRequested && !replyingPermission && !submittedPermissions.Contains((string)pendingPermission["id"]);
        }

        void AdoptEcho(string userId)
        {
            if (pendingEchoKey != null && messageRows.TryGetValue(pendingEchoKey, out var row))
            {
                messageRows.Remove(pendingEchoKey);
                messageRows["user-" + userId] = row;
            }
        }

        void RenderMessages(IEnumerable<JObject> messages)
        {
            var trackOutcome = false;
            foreach (var message in messages)
            {
                var id = (string)message["id"];
                if (string.IsNullOrEmpty(id)) continue;
                if ((string)message["type"] == "user")
                {
                    trackOutcome = id == activeUserId;
                    UpsertMessage("user-" + id, "Tú", (string)message["text"] ?? "");
                }
                if ((string)message["type"] != "assistant") continue;
                var parts = (message["content"] as JArray ?? new JArray()).OfType<JObject>().ToArray();
                // Reasoning, native provider payloads and arbitrary tool output are never rendered.
                var text = string.Join("\n", parts.Where(p => (string)p["type"] == "text").Select(p => (string)p["text"]));
                if (!string.IsNullOrWhiteSpace(text)) UpsertMessage("assistant-" + id, "Seyra", text);
                for (var index = 0; index < parts.Length; index++)
                {
                    var part = parts[index];
                    if ((string)part["type"] != "tool") continue;
                    var key = "tool-" + id + "-" + ((string)part["id"] ?? index.ToString());
                    UpsertMessage(key, "Herramienta", ToolSummary(part, trackOutcome));
                }
            }
        }

        string ToolSummary(JObject part, bool trackOutcome)
        {
            var name = (string)part["name"] ?? "tool";
            var state = part["state"] as JObject;
            var status = (string)state?["status"];
            string label = status == "completed" ? "completada" : status == "error" ? "falló" : status == "pending" ? "pendiente" : "en ejecución";
            var detail = "";
            if (name.StartsWith("seyra_", StringComparison.Ordinal) && status == "completed")
            {
                var payload = (state?["content"] as JArray)?.OfType<JObject>().FirstOrDefault(p => (string)p["type"] == "text");
                try
                {
                    var result = JObject.Parse((string)payload?["text"] ?? "{}");
                    if (trackOutcome && (bool?)result["outcomeUnknown"] == true) unknownToolOutcome = true;
                    if ((bool?)result["success"] == false)
                    {
                        label = (bool?)result["outcomeUnknown"] == true ? "resultado desconocido" : "rechazada";
                        detail = (string)result["error"]?["code"];
                    }
                    else if ((bool?)result["success"] == true)
                    {
                        label = "confirmada";
                        detail = (string)result["data"]?["name"] ?? (string)result["data"]?["slot"];
                        if (result["data"]?["deleted"]?.Type == JTokenType.Boolean)
                            detail = (detail ?? "Objeto") + ((bool)result["data"]["deleted"] ? " · en papelera" : " · disponible");
                    }
                    else { label = "respuesta sin confirmar"; if (trackOutcome) unknownToolOutcome = true; }
                }
                catch (Newtonsoft.Json.JsonException) { label = "respuesta sin confirmar"; if (trackOutcome) unknownToolOutcome = true; }
            }
            if (status == "error") detail = (string)state?["error"]?["message"];
            return ToolTitle(name) + " · " + label + (string.IsNullOrEmpty(detail) ? "" : "\n" + detail.Substring(0, Math.Min(detail.Length, 200)));
        }

        static string ToolTitle(string name)
        {
            switch (name)
            {
                case "seyra_scene_hierarchy": return "Consultar jerarquía";
                case "seyra_scene_find": return "Buscar objetos";
                case "seyra_scene_get": return "Consultar objeto";
                case "seyra_create_gameobject": return "Crear contenedor";
                case "seyra_create_primitive": return "Crear primitiva";
                case "seyra_rename_object": return "Renombrar objeto";
                case "seyra_set_transform": return "Mover, girar o escalar";
                case "seyra_set_parent": return "Cambiar de contenedor";
                case "seyra_request_begin": return "Abrir petición";
                case "seyra_request_end": return "Cerrar petición";
                case "seyra_request_list": return "Consultar peticiones";
                case "seyra_delete_object": return "Enviar a papelera";
                case "seyra_restore_object": return "Recuperar objeto";
                case "seyra_history_list": return "Consultar historial";
                case "seyra_undo": return "Deshacer";
                case "seyra_content_save": return "Guardar contenido";
                case "seyra_content_load": return "Cargar contenido";
                case "seyra_content_list": return "Consultar guardados";
                case "read": return "Leer archivo";
                default: return name;
            }
        }

        void AddMessage(string role, string text) => UpsertMessage("local-" + Guid.NewGuid().ToString("N"), role, text);

        void UpsertMessage(string key, string role, string text)
        {
            if (!messageRows.TryGetValue(key, out var row) || row == null)
            {
                row = Instantiate(messageTemplate, conversation.content);
                row.name = "Message_" + role;
                row.richText = false;
                row.gameObject.SetActive(true);
                messageRows[key] = row;
            }
            var value = role + "\n" + text;
            if (row.text == value) return;
            row.text = value;
            layoutDirty = true;
            FlushLayout();
        }

        void FlushLayout()
        {
            if (!layoutDirty) return;
            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(conversation.content);
            conversation.verticalNormalizedPosition = 0;
            layoutDirty = false;
        }
    }
}
