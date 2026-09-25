using System.Collections.Generic;
using StorageLord.Docks;
using TMPro;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// Readout of every currently active order — goods name, delivered/required count, and time
    /// remaining. Styled UGUI as of #32, parented under UIManager's shared top-left region rather
    /// than drawing its own OnGUI Rect. Created via Bootstrapper alongside the real managers even
    /// though it isn't one itself, so every runtime object still comes from one place rather than
    /// needing a hand-placed scene object.
    ///
    /// Retains the #28 per-order label-caching pattern (rebuild text only when Delivered or the
    /// whole-second deadline actually changed) — now updating a persistent, order-identity-keyed
    /// TMP_Text row in place instead of returning a string for OnGUI to draw. Rows are created the
    /// first time an OrderData is seen and destroyed when it leaves _activeOrders for good
    /// (fulfilled or missed) via OrderEventChannel.OnOrderFulfilled/OnOrderMissed — required, unlike
    /// a plain string cache, since leaving a stale row's GameObject alive would show it forever
    /// even after its order is gone. _activeOrders is append-only (#9/#24) and ShippingHUD is
    /// TopLeft's sole consumer, so a plain create-on-first-sight/append/destroy-on-prune approach
    /// preserves correct visual order with no AddOrdered-style coordination needed (#31's own
    /// mechanism solves a different problem: multiple independent scripts sharing one region —
    /// confirmed not applicable here during #32's arch review).
    /// </summary>
    public class ShippingHUD : MonoBehaviour
    {
        private struct RowEntry
        {
            public TextMeshProUGUI Row;
            public int Delivered;
            public int DeadlineWholeSeconds;
        }

        private ShippingManager _shippingManager;
        private UIManager _uiManager;
        private OrderEventChannel _eventChannel;

        private readonly Dictionary<OrderData, RowEntry> _rows = new Dictionary<OrderData, RowEntry>();

        /// <summary>
        /// Caches the ShippingManager/UIManager references once rather than looking them up every
        /// frame.
        /// </summary>
        private void Awake()
        {
            _shippingManager = FindFirstObjectByType<ShippingManager>();
            _uiManager = FindFirstObjectByType<UIManager>();
        }

        /// <summary>
        /// Injects this HUD's event channel reference and subscribes to the events that prune its
        /// row dictionary. Called once by Bootstrapper immediately after creation — deliberately not
        /// relying on OnEnable() alone, since Bootstrapper creates this object via AddComponent(),
        /// which fires OnEnable() synchronously before Initialize() has set _eventChannel (see
        /// CLAUDE.md's Camera.main precedent for the same pitfall).
        /// </summary>
        public void Initialize(OrderEventChannel eventChannel)
        {
            _eventChannel = eventChannel;
            if (_eventChannel != null)
            {
                _eventChannel.OnOrderFulfilled -= HandlePruneRow;
                _eventChannel.OnOrderFulfilled += HandlePruneRow;
                _eventChannel.OnOrderMissed -= HandlePruneRow;
                _eventChannel.OnOrderMissed += HandlePruneRow;
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
                _eventChannel.OnOrderFulfilled += HandlePruneRow;
                _eventChannel.OnOrderMissed += HandlePruneRow;
            }
        }

        /// <summary>Unsubscribes from the prune-triggering events.</summary>
        private void OnDisable()
        {
            if (_eventChannel != null)
            {
                _eventChannel.OnOrderFulfilled -= HandlePruneRow;
                _eventChannel.OnOrderMissed -= HandlePruneRow;
            }
        }

        /// <summary>
        /// Destroys the given order's row and removes it from the tracking dictionary — called once
        /// an order leaves _activeOrders for good (fulfilled or missed), so neither the row count
        /// nor the dictionary ever grows past the number of orders currently on screen.
        /// </summary>
        private void HandlePruneRow(OrderData order)
        {
            if (order != null && _rows.TryGetValue(order, out RowEntry entry))
            {
                Destroy(entry.Row.gameObject);
                _rows.Remove(order);
            }
        }

        /// <summary>
        /// Updates one row per active order — creating a new row (appended, matching
        /// _activeOrders' own append-only activation order) the first time an OrderData is seen,
        /// rebuilding its text only if the Delivered count or the whole-second deadline changed.
        /// </summary>
        private void Update()
        {
            if (_shippingManager == null || _uiManager == null)
            {
                return;
            }

            foreach (ActiveOrder order in _shippingManager.ActiveOrders)
            {
                UpdateRow(order);
            }
        }

        /// <summary>
        /// Gets or creates the row for <paramref name="order"/>'s underlying OrderData, then
        /// rebuilds its text only if the displayed values actually changed since last call.
        /// </summary>
        private void UpdateRow(ActiveOrder order)
        {
            int deadlineWholeSeconds = Mathf.Max(0, Mathf.RoundToInt(order.RemainingDeadlineSeconds));

            if (!_rows.TryGetValue(order.Data, out RowEntry entry))
            {
                TextMeshProUGUI row = HudTextFactory.CreateLabel(_uiManager.TopLeft.ContentRoot, string.Empty);
                entry = new RowEntry { Row = row, Delivered = int.MinValue, DeadlineWholeSeconds = int.MinValue };
            }

            if (entry.Delivered != order.Delivered || entry.DeadlineWholeSeconds != deadlineWholeSeconds)
            {
                string goodsName = order.Data.requiredGoods != null ? order.Data.requiredGoods.displayName : "?";
                entry.Row.text = $"{order.Data.displayName}: {order.Delivered}/{order.Data.requiredQuantity} {goodsName} — {deadlineWholeSeconds}s left";
                entry.Delivered = order.Delivered;
                entry.DeadlineWholeSeconds = deadlineWholeSeconds;
            }

            _rows[order.Data] = entry;
        }
    }
}
