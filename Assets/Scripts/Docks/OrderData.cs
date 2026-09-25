using UnityEngine;
using StorageLord.Goods;

namespace StorageLord.Docks
{
    /// <summary>
    /// Defines one Company order: a single goods type and quantity to deliver, activating
    /// startDelaySeconds after game start and expiring deadlineSeconds after its own activation.
    /// Single-good only for Phase 1 — an order needing several goods types at once is Phase 2
    /// (see CLAUDE.md's Roadmap).
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Order Data", fileName = "OrderData")]
    public class OrderData : ScriptableObject
    {
        public string displayName;

        [Tooltip("Priority orders are the Company's endless demand loop and the only tier that " +
                 "counts toward the run's missed-order loss condition (#24). Random/Special are " +
                 "forward-declared for future issues — nothing generates an order of either tier yet.")]
        public OrderTier tier = OrderTier.Priority;

        public GoodsData requiredGoods;

        [Min(1)]
        public int requiredQuantity = 1;

        [Tooltip("Seconds after game start before this order activates.")]
        [Min(0f)]
        public float startDelaySeconds;

        [Tooltip("Seconds from this order's own activation until it expires unfulfilled.")]
        [Min(1f)]
        public float deadlineSeconds = 60f;

        [Tooltip("Money awarded on fulfillment — only meaningful when tier == Random (#25); " +
                 "Priority orders never grant money.")]
        [Min(0)]
        public int moneyReward;
    }
}
