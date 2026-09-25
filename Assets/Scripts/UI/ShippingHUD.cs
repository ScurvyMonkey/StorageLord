using System.Collections.Generic;
using UnityEngine;
using StorageLord.Docks;

namespace StorageLord.UI
{
    /// <summary>
    /// Minimal placeholder on-screen readout of every currently active order — goods name,
    /// delivered/required count, and time remaining. Deliberately bare (OnGUI, no styling); a real
    /// styled HUD is Phase 2 UI polish territory (CLAUDE.md's Roadmap, no UIManager exists yet).
    /// Created via Bootstrapper alongside the real managers even though it isn't one itself, so
    /// every runtime object still comes from one place rather than needing a hand-placed scene
    /// object.
    ///
    /// Caches each order's label string (#28) — OnGUI fires multiple times per frame (Layout +
    /// Repaint), and the displayed text (Delivered count, whole-second deadline) only actually
    /// changes at most once a frame at the source, not once per OnGUI pass. Cache entries are
    /// pruned via OrderEventChannel.OnOrderFulfilled/OnOrderMissed — required, unlike JobOfferHUD's
    /// own cache, because _activeOrders grows and shrinks continuously over an unbounded-length
    /// session (escalating wave orders), so a cache keyed by order identity that's never pruned
    /// would itself become the kind of unbounded leak this pass exists to prevent.
    /// </summary>
    public class ShippingHUD : MonoBehaviour
    {
        private struct CachedLabel
        {
            public int Delivered;
            public int DeadlineWholeSeconds;
            public string Text;
        }

        private ShippingManager _shippingManager;
        private OrderEventChannel _eventChannel;

        private readonly Dictionary<OrderData, CachedLabel> _labelCache = new Dictionary<OrderData, CachedLabel>();

        /// <summary>
        /// Caches the ShippingManager reference once rather than looking it up every OnGUI call —
        /// OnGUI runs at similar frequency to Update, sometimes multiple times per frame across
        /// different event types.
        /// </summary>
        private void Awake()
        {
            _shippingManager = FindFirstObjectByType<ShippingManager>();
        }

        /// <summary>
        /// Injects this HUD's event channel reference and subscribes to the events that prune its
        /// label cache. Called once by Bootstrapper immediately after creation — deliberately not
        /// relying on OnEnable() alone, since Bootstrapper creates this object via AddComponent(),
        /// which fires OnEnable() synchronously before Initialize() has set _eventChannel (see
        /// CLAUDE.md's Camera.main precedent for the same pitfall).
        /// </summary>
        public void Initialize(OrderEventChannel eventChannel)
        {
            _eventChannel = eventChannel;
            if (_eventChannel != null)
            {
                _eventChannel.OnOrderFulfilled -= HandlePruneCache;
                _eventChannel.OnOrderFulfilled += HandlePruneCache;
                _eventChannel.OnOrderMissed -= HandlePruneCache;
                _eventChannel.OnOrderMissed += HandlePruneCache;
            }
        }

        /// <summary>
        /// Subscribes to the prune-triggering events — a no-op on the very first enable
        /// (Initialize() hasn't set _eventChannel yet), but correct for any later disable/re-enable
        /// cycle once it has.
        /// </summary>
        private void OnEnable()
        {
            if (_eventChannel != null)
            {
                _eventChannel.OnOrderFulfilled += HandlePruneCache;
                _eventChannel.OnOrderMissed += HandlePruneCache;
            }
        }

        /// <summary>Unsubscribes from the prune-triggering events.</summary>
        private void OnDisable()
        {
            if (_eventChannel != null)
            {
                _eventChannel.OnOrderFulfilled -= HandlePruneCache;
                _eventChannel.OnOrderMissed -= HandlePruneCache;
            }
        }

        /// <summary>
        /// Removes the given order's cached label — called once an order leaves _activeOrders for
        /// good (fulfilled or missed), so the cache never grows past the number of orders currently
        /// on screen.
        /// </summary>
        private void HandlePruneCache(OrderData order)
        {
            if (order != null)
            {
                _labelCache.Remove(order);
            }
        }

        /// <summary>
        /// Draws one text line per active order in the top-left corner of the screen.
        /// </summary>
        private void OnGUI()
        {
            if (_shippingManager == null)
            {
                return;
            }

            float y = 10f;
            foreach (ActiveOrder order in _shippingManager.ActiveOrders)
            {
                GUI.Label(new Rect(10f, y, 600f, 20f), GetLabel(order));
                y += 20f;
            }
        }

        /// <summary>
        /// Returns this order's display label, rebuilding it only if the Delivered count or the
        /// whole-second deadline displayed has actually changed since the last call.
        /// </summary>
        private string GetLabel(ActiveOrder order)
        {
            int deadlineWholeSeconds = Mathf.Max(0, Mathf.RoundToInt(order.RemainingDeadlineSeconds));

            if (_labelCache.TryGetValue(order.Data, out CachedLabel cached)
                && cached.Delivered == order.Delivered
                && cached.DeadlineWholeSeconds == deadlineWholeSeconds)
            {
                return cached.Text;
            }

            string goodsName = order.Data.requiredGoods != null ? order.Data.requiredGoods.displayName : "?";
            string text = $"{order.Data.displayName}: {order.Delivered}/{order.Data.requiredQuantity} {goodsName} — {deadlineWholeSeconds}s left";

            _labelCache[order.Data] = new CachedLabel
            {
                Delivered = order.Delivered,
                DeadlineWholeSeconds = deadlineWholeSeconds,
                Text = text
            };

            return text;
        }
    }
}
