using System;
using UnityEngine;

namespace StorageLord.Docks
{
    /// <summary>
    /// ScriptableObject event channel raised as orders activate, get fulfilled, or expire. Per
    /// CLAUDE.md's Event-Driven Communication convention — no listener exists yet, but a future
    /// ScoreManager is the intended subscriber to OnOrderFulfilled/OnOrderMissed.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Order Event Channel", fileName = "OrderEventChannel")]
    public class OrderEventChannel : ScriptableObject
    {
        /// <summary>Raised the moment a scheduled order activates.</summary>
        public event Action<OrderData> OnOrderActivated;

        /// <summary>Raised the moment an active order's required quantity is fully delivered.</summary>
        public event Action<OrderData> OnOrderFulfilled;

        /// <summary>Raised the moment an active order's deadline passes unfulfilled.</summary>
        public event Action<OrderData> OnOrderMissed;

        /// <summary>Raises <see cref="OnOrderActivated"/> for the given order.</summary>
        public void RaiseOrderActivated(OrderData order) => OnOrderActivated?.Invoke(order);

        /// <summary>Raises <see cref="OnOrderFulfilled"/> for the given order.</summary>
        public void RaiseOrderFulfilled(OrderData order) => OnOrderFulfilled?.Invoke(order);

        /// <summary>Raises <see cref="OnOrderMissed"/> for the given order.</summary>
        public void RaiseOrderMissed(OrderData order) => OnOrderMissed?.Invoke(order);
    }
}
