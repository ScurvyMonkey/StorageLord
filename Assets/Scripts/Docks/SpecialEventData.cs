using UnityEngine;
using StorageLord.Goods;

namespace StorageLord.Docks
{
    /// <summary>
    /// Tunes the rare, single Special-tier offer ShippingManager generates (#26) — a bundle of
    /// bundleSize ordinary orders whose combined weight target is weightFractionTarget of the
    /// platform's current total weight capacity (WeightManager.GetTotalCapacityKg()), each paying
    /// an even share of a lump money reward. Kept as its own asset, separate from
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

        [Tooltip("Fraction of the platform's current total weight capacity the bundle's combined " +
                 "requirement targets (e.g. 0.99 = 99%).")]
        [Range(0.01f, 1f)]
        public float weightFractionTarget = 0.99f;

        [Tooltip("Deadline shared by every order in the bundle, once accepted. Expected to be long " +
                 "(minutes, not seconds) to match the scale of the project.")]
        [Min(1f)]
        public float deadlineSeconds = 300f;

        [Header("Money Reward (total, split evenly across the bundle)")]
        [Min(0)]
        public int moneyRewardMin = 200;
        [Min(0)]
        public int moneyRewardMax = 500;

        [Tooltip("Cooldown after one Special event fully resolves (bundle completed or any member " +
                 "expired) before the next offer can appear.")]
        [Min(0f)]
        public float spawnIntervalSeconds = 300f;
    }
}
