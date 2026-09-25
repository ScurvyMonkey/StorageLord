using UnityEngine;
using StorageLord.Goods;

namespace StorageLord.Docks
{
    /// <summary>
    /// Tunes the rotating pool of optional Random-tier job offers ShippingManager generates and
    /// lets the player pick from (#25) — mirrors WaveEscalationData's shape (a goods pool plus
    /// min/max ranges), but kept as its own asset since Random-tier tuning is a genuinely separate
    /// concern from Priority's escalating-wave tuning, not a variant of it.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Random Job Pool Data", fileName = "RandomJobPoolData")]
    public class RandomJobPoolData : ScriptableObject
    {
        [Tooltip("Goods types a generated offer can require — picked at random per offer.")]
        public GoodsData[] goodsPool;

        [Tooltip("How many candidate offers are visible/pickable at once.")]
        [Min(1)]
        public int poolSize = 3;

        [Tooltip("How long an unpicked offer sits in the pool before expiring and being replaced.")]
        [Min(1f)]
        public float offerLifetimeSeconds = 45f;

        [Header("Quantity")]
        [Min(1)]
        public int quantityMin = 1;
        [Min(1)]
        public int quantityMax = 10;

        [Header("Deadline (once accepted)")]
        [Min(1f)]
        public float deadlineSecondsMin = 30f;
        [Min(1f)]
        public float deadlineSecondsMax = 90f;

        [Header("Money Reward")]
        [Min(0)]
        public int moneyRewardMin = 10;
        [Min(0)]
        public int moneyRewardMax = 50;
    }
}
