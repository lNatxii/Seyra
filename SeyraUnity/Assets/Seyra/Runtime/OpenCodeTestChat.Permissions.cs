using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace Seyra.UI
{
    public sealed partial class OpenCodeTestChat
    {
        [SerializeField] GameObject permissionPanel;
        [SerializeField] TMP_Text permissionHeading;
        [SerializeField] TMP_Text permissionDetails;
        [SerializeField] TMP_Text permissionStatus;
        [SerializeField] UnityEngine.UI.Button allowPermissionButton;
        [SerializeField] UnityEngine.UI.Button rejectPermissionButton;
        [SerializeField] UnityEngine.UI.ScrollRect permissionScroll;
        readonly HashSet<string> submittedPermissions = new HashSet<string>();
        JObject pendingPermission;
        bool replyingPermission;

        void BindPermissions()
        {
            allowPermissionButton.onClick.AddListener(AllowPermission);
            rejectPermissionButton.onClick.AddListener(RejectPermission);
            ClearPermissions();
        }

        void UnbindPermissions()
        {
            allowPermissionButton.onClick.RemoveListener(AllowPermission);
            rejectPermissionButton.onClick.RemoveListener(RejectPermission);
        }

        void ClearPermissions()
        {
            pendingPermission = null;
            submittedPermissions.Clear();
            replyingPermission = false;
            permissionPanel.SetActive(false);
        }

        async Task RefreshPermissionsAsync(CancellationToken token)
        {
            var response = await http.RequestAsync("GET", "/api/session/" + Uri.EscapeDataString(sessionId) + "/permission", null, token);
            var requests = response["data"] as JArray ?? throw new InvalidOperationException("Invalid v2 permission list.");
            var values = requests.OfType<JObject>().ToArray();
            if (values.Length != requests.Count || values.Any(p => string.IsNullOrWhiteSpace((string)p["id"]) ||
                string.IsNullOrWhiteSpace((string)p["action"]) || (string)p["sessionID"] != sessionId ||
                !(p["resources"] is JArray resources) || resources.Any(r => r.Type != JTokenType.String)))
                throw new InvalidOperationException("Invalid permission request or request from another session.");
            var next = values.FirstOrDefault();
            var changed = !JToken.DeepEquals(next, pendingPermission);
            pendingPermission = next;
            permissionPanel.SetActive(next != null);
            if (next == null) return;
            permissionHeading.text = "Permiso solicitado · " + (string)next["action"] + (values.Length > 1 ? " · " + values.Length + " pendientes" : "");
            permissionDetails.text = next.ToString(Formatting.Indented);
            var submitted = submittedPermissions.Contains((string)next["id"]);
            allowPermissionButton.interactable = rejectPermissionButton.interactable = !replyingPermission && !submitted;
            if (changed)
            {
                permissionStatus.text = "Permitir autoriza esta petición. Rechazar cancela también los permisos pendientes de esta sesión.";
                Canvas.ForceUpdateCanvases();
                permissionScroll.verticalNormalizedPosition = 1;
            }
            SetConnected(true, "Conectado · esperando permiso");
        }

        void AllowPermission() => ReplyPermission(true);
        void RejectPermission() => ReplyPermission(false);

        async void ReplyPermission(bool allow)
        {
            if (pendingPermission == null || replyingPermission) return;
            var id = (string)pendingPermission["id"];
            if (!submittedPermissions.Add(id)) return;
            var token = lifetime.Token;
            replyingPermission = true;
            allowPermissionButton.interactable = rejectPermissionButton.interactable = false;
            permissionStatus.text = "Enviando decisión…";
            try
            {
                // A decision can resume a mutation: do not retry an ambiguous response.
                await http.RequestAsync("POST", "/api/session/" + Uri.EscapeDataString(sessionId) + "/permission/" + Uri.EscapeDataString(id) + "/reply",
                    new JObject { ["decision"] = allow ? "once" : "reject" }, token);
                if (!token.IsCancellationRequested && (string)pendingPermission?["id"] == id) permissionStatus.text = "Decisión enviada. Esperando confirmación del servidor…";
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    if ((string)pendingPermission?["id"] == id) permissionStatus.text = "No se confirmó la decisión; no se reenvía. Resuélvela en OpenCode si sigue pendiente.";
                    AddMessage("Sistema", error.Message);
                }
            }
            finally { if (!token.IsCancellationRequested) replyingPermission = false; }
        }
    }
}
