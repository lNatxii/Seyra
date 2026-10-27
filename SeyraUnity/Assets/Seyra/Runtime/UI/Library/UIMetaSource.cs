using UnityEngine;
namespace Seyra.UI.Library
{
    /// <summary>Records the SDK source used by semantic adapters, without runtime path searches.</summary>
    public sealed class UIMetaSource : MonoBehaviour
    {
        [SerializeField] GameObject sourcePrefab;
        public GameObject SourcePrefab => sourcePrefab;
        public void Configure(GameObject source) => sourcePrefab = source;
    }
}
