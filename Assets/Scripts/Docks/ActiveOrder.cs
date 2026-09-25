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
        public int Reserved { get; private set; }
        public float RemainingDeadlineSeconds { get; private set; }

        public bool IsFulfilled => Delivered >= Data.requiredQuantity;
        public bool IsExpired => RemainingDeadlineSeconds <= 0f;

        public ActiveOrder(OrderData data)
        {
            Data = data;
            RemainingDeadlineSeconds = data.deadlineSeconds;
        }

        /// <summary>Credits one delivered unit toward this order's required quantity. If a unit
        /// was reserved (#22 — dispatched from storage, still in transit), releases one
        /// reservation too, since an arrival closes out an in-flight unit whether or not this
        /// exact arrival is the one that was originally reserved for it — a deliberate, safe
        /// approximation: worst case this lets one extra unit dispatch later than strictly
        /// necessary, never fewer, and never a crash or stuck state.</summary>
        public void Deliver()
        {
            Delivered++;
            if (Reserved > 0)
            {
                Reserved--;
            }
        }

        /// <summary>Marks one more unit as claimed from storage and in transit (#22) — reserved
        /// units count toward this order's remaining need the same as delivered ones, so the
        /// automatic-dispatch loop never sends more units toward an order than it actually still
        /// needs, even though dispatched units no longer credit Delivered until they physically
        /// arrive.</summary>
        public void Reserve()
        {
            Reserved++;
        }

        /// <summary>Advances this order's countdown to its deadline by the given time.</summary>
        public void Tick(float deltaTime)
        {
            RemainingDeadlineSeconds -= deltaTime;
        }
    }
}
