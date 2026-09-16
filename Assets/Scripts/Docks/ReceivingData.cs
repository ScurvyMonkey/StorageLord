using UnityEngine;
using StorageLord.Goods;

namespace StorageLord.Docks
{
    /// <summary>
    /// Tunable definition of what a Receiving dock spawns and how fast. Same minimal
    /// ScriptableObject-first pattern as ContainerData/GoodsData/ConveyorData.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Receiving Data", fileName = "ReceivingData")]
    public class ReceivingData : ScriptableObject
    {
        public GoodsData goodsData;

        [Tooltip("How many goods a dock spawns per minute. Fixed for Phase 1 — ramping the rate " +
                 "over a session is a deliberately deferred GDD open question, not built here.")]
        [Min(0.01f)]
        public float itemsPerMinute = 30f;
    }
}
