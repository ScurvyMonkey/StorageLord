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

        [Tooltip("Real seconds after game start (or after the tutorial gate opens, #52) before this order " +
                 "activates. 300 s = 1 game day (#53).")]
        [Min(0f)]
        public float startDelaySeconds;

        [Tooltip("Real seconds from this order's own activation until it expires unfulfilled. 300 s = 1 game " +
                 "day, 12.5 s = 1 game hour (#53). Ignored when isUntimed or hasNoDeadline.")]
        [Min(1f)]
        public float deadlineSeconds = 60f;

        [Tooltip("Untimed stockpile order (#49): never expires. It must first be fully stockpiled in " +
                 "storage (stockpiling), and only then ships through the belts like a normal order (shipping).")]
        public bool isUntimed;

        [Tooltip("No deadline, but delivered directly like a normal order (#52) — e.g. the tutorial's " +
                 "Initiation Project. Unlike isUntimed, there is no stockpile phase. Never counts as missed.")]
        public bool hasNoDeadline;

        [Tooltip("Money awarded on fulfillment — only meaningful when tier == Random (#25); " +
                 "Priority orders never grant money.")]
        [Min(0)]
        public int moneyReward;
    }
}
