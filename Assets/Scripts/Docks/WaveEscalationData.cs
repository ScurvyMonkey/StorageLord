using UnityEngine;
using StorageLord.Goods;

namespace StorageLord.Docks
{
    /// <summary>
    /// Drives the Company's orders once the hand-authored ShippingScheduleData list is exhausted
    /// (#13) — each generated order draws a random GoodsData from goodsPool, with quantity rising
    /// and deadline/spawn-interval shrinking every wave, each floored so the run never becomes
    /// literally impossible. Single-goods-type per generated order, same as authored OrderData —
    /// this stays Phase 1 as long as that holds (multi-good orders are real Phase 2 "order
    /// complexity").
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Wave Escalation Data", fileName = "WaveEscalationData")]
    public class WaveEscalationData : ScriptableObject
    {
        [Tooltip("Goods types a generated order can require — picked at random per order.")]
        public GoodsData[] goodsPool;

        [Header("Deadline")]
        [Min(1f)]
        public float baseDeadlineSeconds = 60f;
        [Min(0f)]
        public float deadlineReductionPerWave = 2f;
        [Min(1f)]
        public float minDeadlineSeconds = 15f;

        [Header("Quantity")]
        [Min(1)]
        public int baseQuantity = 3;
        [Min(0)]
        public int quantityGrowthPerWave = 1;

        [Header("Spawn Interval")]
        [Min(0.1f)]
        public float baseSpawnIntervalSeconds = 20f;
        [Min(0f)]
        public float spawnIntervalReductionPerWave = 0.5f;
        [Min(0.1f)]
        public float minSpawnIntervalSeconds = 5f;
    }
}
