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

        /// <summary>True once an untimed order's full quantity has been stockpiled and it has started shipping (#49). Always false for timed orders.</summary>
        public bool IsShipping { get; private set; }

        /// <summary>Units of this order's goods type currently stored across all containers, last refreshed by ShippingManager while stockpiling (#49).</summary>
        public int StockpileStored { get; private set; }

        public bool IsFulfilled => Delivered >= Data.requiredQuantity;

        /// <summary>
        /// True for an untimed order that is still collecting its stockpile (#49). It must not be
        /// dispatched or credited until it switches to shipping.
        /// </summary>
        public bool IsStockpiling => Data.isUntimed && !IsShipping;

        /// <summary>
        /// True if this order has a deadline counting down (#52). False for untimed stockpile orders
        /// (#49) and no-deadline orders such as the tutorial. Every "show or tick a countdown?"
        /// decision reads this, never isUntimed directly, since isUntimed also means "stockpile first".
        /// </summary>
        public bool HasDeadline => !Data.isUntimed && !Data.hasNoDeadline;

        /// <summary>Orders without a deadline never expire (#49/#52). Without this, their unused deadline would expire them on the first tick.</summary>
        public bool IsExpired => HasDeadline && RemainingDeadlineSeconds <= 0f;

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
            if (HasDeadline)
            {
                RemainingDeadlineSeconds -= deltaTime;
            }
        }

        /// <summary>Records how many units of this order's goods are currently stored (#49), for the stockpile threshold and the Orders panel.</summary>
        public void SetStockpileStored(int stored)
        {
            StockpileStored = stored;
        }

        /// <summary>Switches an untimed order from stockpiling to shipping (#49). One-way: a shipping order stays committed even if stock later drops.</summary>
        public void BeginShipping()
        {
            IsShipping = true;
        }
    }
}
