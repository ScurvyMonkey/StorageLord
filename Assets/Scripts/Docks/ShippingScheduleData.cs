using UnityEngine;

namespace StorageLord.Docks
{
    /// <summary>
    /// The ordered list of every OrderData a session will offer. ShippingManager activates each
    /// one independently once its own startDelaySeconds elapses — this asset just defines the set,
    /// not the timing logic itself.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Shipping Schedule Data", fileName = "ShippingScheduleData")]
    public class ShippingScheduleData : ScriptableObject
    {
        public OrderData[] scheduledOrders;

        [Tooltip("Tutorial gate (#52): this order is active alone from run start, and every other order " +
                 "source (the rest of the schedule, job offers, Specials, waves) waits until it's " +
                 "delivered. Give it hasNoDeadline, not isUntimed (that would force a stockpile first). " +
                 "Leave empty for no gate.")]
        public OrderData tutorialGateOrder;
    }
}
