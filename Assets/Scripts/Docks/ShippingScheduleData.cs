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
    }
}
