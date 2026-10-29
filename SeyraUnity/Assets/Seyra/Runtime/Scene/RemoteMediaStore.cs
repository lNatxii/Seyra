using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Seyra.UI.Presentations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Seyra.SceneTools
{
    // Separate scene resource; never part of the persisted Generated hierarchy.
    [DisallowMultipleComponent]
    public sealed class RemoteMediaStore : MonoBehaviour, IUIMediaResolver
    {
        const int MaxBytes = 1024 * 1024;
        const int TextureLimit = 48;
        static readonly Regex ValidId = new Regex(@"\Aweb:[0-9a-f]{32}\z", RegexOptions.CultureInvariant);
        static readonly Dictionary<Transform, RemoteMediaStore> stores = new Dictionary<Transform, RemoteMediaStore>();
        [SerializeField] Transform generatedRoot;
        int owners;
        readonly Dictionary<string, Sprite> loaded = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        readonly LinkedList<string> recency = new LinkedList<string>();
        string directory;

        public static RemoteMediaStore For(Transform generatedRoot)
        {
            if (generatedRoot == null) return null;
            if (stores.TryGetValue(generatedRoot, out var store) && store != null) return store;
            // Recover scene-owned stores after an Editor domain reload.
            store = Object.FindObjectsByType<RemoteMediaStore>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.generatedRoot == generatedRoot);
            if (store != null) stores[generatedRoot] = store;
            return store;
        }

        internal static RemoteMediaStore Acquire(Transform generatedRoot, string persistenceDirectory)
        {
            var store = For(generatedRoot);
            if (store == null)
            {
                var go = new GameObject("SeyraRemoteMedia");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, generatedRoot.gameObject.scene);
                store = go.AddComponent<RemoteMediaStore>();
                store.generatedRoot = generatedRoot;
                stores[generatedRoot] = store;
            }
            store.Configure(persistenceDirectory); store.owners++;
            return store;
        }

        internal void ReleaseOwner()
        {
            if (--owners <= 0) DestroyStore();
        }
        internal void DestroyStore()
        {
            if (!ReferenceEquals(generatedRoot, null) && stores.TryGetValue(generatedRoot, out var store) && store == this) stores.Remove(generatedRoot);
            // Do not rediscover an object awaiting Unity's deferred destruction.
            generatedRoot = null;
            ClearResources();
            DestroyResource(gameObject);
        }
        void LateUpdate()
        {
            if (generatedRoot == null) DestroyStore();
        }

        internal void Configure(string persistenceDirectory)
        {
            var next = Path.Combine(persistenceDirectory, "Media");
            if (directory != null && directory != next) throw new InvalidOperationException("The media store already uses another persistence directory.");
            directory = next;
        }

        public void Register(JArray media)
        {
            if (media == null || media.Count > 12) throw new ArgumentException("media must contain at most 12 entries.");
            var prepared = new List<(string id, byte[] bytes, Sprite sprite)>();
            try
            {
                foreach (var token in media)
                {
                    var entry = token as JObject ?? throw new ArgumentException("Invalid media entry.");
                    PresentationContent.RequireKeys(entry, "id", "mime", "width", "height", "data");
                    if (entry["id"].Type != JTokenType.String || !ValidId.IsMatch((string)entry["id"]) ||
                        entry["mime"].Type != JTokenType.String || (string)entry["mime"] != "image/jpeg" ||
                        entry["width"].Type != JTokenType.Integer || entry["height"].Type != JTokenType.Integer ||
                        (long)entry["width"] < 1 || (long)entry["width"] > 512 || (long)entry["height"] < 1 || (long)entry["height"] > 512 ||
                        entry["data"].Type != JTokenType.String) throw new ArgumentException("Invalid media fields.");
                    string id = (string)entry["id"], data = (string)entry["data"];
                    if (data.Length > 4 * ((MaxBytes + 2) / 3)) throw new ArgumentException("Media exceeds 1 MiB.");
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(data); }
                    catch (FormatException error) { throw new ArgumentException("Invalid media base64.", error); }
                    ValidateBytes(id, bytes);
                    var sprite = Decode(bytes);
                    if (sprite.texture.width != (int)entry["width"] || sprite.texture.height != (int)entry["height"])
                    { Release(sprite); throw new ArgumentException("Media dimensions do not match the JPEG."); }
                    prepared.Add((id, bytes, sprite));
                }
                // Every entry is validated before writing files or replacing cached resources.
                Directory.CreateDirectory(directory);
                foreach (var item in prepared)
                {
                    var path = FilePath(item.id);
                    if (!File.Exists(path))
                    {
                        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            File.WriteAllBytes(temporary, item.bytes);
                            try { File.Move(temporary, path); }
                            catch (IOException) when (File.Exists(path)) { }
                        }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                }
                foreach (var item in prepared)
                {
                    if (!loaded.ContainsKey(item.id)) { loaded.Add(item.id, item.sprite); recency.AddLast(item.id); }
                    else Release(item.sprite);
                }
                prepared.Clear();
                Trim();
            }
            finally { foreach (var item in prepared) Release(item.sprite); }
        }

        public bool TryResolve(string reference, out Sprite sprite)
        {
            sprite = null;
            if (reference == null || !ValidId.IsMatch(reference) || directory == null) return false;
            if (!loaded.TryGetValue(reference, out sprite))
            {
                try
                {
                    var path = FilePath(reference);
                    if (!File.Exists(path) || new FileInfo(path).Length > MaxBytes) return false;
                    var bytes = File.ReadAllBytes(path); ValidateBytes(reference, bytes);
                    sprite = Decode(bytes); loaded.Add(reference, sprite);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
                { sprite = null; return false; }
            }
            recency.Remove(reference); recency.AddLast(reference);
            Trim(reference);
            return true;
        }

        string FilePath(string id) => Path.Combine(directory, id.Substring(4) + ".jpg");

        static void ValidateBytes(string id, byte[] bytes)
        {
            if (bytes.Length < 4 || bytes.Length > MaxBytes || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[2] != 0xff)
                throw new ArgumentException("Media must be a JPEG of at most 1 MiB.");
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            if (!hash.StartsWith(id.Substring(4), StringComparison.Ordinal)) throw new ArgumentException("Media hash does not match its ID.");
            // Read SOF dimensions before allocating a decoder texture, including files loaded from disk.
            for (int offset = 2; offset + 3 < bytes.Length;)
            {
                if (bytes[offset++] != 0xff) break;
                while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
                if (offset >= bytes.Length) break;
                int marker = bytes[offset++];
                if (marker == 0xda || marker == 0xd9) break;
                if (marker == 0x01 || marker >= 0xd0 && marker <= 0xd7) continue;
                if (offset + 1 >= bytes.Length) break;
                int length = bytes[offset] * 256 + bytes[offset + 1];
                if (length < 2 || offset + length > bytes.Length) break;
                if (marker >= 0xc0 && marker <= 0xcf && marker != 0xc4 && marker != 0xc8 && marker != 0xcc)
                {
                    if (length < 8) break;
                    int height = bytes[offset + 3] * 256 + bytes[offset + 4], width = bytes[offset + 5] * 256 + bytes[offset + 6];
                    if (width < 1 || height < 1 || width > 512 || height > 512) break;
                    return;
                }
                offset += length;
            }
            throw new ArgumentException("Invalid JPEG or dimensions outside 1..512 pixels.");
        }

        static Sprite Decode(byte[] bytes)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
            try
            {
                if (!texture.LoadImage(bytes, markNonReadable: true)) throw new ArgumentException("JPEG decoding failed.");
                texture.wrapMode = TextureWrapMode.Clamp;
                return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
            }
            catch { DestroyResource(texture); throw; }
        }

        void Trim(string requested = null)
        {
            if (loaded.Count <= TextureLimit) return;
            var used = new HashSet<string>(generatedRoot.GetComponentsInChildren<ManagedPanel>(true).SelectMany(panel => panel.MediaReferences()), StringComparer.Ordinal);
            if (requested != null) used.Add(requested);
            var current = recency.First;
            while (loaded.Count > TextureLimit && current != null)
            {
                var next = current.Next;
                if (!used.Contains(current.Value))
                {
                    Release(loaded[current.Value]); loaded.Remove(current.Value); recency.Remove(current);
                }
                current = next;
            }
            if (loaded.Count > TextureLimit) Debug.LogWarning("Seyra: remote media exceeds 48 textures because all remaining images are in use.");
        }

        static void DestroyResource(Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Object.Destroy(resource); else Object.DestroyImmediate(resource);
        }
        static void Release(Sprite sprite)
        {
            if (sprite == null) return;
            var texture = sprite.texture; DestroyResource(sprite); DestroyResource(texture);
        }
        void OnDestroy()
        {
            if (!ReferenceEquals(generatedRoot, null) && stores.TryGetValue(generatedRoot, out var store) && store == this) stores.Remove(generatedRoot);
            ClearResources();
        }
        void ClearResources()
        {
            foreach (var sprite in loaded.Values) Release(sprite);
            loaded.Clear(); recency.Clear();
        }
    }

    internal sealed class PresentationMediaResolver : IUIMediaResolver
    {
        readonly RemoteMediaStore remote;
        readonly UIPresentationCatalog catalog;
        internal PresentationMediaResolver(RemoteMediaStore remote, UIPresentationCatalog catalog) { this.remote = remote; this.catalog = catalog; }
        public bool TryResolve(string reference, out Sprite sprite)
        {
            sprite = null;
            if (reference != null && reference.StartsWith("web:", StringComparison.Ordinal)) return remote != null && remote.TryResolve(reference, out sprite);
            return catalog != null && catalog.TryResolve(reference, out sprite);
        }
    }
}
