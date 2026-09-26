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
    ///
    /// Owns a real TMP_Dropdown as of #34/#35 (UIThemeData.dropdownPrefab) — the first genuinely
    /// interactive UI element in the project — inserted as TopLeft's first child so it sits above
    /// the order rows. Selecting an option there purely tints the corresponding row (no
    /// ShippingManager/fulfillment-priority involvement at all); the options list itself is only
    /// ever rebuilt at the two discrete points a row is actually created or destroyed (never inside
    /// Update()'s per-frame loop, per #35's own arch review — a full option-list rebuild every
    /// frame would silently reset the player's selection every frame).
    /// </summary>
    public class ShippingHUD : MonoBehaviour
    {
        private struct RowEntry
        {
            public TextMeshProUGUI Row;
            public int Delivered;
            public int DeadlineWholeSeconds;
        }

        private static readonly Color HighlightColor = new Color(1f, 0.75f, 0.1f);

        private ShippingManager _shippingManager;
        private UIManager _uiManager;
        private OrderEventChannel _eventChannel;
        private TMP_Dropdown _dropdown;
        private OrderData _highlightedOrder;

        private readonly Dictionary<OrderData, RowEntry> _rows = new Dictionary<OrderData, RowEntry>();
        private readonly List<OrderData> _dropdownOrders = new List<OrderData>();

        /// <summary>
        /// Caches the ShippingManager/UIManager references, then instantiates the order dropdown
        /// (#35) as TopLeft's first child, hidden until at least one order is active.
        /// </summary>
        private void Awake()
        {
            _shippingManager = FindFirstObjectByType<ShippingManager>();
            _uiManager = FindFirstObjectByType<UIManager>();

            if (_uiManager == null || _uiManager.Theme == null || _uiManager.Theme.dropdownPrefab == null)
            {
                return;
            }

            GameObject dropdownInstance = Instantiate(_uiManager.Theme.dropdownPrefab, _uiManager.TopLeft.ContentRoot, false);
            dropdownInstance.name = "OrderDropdown";
            dropdownInstance.transform.SetAsFirstSibling();

            _dropdown = dropdownInstance.GetComponent<TMP_Dropdown>();
            if (_dropdown != null)
            {
                _dropdown.onValueChanged.AddListener(HandleDropdownValueChanged);
            }

            dropdownInstance.SetActive(false);
        }

        /// <summary>Removes the dropdown's value-changed listener.</summary>
        private void OnDestroy()
        {
            if (_dropdown != null)
            {
                _dropdown.onValueChanged.RemoveListener(HandleDropdownValueChanged);
            }
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
        /// nor the dictionary ever grows past the number of orders currently on screen. Also
        /// rebuilds the dropdown's option list (#35) — one of the two discrete points that changes,
        /// never the per-frame Update() loop.
        /// </summary>
        private void HandlePruneRow(OrderData order)
        {
            if (order != null && _rows.TryGetValue(order, out RowEntry entry))
            {
                Destroy(entry.Row.gameObject);
                _rows.Remove(order);
                RebuildDropdownOptions();
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
        /// rebuilds its text only if the displayed values actually changed since last call. Tints
        /// the row every call (#35) based on whether it's the dropdown's current selection — a
        /// plain color set with no layout/rebuild cost, unlike the text-string caching above, so it
        /// doesn't need its own change-guard. A newly-created row also triggers a dropdown option
        /// rebuild — the other of the two discrete points that changes (see HandlePruneRow).
        /// </summary>
        private void UpdateRow(ActiveOrder order)
        {
            int deadlineWholeSeconds = Mathf.Max(0, Mathf.RoundToInt(order.RemainingDeadlineSeconds));
            bool isNewRow = !_rows.TryGetValue(order.Data, out RowEntry entry);

            if (isNewRow)
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

            entry.Row.color = order.Data == _highlightedOrder ? HighlightColor : Color.white;

            _rows[order.Data] = entry;

            if (isNewRow)
            {
                RebuildDropdownOptions();
            }
        }

        /// <summary>
        /// Rebuilds the dropdown's option list from ShippingManager.ActiveOrders (its own
        /// activation-ordered list) — called only when a row is created or pruned, never per frame.
        /// Re-selects whichever index the currently-highlighted order now occupies (indices can
        /// shift when an earlier order is removed), or clears the highlight if that order is gone.
        /// Hides the dropdown entirely when no order is active, mirroring JobOfferHUD's
        /// collapse-to-nothing convention.
        /// </summary>
        private void RebuildDropdownOptions()
        {
            if (_dropdown == null)
            {
                return;
            }

            _dropdownOrders.Clear();
            List<TMP_Dropdown.OptionData> optionData = new List<TMP_Dropdown.OptionData>();
            foreach (ActiveOrder order in _shippingManager.ActiveOrders)
            {
                _dropdownOrders.Add(order.Data);
                optionData.Add(new TMP_Dropdown.OptionData(order.Data.displayName));
            }

            bool hasAnyOrder = _dropdownOrders.Count > 0;
            _dropdown.gameObject.SetActive(hasAnyOrder);
            if (!hasAnyOrder)
            {
                _highlightedOrder = null;
                return;
            }

            _dropdown.ClearOptions();
            _dropdown.AddOptions(optionData);

            int index = _highlightedOrder != null ? _dropdownOrders.IndexOf(_highlightedOrder) : -1;
            if (index < 0)
            {
                _highlightedOrder = null;
                index = 0;
            }

            _dropdown.SetValueWithoutNotify(index);
            _dropdown.RefreshShownValue();
        }

        /// <summary>
        /// Updates which order is highlighted when the player picks a dropdown option — purely a UI
        /// convenience, per #35's own spec: no ShippingManager/fulfillment-priority logic is
        /// touched. The actual row tint is applied in UpdateRow, not here.
        /// </summary>
        private void HandleDropdownValueChanged(int index)
        {
            _highlightedOrder = (index >= 0 && index < _dropdownOrders.Count) ? _dropdownOrders[index] : null;
        }
    }
}
