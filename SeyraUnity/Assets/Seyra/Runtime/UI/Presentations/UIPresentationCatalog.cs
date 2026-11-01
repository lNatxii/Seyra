using System;
using System.Collections.Generic;
using System.Linq;
using Seyra.UI.Library;
using UnityEngine;

namespace Seyra.UI.Presentations
{
    [Serializable] public sealed class UIPresentationTemplate
    {
        public string type;
        public UIPresentationView prefab;
    }
    [Serializable] public sealed class UIMediaEntry
    {
        public string id, description;
        public Sprite sprite;
    }
    public interface IUIMediaResolver
    {
        bool TryResolve(string reference, out Sprite sprite);
        bool IsPending(string reference);
    }

    [CreateAssetMenu(menuName = "Seyra/UI/Presentation catalog")]
    public sealed class UIPresentationCatalog : ScriptableObject, IUIMediaResolver
    {
        [SerializeField] UIPrefabCatalog components;
        [SerializeField] List<UIPresentationTemplate> templates = new List<UIPresentationTemplate>();
        [SerializeField] List<UIMediaEntry> media = new List<UIMediaEntry>();
        public UIPrefabCatalog Components => components;
        public IReadOnlyList<UIPresentationTemplate> Templates => templates;
        public IReadOnlyList<UIMediaEntry> Media => media;
        public UIPresentationView Get(string type) => templates.FirstOrDefault(t => t.type == type)?.prefab ?? throw new ArgumentException("Missing presentation prefab: " + type);
        public void Configure(UIPrefabCatalog basic, IEnumerable<UIPresentationTemplate> entries)
        {
            var next = entries.ToList();
            if (basic == null || next.Count != UIPresentationCodec.Types.Count || next.Select(t => t.type).Distinct().Count() != next.Count ||
                next.Any(t => !UIPresentationCodec.Types.ContainsKey(t.type) || t.prefab == null || t.prefab.Kind != t.type)) throw new ArgumentException("Presentation catalog is incomplete or invalid.");
            components = basic; templates = next;
        }
        public bool TryResolve(string reference, out Sprite sprite)
        {
            // Explicit asset registry: the AI cannot read arbitrary files or initiate a download.
            sprite = media.FirstOrDefault(m => m.id == reference)?.sprite;
            return sprite != null;
        }
        public bool IsPending(string reference) => false;
        public void RegisterMedia(string id, Sprite sprite, string description = "")
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || sprite == null) throw new ArgumentException("Invalid image resource.");
            var entry = media.FirstOrDefault(m => m.id == id);
            if (entry == null) { entry = new UIMediaEntry { id = id }; media.Add(entry); }
            entry.sprite = sprite; entry.description = description;
        }
    }
}
