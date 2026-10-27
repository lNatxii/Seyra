using System.Collections.Generic;
using Seyra.Chat;
using TMPro;
using UnityEngine;

namespace Seyra.UI
{
    public sealed partial class OpenCodeTestChat
    {
        readonly Dictionary<string, TMP_Text> messageRows = new Dictionary<string, TMP_Text>();
        bool layoutDirty;

        void RefreshControls()
        {
            if (sendButton != null)
                sendButton.interactable = session != null && session.CanSend;

            if (reconnectButton != null)
                reconnectButton.interactable = session != null && session.CanReconnect;

            if (cancelButton != null)
            {
                var canShowCancel = session != null && session.HasTurn && !session.IsSessionMissing &&
                    (session.ExecutionState == OpenCodeConversationSession.TurnState.Sending ||
                     session.ExecutionState == OpenCodeConversationSession.TurnState.Running ||
                     session.ExecutionState == OpenCodeConversationSession.TurnState.WaitingForPermission ||
                     session.ExecutionState == OpenCodeConversationSession.TurnState.Cancelling ||
                     session.ExecutionState == OpenCodeConversationSession.TurnState.Unresolved);
                cancelButton.gameObject.SetActive(canShowCancel);
                cancelButton.interactable = session != null && session.CanCancel;
            }

            if (closeTurnButton != null)
            {
                var canShowClose = session != null && (session.IsSessionMissing || session.CanCloseTurn ||
                    session.ExecutionState == OpenCodeConversationSession.TurnState.Completed ||
                    session.ExecutionState == OpenCodeConversationSession.TurnState.Cancelled ||
                    session.ExecutionState == OpenCodeConversationSession.TurnState.Failed);
                closeTurnButton.gameObject.SetActive(canShowClose);
                closeTurnButton.interactable = session != null && session.CanCloseTurn;
            }

            if (executionLabel != null)
            {
                string title;
                var state = session != null ? session.ExecutionState : OpenCodeConversationSession.TurnState.Ready;
                switch (state)
                {
                    case OpenCodeConversationSession.TurnState.Sending: title = "Enviando"; break;
                    case OpenCodeConversationSession.TurnState.Running: title = "En ejecución"; break;
                    case OpenCodeConversationSession.TurnState.WaitingForPermission: title = "Esperando permiso"; break;
                    case OpenCodeConversationSession.TurnState.Cancelling: title = "Cancelando"; break;
                    case OpenCodeConversationSession.TurnState.Completed: title = "Completado"; break;
                    case OpenCodeConversationSession.TurnState.Cancelled: title = "Interrumpido"; break;
                    case OpenCodeConversationSession.TurnState.Failed: title = "Fallo"; break;
                    case OpenCodeConversationSession.TurnState.Unresolved: title = "Resultado desconocido"; break;
                    default: title = "Listo"; break;
                }
                executionLabel.richText = false;
                var detail = session != null ? session.ExecutionDetail : executionDetail;
                executionLabel.text = title + "\n" + detail;
            }

            if (permissionPanel != null && allowPermissionButton != null && rejectPermissionButton != null)
            {
                var canReply = session != null && session.CanReplyPermission;
                allowPermissionButton.interactable = canReply;
                rejectPermissionButton.interactable = canReply;
            }
        }

        void AdoptEcho(string oldKey, string newKey)
        {
            if (oldKey != null && messageRows.TryGetValue(oldKey, out var row))
            {
                messageRows.Remove(oldKey);
                messageRows[newKey] = row;
            }
        }

        void UpsertMessage(string key, string role, string text)
        {
            if (messageRows.TryGetValue(key, out var existing))
            {
                existing.text = role + "\n" + text;
                return;
            }

            if (conversation == null || conversation.content == null || messageTemplate == null) return;

            var rowObject = Instantiate(messageTemplate.gameObject, conversation.content);
            rowObject.SetActive(true);
            var textComponent = rowObject.GetComponent<TMP_Text>();
            if (textComponent != null)
            {
                textComponent.text = role + "\n" + text;
                messageRows[key] = textComponent;
            }

            Canvas.ForceUpdateCanvases();
            conversation.verticalNormalizedPosition = 0f;
        }
    }
}
