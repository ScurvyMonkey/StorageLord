using StorageLord.Conveyors;
using StorageLord.Docks;
using StorageLord.Placement;
using StorageLord.Storage;
using UnityEngine;

namespace StorageLord.Core
{
    /// <summary>
    /// Owns the run's single loss condition (#12): counts every missed order
    /// (OrderEventChannel.OnOrderMissed) and, once the count reaches GameRulesData.maxMissedOrders,
    /// ends the run — halting placement/removal input, conveyor movement, order
    /// spawning/activation, and (#27) upgrade-purchase clicks across PlacementManager,
    /// ConveyorManager, ReceivingManager, ShippingManager, and WeightManager via each one's own
    /// SetGameActive(bool). No win condition, restart flow, or scoring beyond the raw miss count —
    /// all explicitly out of scope for this first pass.
    ///
    /// Created and wired by Bootstrapper, last among the managers it depends on (it needs all five
    /// to already exist to halt them, plus a live OrderEventChannel reference) — not placed directly
    /// in a scene, since it has no serialized Inspector fields to wire (its data references are
    /// injected via Initialize()).
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        private OrderEventChannel _eventChannel;
        private GameRulesData _rules;
        private PlacementManager _placementManager;
        private ConveyorManager _conveyorManager;
        private ReceivingManager _receivingManager;
        private ShippingManager _shippingManager;
        private WeightManager _weightManager;

        /// <summary>How many orders have been missed so far this run.</summary>
        public int MissedCount { get; private set; }

        /// <summary>True once the run has ended (missed-order limit reached).</summary>
        public bool IsGameOver { get; private set; }

        /// <summary>
        /// Injects this manager's data/manager references and subscribes to the missed-order event.
        /// Called once by Bootstrapper immediately after creation — deliberately not relying on
        /// OnEnable() alone for the subscription, since Bootstrapper creates this manager via
        /// AddComponent(), which fires OnEnable() synchronously before Initialize() has set
        /// _eventChannel (see CLAUDE.md's Camera.main precedent for the same pitfall) — OnEnable()'s
        /// own subscribe attempt is a no-op against a still-null channel at that point, so this is
        /// the subscription that actually takes effect. The unsubscribe-then-subscribe here is
        /// idempotent against a second Initialize() call (Bootstrapper's "reuse existing manager"
        /// short-circuit never calls Initialize() twice today, but this stays safe if that changes).
        /// </summary>
        public void Initialize(
            OrderEventChannel eventChannel,
            GameRulesData rules,
            PlacementManager placementManager,
            ConveyorManager conveyorManager,
            ReceivingManager receivingManager,
            ShippingManager shippingManager,
            WeightManager weightManager)
        {
            _eventChannel = eventChannel;
            _rules = rules;
            _placementManager = placementManager;
            _conveyorManager = conveyorManager;
            _receivingManager = receivingManager;
            _shippingManager = shippingManager;
            _weightManager = weightManager;

            if (_eventChannel != null)
            {
                _eventChannel.OnOrderMissed -= HandleOrderMissed;
                _eventChannel.OnOrderMissed += HandleOrderMissed;
            }
        }

        /// <summary>
        /// Subscribes to the missed-order event — a no-op on the very first enable (Initialize()
        /// hasn't set _eventChannel yet, see Initialize()'s own doc comment), but correct for any
        /// later disable/re-enable cycle once it has.
        /// </summary>
        private void OnEnable()
        {
            if (_eventChannel != null)
            {
                _eventChannel.OnOrderMissed += HandleOrderMissed;
            }
        }

        /// <summary>Unsubscribes from the missed-order event.</summary>
        private void OnDisable()
        {
            if (_eventChannel != null)
            {
                _eventChannel.OnOrderMissed -= HandleOrderMissed;
            }
        }

        /// <summary>
        /// Increments the miss counter and, once it reaches the configured limit, ends the run.
        /// Only Priority-tier orders count (#24) — the Company's endless demands are the only
        /// thing that can end a run; Random/Special misses (once those tiers exist) cost the
        /// player their potential reward, not the run itself.
        /// </summary>
        private void HandleOrderMissed(OrderData order)
        {
            if (IsGameOver || order.tier != OrderTier.Priority)
            {
                return;
            }

            MissedCount++;

            int limit = _rules != null ? _rules.maxMissedOrders : 10;
            if (MissedCount >= limit)
            {
                EndRun();
            }
        }

        /// <summary>
        /// Ends the run: halts every manager that would otherwise keep accepting input or advancing
        /// state.
        /// </summary>
        private void EndRun()
        {
            IsGameOver = true;
            _placementManager?.SetGameActive(false);
            _conveyorManager?.SetGameActive(false);
            _receivingManager?.SetGameActive(false);
            _shippingManager?.SetGameActive(false);
            _weightManager?.SetGameActive(false);
        }
    }
}
