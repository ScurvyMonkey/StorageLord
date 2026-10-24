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
        [Tooltip("OBSOLETE since #53: replaced by the game-time field below. Kept so existing assets load; not read.")]
        [Min(1f)]
        public float baseDeadlineSeconds = 60f;
        [Tooltip("OBSOLETE since #53: replaced by the game-time field below. Kept so existing assets load; not read.")]
        [Min(0f)]
        public float deadlineReductionPerWave = 2f;
        [Tooltip("OBSOLETE since #53: replaced by the game-time field below. Kept so existing assets load; not read.")]
        [Min(1f)]
        public float minDeadlineSeconds = 15f;

        [Header("Deadline in game time (#53)")]
        [Tooltip("First wave's deadline, in game hours (1 game day = GameClockData.realSecondsPerGameDay).")]
        [Min(0.1f)]
        public float baseDeadlineGameHours = 12f;
        [Tooltip("Game hours taken off the deadline every wave.")]
        [Min(0f)]
        public float deadlineReductionGameHoursPerWave = 0.5f;
        [Tooltip("The deadline never drops below this, in game hours.")]
        [Min(0.1f)]
        public float minDeadlineGameHours = 4f;

        [Header("Quantity")]
        [Min(1)]
        public int baseQuantity = 3;
        [Min(0)]
        public int quantityGrowthPerWave = 1;

        [Header("Spawn Interval")]
        [Tooltip("OBSOLETE since #53: replaced by the game-time field below. Kept so existing assets load; not read.")]
        [Min(0.1f)]
        public float baseSpawnIntervalSeconds = 20f;
        [Tooltip("OBSOLETE since #53: replaced by the game-time field below. Kept so existing assets load; not read.")]
        [Min(0f)]
        public float spawnIntervalReductionPerWave = 0.5f;
        [Tooltip("OBSOLETE since #53: replaced by the game-time field below. Kept so existing assets load; not read.")]
        [Min(0.1f)]
        public float minSpawnIntervalSeconds = 5f;

        [Header("Spawn interval in game time (#53)")]
        [Tooltip("Gap before the next wave, in game hours. Keep the deadline about 3x this so only 2-4 " +
                 "Company Orders are active at once.")]
        [Min(0.01f)]
        public float baseSpawnIntervalGameHours = 4f;
        [Tooltip("Game hours taken off the spawn interval every wave.")]
        [Min(0f)]
        public float spawnIntervalReductionGameHoursPerWave = 0.15f;
        [Tooltip("The spawn interval never drops below this, in game hours.")]
        [Min(0.01f)]
        public float minSpawnIntervalGameHours = 1.33f;
    }
}
