namespace StorageLord.Docks
{
    /// <summary>
    /// Runtime state for one currently active order — how much has been delivered and how much
    /// time remains before its deadline. Owned and mutated only by ShippingManager; exposed
    /// read-only elsewhere (e.g. ShippingHUD) the same way ContainerInstance exposes its state.
    /// </summary>
    public class ActiveOrder
    {
        public OrderData Data { get; }
        public int Delivered { get; private set; }
        public float RemainingDeadlineSeconds { get; private set; }

        public bool IsFulfilled => Delivered >= Data.requiredQuantity;
        public bool IsExpired => RemainingDeadlineSeconds <= 0f;

        public ActiveOrder(OrderData data)
        {
            Data = data;
            RemainingDeadlineSeconds = data.deadlineSeconds;
        }

        /// <summary>Credits one delivered unit toward this order's required quantity.</summary>
        public void Deliver()
        {
            Delivered++;
        }

        /// <summary>Advances this order's countdown to its deadline by the given time.</summary>
        public void Tick(float deltaTime)
        {
            RemainingDeadlineSeconds -= deltaTime;
        }
    }
}
