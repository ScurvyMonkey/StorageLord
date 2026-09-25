using StorageLord.Docks;
using UnityEngine;

namespace StorageLord.Core
{
    /// <summary>
    /// Tracks the player's running money total (#25) — the slot CLAUDE.md's Proposed Manager
    /// Hierarchy has reserved since #9. Subscribes to OrderEventChannel.OnOrderFulfilled and awards
    /// OrderData.moneyReward only when the fulfilled order's tier is Random — Priority orders never
    /// grant money ("the Company takes care of your other needs").
    ///
    /// Created and wired by Bootstrapper — not placed directly in a scene, since it has no
    /// serialized Inspector fields to wire (its data reference is injected via Initialize()).
    /// </summary>
    public class ScoreManager : MonoBehaviour
    {
        private OrderEventChannel _eventChannel;

        /// <summary>The player's current running money total.</summary>
        public int Money { get; private set; }

        /// <summary>
        /// Injects this manager's event channel reference and subscribes to the fulfilled-order
        /// event. Called once by Bootstrapper immediately after creation — deliberately not relying
        /// on OnEnable() alone, since Bootstrapper creates this manager via AddComponent(), which
        /// fires OnEnable() synchronously before Initialize() has set _eventChannel (see CLAUDE.md's
        /// Camera.main precedent for the same pitfall).
        /// </summary>
        public void Initialize(OrderEventChannel eventChannel)
        {
            _eventChannel = eventChannel;
            if (_eventChannel != null)
            {
                _eventChannel.OnOrderFulfilled -= HandleOrderFulfilled;
                _eventChannel.OnOrderFulfilled += HandleOrderFulfilled;
            }
        }

        /// <summary>
        /// Subscribes to the fulfilled-order event — a no-op on the very first enable (Initialize()
        /// hasn't set _eventChannel yet), but correct for any later disable/re-enable cycle once it
        /// has.
        /// </summary>
        private void OnEnable()
        {
            if (_eventChannel != null)
            {
                _eventChannel.OnOrderFulfilled += HandleOrderFulfilled;
            }
        }

        /// <summary>Unsubscribes from the fulfilled-order event.</summary>
        private void OnDisable()
        {
            if (_eventChannel != null)
            {
                _eventChannel.OnOrderFulfilled -= HandleOrderFulfilled;
            }
        }

        /// <summary>
        /// Awards the order's moneyReward if — and only if — it's a Random- or Special-tier order
        /// (#26). Priority fulfillments reach this handler too (the same shared event every order
        /// tier raises) but are deliberately ignored, matching "the Company takes care of your
        /// other needs" — checked as "not Priority" rather than an explicit Random/Special
        /// allowlist, the same inverted-check shape GameManager's own loss-condition gate already
        /// uses, so a future fourth tier wouldn't silently need a third comparison added here.
        /// </summary>
        private void HandleOrderFulfilled(OrderData order)
        {
            if (order != null && order.tier != OrderTier.Priority)
            {
                Money += order.moneyReward;
            }
        }

        /// <summary>
        /// Attempts to deduct the given amount from the player's money total (#27) — fails (no-op,
        /// returns false) if the amount is negative or exceeds the current total. The one spend path
        /// every purchase (upgrade tiers, so far) goes through.
        /// </summary>
        public bool TrySpend(int amount)
        {
            if (amount < 0 || amount > Money)
            {
                return false;
            }

            Money -= amount;
            return true;
        }
    }
}
