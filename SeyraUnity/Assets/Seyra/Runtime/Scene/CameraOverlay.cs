using UnityEngine;

namespace Seyra.SceneTools
{
    [DisallowMultipleComponent]
    public sealed class CameraOverlay : MonoBehaviour
    {
        ManagedPanel card;
        Mesh mesh;
        Texture2D texture;
        Material material;
        MeshRenderer surface;
        float fade;
        bool retiring;

        public static CameraOverlay Show(Transform root, string id, ManagedPanel card, Mesh mesh, Texture2D texture, Shader shader)
        {
            CameraOverlay existing = null;
            var entries = new System.Collections.Generic.List<CameraOverlay>(root.GetComponentsInChildren<CameraOverlay>(true));
            entries.RemoveAll(entry => entry.retiring);
            foreach (var entry in entries) if (entry.name == id) existing = entry;
            if (existing == null)
            {
                if (entries.Count >= 4)
                {
                    entries[0].retiring = true;
                    entries[0].gameObject.SetActive(false);
                    Destroy(entries[0].gameObject);
                }
                var item = new GameObject(id);
                item.transform.SetParent(root, false);
                existing = item.AddComponent<CameraOverlay>();
            }
            existing.Bind(card, mesh, texture, shader);
            return existing;
        }

        public void Bind(ManagedPanel card, Mesh mesh, Texture2D texture, Shader shader)
        {
            Release();
            this.card = card; this.mesh = mesh; this.texture = texture;
            material = new Material(shader);
            material.mainTexture = texture;
            material.SetFloat("_Fade", 0);
            material.SetFloat("_Exposure", 1);
            var filter = GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            surface = GetComponent<MeshRenderer>();
            if (surface == null) surface = gameObject.AddComponent<MeshRenderer>();
            surface.sharedMaterial = material;
            surface.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            surface.receiveShadows = false;
            fade = 0;
        }

        void LateUpdate()
        {
            if (card == null) { Destroy(gameObject); return; }
            bool original = card.PresentationView != null && card.PresentationView.Values.TryGetValue("checked", out var value) && value == "true";
            bool visible = card.gameObject.activeInHierarchy && !original;
            fade = Mathf.MoveTowards(fade, visible ? 1 : 0, Time.unscaledDeltaTime / 0.25f);
            if (material != null) material.SetFloat("_Fade", fade);
            if (surface != null) surface.enabled = card.gameObject.activeInHierarchy && fade > 0;
        }

        void Release()
        {
            if (mesh != null) Destroy(mesh);
            if (texture != null) Destroy(texture);
            if (material != null) Destroy(material);
        }
        void OnDestroy() => Release();
    }
}
