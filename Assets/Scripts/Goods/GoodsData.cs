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

        [Tooltip("Weight of one unit, in kilograms — counts against whatever platform segment a " +
                 "container holding this good is sitting on (#14).")]
        [Min(0.01f)]
        public float weightKg = 1f;

        [Tooltip("Priority at conveyor merge points (#7, #21) when this good's feeder competes " +
                 "with a differently-typed feeder for the same junction cell — higher wins " +
                 "outright, even over the structural main-line rule. Ties (including the default, " +
                 "when nothing's been set) fall through unchanged to the existing " +
                 "main-line/fair-alternation arbitration.")]
        public int mergePriority;
    }
}
