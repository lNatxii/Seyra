using System;
using System.Text.RegularExpressions;

namespace Seyra.Chat
{
    public static class ChatActivityText
    {
        public const string Thinking = "Pensando…";
        public const string Working = "Trabajando…";

        // A null description preserves the previous activity for request boundaries.
        public static string Describe(string toolName)
        {
            var name = toolName ?? "";
            if (name.StartsWith("seyra_", StringComparison.Ordinal)) name = name.Substring(6);
            switch (name)
            {
                case "scene_hierarchy": case "scene_find": case "scene_get": case "scene_bounds":
                case "request_list": case "history_list": case "content_list": case "ui_catalog":
                case "ui_media_list": case "ui_list": case "ui_get":
                    return "Revisando lo que hay a tu alrededor…";
                case "room_get": case "room_objects": case "spatial_candidates":
                    return "Mirando tu habitación…";
                case "layout_plan": return "Pensando cómo colocarlo…";
                case "create_gameobject": case "create_primitive": case "create_panel": case "update_panel":
                case "scene_build": case "scene_edit":
                case "ui_present": case "ui_update": case "layout_create": case "layout_apply":
                case "set_transform": case "set_parent": case "set_appearance": case "place_relative":
                case "spatial_place": case "rename_object": case "ui_open": case "ui_close":
                    return "Colocando el contenido…";
                case "delete_object": case "ui_remove": case "restore_object": case "undo":
                    return "Aplicando los cambios…";
                case "content_save": return "Guardando…";
                case "content_load": return "Cargando…";
                case "websearch": case "webfetch": return "Buscando información…";
                case "request_begin": case "request_end": return null;
                default: return Working;
            }
        }

        public static string Line(string text, int max = 80)
        {
            var line = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            if (max <= 0) return "";
            return line.Length <= max ? line : line.Substring(0, max - 1).TrimEnd() + "…";
        }
    }
}
