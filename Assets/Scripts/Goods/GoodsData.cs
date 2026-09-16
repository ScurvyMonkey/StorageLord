using UnityEngine;

namespace StorageLord.Goods
{
    /// <summary>
    /// Defines a type of good that can travel through the platform. Deliberately minimal, same
    /// pattern as ContainerData — no category/type-matching field yet, that's Phase 2 taxonomy
    /// territory. The per-instance type identity used by type-locked storage (a follow-up feature)
    /// will compare GoodsData references directly, not a category field.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Goods Data", fileName = "GoodsData")]
    public class GoodsData : ScriptableObject
    {
        public string displayName;
        public GameObject prefab;
    }
}
