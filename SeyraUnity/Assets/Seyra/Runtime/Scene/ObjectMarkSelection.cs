using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seyra.SceneTools
{
    [DisallowMultipleComponent]
    public sealed class ObjectMarkSelection : MonoBehaviour
    {
        public sealed class Entry
        {
            public string Label, Context;
            public ObjectMarkTarget LabelTarget, BoxTarget;
        }
        static ObjectMarkSelection current;
        static int currentIndex = -1;
        readonly List<Entry> entries = new List<Entry>();
        readonly List<Action> handlers = new List<Action>();
        public static event Action CurrentChanged;
        public static string CurrentLabel => current == null ? null : current.entries[currentIndex].Label;
        public static string CurrentContext => current == null ? null : current.entries[currentIndex].Context;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { current = null; currentIndex = -1; CurrentChanged = null; }
        public void Bind(IReadOnlyList<Entry> values)
        {
            Unbind();
            for (int i = 0; i < values.Count; i++)
            {
                int index = i;
                var entry = values[i];
                Action handler = () => Toggle(index);
                entries.Add(entry); handlers.Add(handler);
                entry.LabelTarget.Tapped += handler;
                if (entry.BoxTarget != null)
                {
                    entry.BoxTarget.Tapped += handler;
                    entry.BoxTarget.SetPickable(true);
                }
            }
            LateUpdate();
        }
        public void Toggle(int index)
        {
            if (!isActiveAndEnabled || index < 0 || index >= entries.Count) return;
            bool clear = current == this && currentIndex == index;
            if (current != null) current.Highlight(currentIndex, false);
            current = clear ? null : this; currentIndex = clear ? -1 : index;
            if (!clear) Highlight(index, true);
            CurrentChanged?.Invoke();
        }
        void Highlight(int index, bool value)
        {
            var entry = entries[index];
            if (entry.LabelTarget != null) entry.LabelTarget.SetSelected(value);
            if (entry.BoxTarget != null) entry.BoxTarget.SetSelected(value);
        }
        public static void ClearCurrent()
        {
            if (current == null) return;
            current.Highlight(currentIndex, false);
            current = null; currentIndex = -1;
            CurrentChanged?.Invoke();
        }
        void LateUpdate()
        {
            foreach (var entry in entries)
                if (entry.LabelTarget != null) entry.LabelTarget.SetPickable(entry.BoxTarget == null || !entry.BoxTarget.gameObject.activeInHierarchy);
        }
        void Unbind()
        {
            if (current == this) ClearCurrent();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].LabelTarget != null) entries[i].LabelTarget.Tapped -= handlers[i];
                if (entries[i].BoxTarget != null) entries[i].BoxTarget.Tapped -= handlers[i];
                Highlight(i, false);
                if (entries[i].LabelTarget != null) entries[i].LabelTarget.SetPickable(false);
                if (entries[i].BoxTarget != null) entries[i].BoxTarget.SetPickable(false);
            }
            entries.Clear(); handlers.Clear();
        }
        void OnDisable() { if (current == this) ClearCurrent(); }
        void OnDestroy() => Unbind();
    }
}
