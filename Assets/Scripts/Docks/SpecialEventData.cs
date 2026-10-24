using UnityEngine;
using StorageLord.Goods;

namespace StorageLord.Docks
{
    /// <summary>
    /// Tunes the rare, single Special-tier offer ShippingManager generates (#26) — a bundle of
    /// bundleSize untimed stockpile orders (#50, on #49's lifecycle) whose combined weight target is
    /// weightFractionTarget of the platform's base weight capacity (WeightManager.GetBaseCapacityKg(),
    /// no upgrade multiplier), each paying an even share of a lump money reward. Kept as its own asset, separate from
    /// RandomJobPoolData, since Special-tier tuning (rare, platform-scale, no pool/churn) is a
    /// genuinely different concern from Random's continuously-rotating pool, not a variant of it.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Special Event Data", fileName = "SpecialEventData")]
    public class SpecialEventData : ScriptableObject
    {
        [Tooltip("Goods types a bundle order can require — picked at random per order.")]
        public GoodsData[] goodsPool;

        [Tooltip("How many separate orders the weight target is split evenly across.")]
        [Min(1)]
        public int bundleSize = 3;

        [Tooltip("Fraction of the platform's base weight capacity (no upgrade multiplier) the " +
                 "bundle's combined requirement targets (e.g. 0.90 = 90%).")]
        [Range(0.01f, 1f)]
        public float weightFractionTarget = 0.90f;

        [Tooltip("OBSOLETE since #50: Special bundles are untimed (stockpile, then ship) and never " +
                 "expire. Kept only so existing assets still load; not read anywhere.")]
        [Min(1f)]
        public float deadlineSeconds = 300f;

        [Header("Money Reward (total, split evenly across the bundle)")]
        [Min(0)]
        public int moneyRewardMin = 200;
        [Min(0)]
        public int moneyRewardMax = 500;

        [Tooltip("OBSOLETE since #53: replaced by the game-time field below. Kept so existing assets load; not read.")]
        [Min(0f)]
        public float spawnIntervalSeconds = 300f;

        [Tooltip("Cooldown after one Special event fully resolves before the next offer can appear, " +
                 "in game days (#53).")]
        [Min(0f)]
        public float spawnIntervalGameDays = 1f;
    }
}
