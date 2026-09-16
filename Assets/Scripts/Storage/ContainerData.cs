using UnityEngine;

namespace StorageLord.Storage
{
    /// <summary>
    /// Defines a placeable storage container. Deliberately minimal for now — capacity and
    /// accepted-goods filtering are Phase 2 per CLAUDE.md; this only needs to drive placement.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Container Data", fileName = "ContainerData")]
    public class ContainerData : ScriptableObject
    {
        public string displayName;
        public GameObject prefab;
    }
}
