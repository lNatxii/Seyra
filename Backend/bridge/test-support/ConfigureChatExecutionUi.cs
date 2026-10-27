using System;
using Seyra.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ConfigureChatExecutionUi
{
    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Configure the saved scene outside Play Mode.");
        var chat = UnityEngine.Object.FindFirstObjectByType<OpenCodeTestChat>();
        var serialized = new SerializedObject(chat);
        var template = (TMP_Text)serialized.FindProperty("messageTemplate").objectReferenceValue;
        var parent = template.transform.root.Find("ChatLayout");
        if (parent == null || parent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() == null)
            throw new InvalidOperationException("The existing ChatLayout is required.");
        var existing = parent.Find("TurnControls");
        if (existing != null) throw new InvalidOperationException("TurnControls already exists; inspect instead of duplicating it.");
        var row = new GameObject("TurnControls", typeof(RectTransform), typeof(UnityEngine.UI.HorizontalLayoutGroup), typeof(UnityEngine.UI.LayoutElement));
        Undo.RegisterCreatedObjectUndo(row, "Add chat execution controls");
        row.transform.SetParent(parent, false);
        row.transform.SetSiblingIndex(1);
        var layout = row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 12;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;
        row.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 88;
        var labelObject = new GameObject("ExecutionLabel", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(UnityEngine.UI.LayoutElement));
        labelObject.transform.SetParent(row.transform, false);
        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.font = template.font;
        label.fontSize = 18;
        label.color = new Color(.86f, .91f, .97f);
        label.richText = false;
        label.text = "Listo\nEscribe un mensaje para empezar.";
        label.alignment = TextAlignmentOptions.MidlineLeft;
        labelObject.GetComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1;
        labelObject.GetComponent<UnityEngine.UI.LayoutElement>().minWidth = 100;
        labelObject.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 84;
        UnityEngine.UI.Button Button(string name, string text, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button), typeof(UnityEngine.UI.LayoutElement));
            gameObject.transform.SetParent(row.transform, false);
            var size = gameObject.GetComponent<UnityEngine.UI.LayoutElement>();
            size.preferredWidth = 126;
            size.preferredHeight = 46;
            gameObject.GetComponent<UnityEngine.UI.Image>().color = color;
            var button = gameObject.GetComponent<UnityEngine.UI.Button>();
            button.targetGraphic = gameObject.GetComponent<UnityEngine.UI.Image>();
            var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(gameObject.transform, false);
            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8, 4); rect.offsetMax = new Vector2(-8, -4);
            var caption = textObject.GetComponent<TextMeshProUGUI>();
            caption.font = template.font; caption.fontSize = 18; caption.text = text;
            caption.color = Color.white; caption.richText = false; caption.alignment = TextAlignmentOptions.Center;
            return button;
        }
        var cancel = Button("CancelTurnButton", "Cancelar", new Color(.40f, .19f, .13f));
        var close = Button("CloseTurnButton", "Cerrar turno", new Color(.12f, .32f, .55f));
        cancel.gameObject.SetActive(false); close.gameObject.SetActive(false);
        serialized.FindProperty("executionLabel").objectReferenceValue = label;
        serialized.FindProperty("cancelButton").objectReferenceValue = cancel;
        serialized.FindProperty("closeTurnButton").objectReferenceValue = close;
        serialized.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(chat.gameObject.scene);
        EditorSceneManager.SaveScene(chat.gameObject.scene);
        return "Execution status, cancel and explicit close controls saved with existing layout and references preserved.";
    }
}
