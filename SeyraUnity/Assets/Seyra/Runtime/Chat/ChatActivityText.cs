using System;
using System.Text.RegularExpressions;

namespace Seyra.Chat
{
    public static class ChatActivityText
    {
        public const string Thinking = "Thinking…";
        public const string Working = "Working…";

        // A null description preserves the previous activity for request boundaries.
        public static string Describe(string toolName)
        {
            var name = toolName ?? "";
            if (name.StartsWith("seyra_", StringComparison.Ordinal)) name = name.Substring(6);
            switch (name)
            {
                case "scene_hierarchy": case "scene_find": case "scene_get": case "scene_bounds":
                case "request_list": case "history_list": case "content_list": case "ui_catalog":
                case "ui_media_list": case "ui_list": case "ui_get": case "card_checks":
                    return "Checking your surroundings…";
                case "room_get": case "room_objects": case "spatial_candidates":
                    return "Looking at your room…";
                case "layout_plan": return "Planning the placement…";
                case "create_gameobject": case "create_primitive": case "create_panel": case "update_panel":
                case "create_point": case "create_line": case "update_line":
                case "motion_set": case "motion_remove": case "motion_control":
                case "scene_build": case "scene_edit":
                case "ui_present": case "ui_update": case "layout_create": case "layout_apply":
                case "set_transform": case "set_parent": case "set_appearance": case "place_relative":
                case "spatial_place": case "rename_object": case "ui_open": case "ui_close":
                    return "Placing content…";
                case "delete_object": case "ui_remove": case "restore_object": case "undo":
                    return "Applying changes…";
                case "content_save": return "Saving…";
                case "content_load": return "Loading…";
                case "websearch": case "webfetch": return "Looking up information…";
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
