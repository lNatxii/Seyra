using UnityEngine;

namespace Seyra.SceneTools
{
    // Identity/provenance of content owned by Seyra. Scene assets and external objects keep runtime IDs.
    [DisallowMultipleComponent]
    public sealed class GeneratedObject : MonoBehaviour
    {
        [SerializeField, HideInInspector] string entityId;
        [SerializeField, HideInInspector] string kind;
        [SerializeField, HideInInspector] string primitiveType;
        [SerializeField, HideInInspector] string requestGroupId;
        public string EntityId => entityId;
        public string Kind => kind;
        public string PrimitiveType => primitiveType;
        public string RequestGroupId => requestGroupId;
        [SerializeField, HideInInspector] internal Mesh OriginalMesh;
        [SerializeField, HideInInspector] internal Material OriginalMaterial;
        [SerializeField, HideInInspector] internal string OriginalComponents;
        [SerializeField, HideInInspector] internal Transform TrashRoot;

        internal void Initialize(string id,string objectKind,string shape,string group)
        {entityId=id;kind=objectKind;primitiveType=shape;requestGroupId=group;}
        void OnDestroy()
        {
            if (kind == "root") RemoteMediaStore.For(transform)?.DestroyStore();
        }
    }
}
