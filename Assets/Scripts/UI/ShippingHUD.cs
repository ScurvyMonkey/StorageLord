using System.Collections.Generic;
using StorageLord.Docks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    /// interactive UI element in the project — folded into a small inline header row (title +
    /// dropdown, #38) that sits above the order rows as TopLeft's first child. Originally sat as
    /// its own full-width row with no label, which read as a dead/empty box; pairing it with a
    /// title fixed that with zero new mechanism (see Awake()). Selecting an option there purely
    /// tints the corresponding row (no ShippingManager/fulfillment-priority involvement at all);
    /// the options list itself is only ever rebuilt at the two discrete points a row is actually
    /// created or destroyed (never inside Update()'s per-frame loop, per #35's own arch review — a
    /// full option-list rebuild every frame would silently reset the player's selection every
    /// frame).
    ///
    /// Caps how many order rows are actually shown at once (#39) — the active-order set is
    /// unbounded over a long session (escalating waves, #13/#24), so this panel used to grow
    /// forever. <see cref="RecomputeVisibility"/> ranks every active order soonest-deadline-first,
    /// shows only the nearest <see cref="maxVisibleOrderRows"/>, and reveals the rest behind a
    /// "+N more" toggle row. It's only ever called from discrete triggers — a row being created or
    /// pruned, a dropdown selection changing, or at most once per whole real second (#39's own arch
    /// review flagged that sorting every Update() frame would repeat the exact per-frame-allocation
    /// anti-pattern #28 already fixed in ShippingManager.OrdersByPriority — this follows that same
    /// method's shape directly: a reusable buffer + List&lt;T&gt;.Sort with an explicit tiebreak).
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

        [SerializeField]
        [Tooltip("How many order rows show at once before the rest collapse behind '+N more' (#39).")]
        private int maxVisibleOrderRows = 4;

        private ShippingManager _shippingManager;
        private UIManager _uiManager;
        private OrderEventChannel _eventChannel;
        private TMP_Dropdown _dropdown;
        private RectTransform _headerRow;
        private OrderData _highlightedOrder;
        private TextMeshProUGUI _moreRow;
        private bool _isExpanded;
        private int _lastRecomputeSecond = int.MinValue;

        private readonly Dictionary<OrderData, RowEntry> _rows = new Dictionary<OrderData, RowEntry>();
        private readonly List<OrderData> _dropdownOrders = new List<OrderData>();

        // Reusable buffer for RecomputeVisibility (#39) — mirrors ShippingManager.OrdersByPriority's
        // own #28 fix exactly, for the same reason: cleared and repopulated each call rather than
        // replaced, carrying each order's original ActiveOrders enumeration index alongside it since
        // List&lt;T&gt;.Sort isn't stable, so CompareByDeadline needs an explicit tiebreak.
        private readonly List<(ActiveOrder Order, int OriginalIndex)> _orderedRowsBuffer = new List<(ActiveOrder, int)>();

        /// <summary>
        /// Caches the ShippingManager/UIManager references, then builds a small inline header row
        /// (title + the order dropdown, #35) as TopLeft's first child, hidden until at least one
        /// order is active. Folding the dropdown into this header — rather than letting it sit as
        /// its own full-width row — is #38's fix for it reading as an unlabeled, dead-looking empty
        /// box: paired with a title, it now reads as a real, working control.
        /// </summary>
        private void Awake()
        {
            _shippingManager = FindFirstObjectByType<ShippingManager>();
            _uiManager = FindFirstObjectByType<UIManager>();

            if (_uiManager == null || _uiManager.Theme == null || _uiManager.Theme.dropdownPrefab == null)
            {
                return;
            }

            // Deliberately NOT SetAsFirstSibling() — UIManager.CreateRegion's decorative background
            // is this region's only existing child at this point in Awake(), so a plain SetParent
            // append naturally lands the header right after it (visible, on top of the background)
            // and before any order row created later (#39's RecomputeVisibility only ever pushes
            // rows further toward the end via SetAsLastSibling, never touches the header). Forcing
            // index 0 here would put the header *before* the background instead, rendering it
            // hidden underneath — the same sibling-order class of bug #39 already hit once.
            _headerRow = HudTextFactory.CreateHorizontalSlot();
            _headerRow.SetParent(_uiManager.TopLeft.ContentRoot, false);
            _headerRow.name = "OrderHeader";

            HudTextFactory.CreateLabel(_headerRow, "Active Orders");

            GameObject dropdownInstance = Instantiate(_uiManager.Theme.dropdownPrefab, _headerRow, false);
            dropdownInstance.name = "OrderDropdown";

            _dropdown = dropdownInstance.GetComponent<TMP_Dropdown>();
            if (_dropdown != null)
            {
                _dropdown.onValueChanged.AddListener(HandleDropdownValueChanged);
            }

            _headerRow.gameObject.SetActive(false);

            _moreRow = HudTextFactory.CreateLabel(_uiManager.TopLeft.ContentRoot, string.Empty);
            Button moreButton = _moreRow.gameObject.AddComponent<Button>();
            moreButton.targetGraphic = _moreRow;
            moreButton.onClick.AddListener(HandleMoreClicked);
            _moreRow.gameObject.SetActive(false);
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
                RecomputeVisibility();
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

            int currentSecond = Mathf.FloorToInt(Time.time);
            if (currentSecond != _lastRecomputeSecond)
            {
                _lastRecomputeSecond = currentSecond;
                RecomputeVisibility();
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
                RecomputeVisibility();
            }
        }

        /// <summary>
        /// Rebuilds the dropdown's option list from ShippingManager.ActiveOrders (its own
        /// activation-ordered list) — called only when a row is created or pruned, never per frame.
        /// Re-selects whichever index the currently-highlighted order now occupies (indices can
        /// shift when an earlier order is removed), or clears the highlight if that order is gone.
        /// Hides the whole header row (title + dropdown, #38) entirely when no order is active,
        /// mirroring JobOfferHUD's collapse-to-nothing convention.
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
            _headerRow.gameObject.SetActive(hasAnyOrder);
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
            RecomputeVisibility();
        }

        /// <summary>
        /// Toggles manual expand/collapse when the player clicks the "+N more"/"show fewer" row
        /// (#39). Collapsing has no visible effect while a currently-hidden order is highlighted —
        /// <see cref="RecomputeVisibility"/> forces the list open in that case regardless of this
        /// flag, the simpler of the two spec-permitted ways to satisfy "never silently hide the
        /// highlighted row."
        /// </summary>
        private void HandleMoreClicked()
        {
            _isExpanded = !_isExpanded;
            RecomputeVisibility();
        }

        /// <summary>
        /// Ranks every active order soonest-deadline-first (mirroring
        /// ShippingManager.OrdersByPriority's #28 non-allocating-sort shape exactly), shows only the
        /// nearest <see cref="maxVisibleOrderRows"/> unless expanded/forced open, and repositions
        /// each row via <c>SetAsLastSibling()</c> (processed in ascending rank order) so the visible
        /// rows always read top-to-bottom in deadline order regardless of which order they were
        /// created in. Deliberately relative, not an absolute <c>SetSiblingIndex</c> — an earlier
        /// version assumed the dropdown alone preceded rows at index 0, which silently collided with
        /// TopLeftBackground's own sibling position (found live: the background ended up rendering
        /// *on top of* order text). SetAsLastSibling needs no assumption about how many/which other
        /// siblings exist, matching HudRegion.AddOrdered's (#31) same "relative to what's actually
        /// there" principle. Called only from discrete triggers (see this class's own doc comment),
        /// never every Update() frame.
        /// </summary>
        private void RecomputeVisibility()
        {
            _orderedRowsBuffer.Clear();
            int originalIndex = 0;
            foreach (ActiveOrder order in _shippingManager.ActiveOrders)
            {
                _orderedRowsBuffer.Add((order, originalIndex));
                originalIndex++;
            }

            _orderedRowsBuffer.Sort(CompareByDeadline);

            bool highlightedBeyondCap = _highlightedOrder != null && IsHighlightedBeyondCap();
            bool showAll = _isExpanded || highlightedBeyondCap || _orderedRowsBuffer.Count <= maxVisibleOrderRows;

            for (int rank = 0; rank < _orderedRowsBuffer.Count; rank++)
            {
                OrderData data = _orderedRowsBuffer[rank].Order.Data;
                if (!_rows.TryGetValue(data, out RowEntry entry))
                {
                    continue;
                }

                entry.Row.transform.SetAsLastSibling();
                entry.Row.gameObject.SetActive(showAll || rank < maxVisibleOrderRows);
            }

            UpdateMoreRow(_orderedRowsBuffer.Count, showAll);
        }

        /// <summary>True if the currently-highlighted order's rank in the last-sorted buffer falls outside the visible cap.</summary>
        private bool IsHighlightedBeyondCap()
        {
            for (int rank = 0; rank < _orderedRowsBuffer.Count; rank++)
            {
                if (_orderedRowsBuffer[rank].Order.Data == _highlightedOrder)
                {
                    return rank >= maxVisibleOrderRows;
                }
            }

            return false;
        }

        /// <summary>Shows/hides and relabels the "+N more" toggle row, keeping it as TopLeft's last sibling so it always renders below every order row.</summary>
        private void UpdateMoreRow(int totalCount, bool showAll)
        {
            if (_moreRow == null)
            {
                return;
            }

            bool needed = totalCount > maxVisibleOrderRows;
            _moreRow.gameObject.SetActive(needed);
            if (!needed)
            {
                return;
            }

            int hiddenCount = totalCount - maxVisibleOrderRows;
            _moreRow.text = showAll ? "show fewer ▴" : $"+{hiddenCount} more ▾";
            _moreRow.transform.SetAsLastSibling();
        }

        /// <summary>Comparison backing RecomputeVisibility's sort — soonest remaining deadline first, then original activation-order index as an explicit tiebreak (List&lt;T&gt;.Sort isn't stable).</summary>
        private static int CompareByDeadline((ActiveOrder Order, int OriginalIndex) a, (ActiveOrder Order, int OriginalIndex) b)
        {
            int deadlineCompare = a.Order.RemainingDeadlineSeconds.CompareTo(b.Order.RemainingDeadlineSeconds);
            if (deadlineCompare != 0)
            {
                return deadlineCompare;
            }

            return a.OriginalIndex.CompareTo(b.OriginalIndex);
        }
    }
}
