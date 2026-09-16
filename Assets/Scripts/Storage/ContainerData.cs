using UnityEngine;

namespace StorageLord.Storage
{
    /// <summary>
    /// Defines a placeable storage container. Capacity and per-instance type-lock landed with #5 —
    /// accepted-goods-*category* filtering (multiple types sharing one filter) stays Phase 2; this
    /// is a per-instance identity lock only, resolving the GDD's container type-matching question.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Container Data", fileName = "ContainerData")]
    public class ContainerData : ScriptableObject
    {
        public string displayName;
        public GameObject prefab;

        [Tooltip("How many goods this container can hold once locked to a type. Placeholder value, " +
                 "not tuned — same treatment as cellSize/cellHeight.")]
        [Min(1)]
        public int capacity = 5;
    }
}
