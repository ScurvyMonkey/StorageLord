using UnityEngine;

namespace StorageLord.Docks
{
    /// <summary>
    /// Tunable definition of the extra Receiving buildings a player can buy and place (#51): the
    /// compact one-cell prefab, its rising price, and an optional spawn-rate override. Same minimal
    /// ScriptableObject-first pattern as ReceivingData.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Receiving Building Data", fileName = "ReceivingBuildingData")]
    public class ReceivingBuildingData : ScriptableObject
    {
        [Tooltip("Compact one-cell Receiving building. Its root must carry a ReceivingDock, and its " +
                 "output side is the root's local forward (+Z); it must fit inside one grid cell.")]
        public GameObject prefab;

        [Tooltip("Price of the first extra building. The fixed starting dock is free and doesn't count.")]
        [Min(0)]
        public int baseCost = 150;

        [Tooltip("Each further building costs this many times the previous one: cost(n) = baseCost × costGrowth^(n−1).")]
        [Min(1f)]
        public float costGrowth = 1.5f;

        [Tooltip("Goods per minute for a placed building. 0 uses ReceivingData.itemsPerMinute, the fixed dock's rate.")]
        [Min(0f)]
        public float itemsPerMinute;

        /// <summary>
        /// Price of the next building when <paramref name="placedCount"/> extra buildings already
        /// exist — baseCost × costGrowth^placedCount, rounded to whole money.
        /// </summary>
        public int CostForNext(int placedCount)
        {
            return Mathf.RoundToInt(baseCost * Mathf.Pow(costGrowth, placedCount));
        }
    }
}
