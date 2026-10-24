using System.Collections.Generic;
using StorageLord.Core;
using StorageLord.Docks;
using TMPro;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// Readout of the current rotating pool of Random-tier job offers (#25) — goods, quantity,
    /// money reward, and time left before the offer expires and is replaced — plus the rare pending
    /// Special-tier offer (#26), if one exists, shown as one more line past the pool. The currently-
    /// highlighted offer (left-click the Shipping dock to cycle, right-click to accept) is prefixed
    /// so it's visually distinguished; ShippingManager treats the Special offer as a virtual extra
    /// slot past the pool's own indices (see its PendingSpecialOffer doc comment), so index ==
    /// JobOffers.Count means "the Special line" here too. Styled UGUI as of #31, parented under
    /// UIManager's shared top-right stack region rather than drawing its own OnGUI Rect. Created via
    /// Bootstrapper alongside the real managers even though it isn't one itself, so every runtime
    /// object still comes from one place rather than needing a hand-placed scene object.
    ///
    /// Retains the #28 per-line label-caching pattern (rebuild text only when the order/remaining-
    /// seconds/highlighted state actually changed) — now updating a persistent, pooled TMP_Text in
    /// place instead of returning a string for OnGUI to draw. Unlike ShippingHUD's cache, this one
    /// needs no pruning: the pool cache is keyed by slot index (0..poolSize-1), naturally bounded
    /// since JobOffers is a fixed-size, in-place-replaced rotating pool (#25) — its length never
    /// changes at runtime — and the Special line's cache is a single field, not a growing collection.
    /// </summary>
    public class JobOfferHUD : MonoBehaviour
    {
        private struct CachedPoolLabel
        {
            public OrderData Order;
            public int RemainingKey;
            public bool Highlighted;
        }

        private struct CachedSpecialLabel
        {
            public float TotalWeightTargetKg;
            public int TotalMoneyReward;
            public bool Highlighted;
        }

        private const string HeaderText = "Job Offers (left-click dock to cycle, right-click to accept):";
        private const string GatedHeaderText = "Job Offers: complete the Initiation Project to unlock";

        private ShippingManager _shippingManager;
        private RectTransform _slot;
        private TextMeshProUGUI _headerLabel;
        private TextMeshProUGUI _specialRow;

        private readonly List<TextMeshProUGUI> _rowPool = new List<TextMeshProUGUI>();
        private readonly List<CachedPoolLabel> _poolLabelCache = new List<CachedPoolLabel>();
        private CachedSpecialLabel? _specialLabelCache;

        /// <summary>
        /// Caches the ShippingManager reference, then builds this HUD's header/rows/special-line
        /// slot and inserts it into the shared top-right stack at its fixed visual position.
        /// </summary>
        private void Awake()
        {
            _shippingManager = FindFirstObjectByType<ShippingManager>();

            UIManager uiManager = FindFirstObjectByType<UIManager>();
            if (uiManager == null)
            {
                return;
            }

            _slot = HudTextFactory.CreateSlot();
            uiManager.TopRightStack.AddOrdered(_slot, UIManager.TopRightOrderJobOffer);

            _headerLabel = HudTextFactory.CreateLabel(_slot, HeaderText);
            _specialRow = HudTextFactory.CreateLabel(_slot, string.Empty);
            _specialRow.transform.SetAsLastSibling();
        }

        /// <summary>
        /// Updates the header/pool rows/special row's active state and text each frame — the whole
        /// slot collapses to nothing (matching the old OnGUI version's "draw nothing" behavior) when
        /// there are no offers at all.
        /// </summary>
        private void Update()
        {
            if (_shippingManager == null || _slot == null)
            {
                return;
            }

            if (_shippingManager.IsTutorialGated)
            {
                ShowGatedHint();
                return;
            }

            if (_headerLabel.text != HeaderText)
            {
                _headerLabel.text = HeaderText;
            }

            bool hasSpecialOffer = _shippingManager.PendingSpecialOffer.HasValue;
            bool hasAnyOffer = _shippingManager.JobOffers.Count > 0 || hasSpecialOffer;
            _headerLabel.gameObject.SetActive(hasAnyOffer);

            if (!hasAnyOffer)
            {
                for (int i = 0; i < _rowPool.Count; i++)
                {
                    _rowPool[i].gameObject.SetActive(false);
                }

                _specialRow.gameObject.SetActive(false);
                return;
            }

            for (int i = 0; i < _shippingManager.JobOffers.Count; i++)
            {
                (OrderData order, float remainingLifetime) = _shippingManager.JobOffers[i];
                bool highlighted = i == _shippingManager.HighlightedOfferIndex;
                int remainingKey = _shippingManager.Clock.RemainingDisplayKey(remainingLifetime);

                TextMeshProUGUI row = GetOrCreateRow(i);
                row.gameObject.SetActive(true);
                UpdatePoolRow(i, row, order, remainingKey, highlighted);
            }

            for (int i = _shippingManager.JobOffers.Count; i < _rowPool.Count; i++)
            {
                _rowPool[i].gameObject.SetActive(false);
            }

            if (hasSpecialOffer)
            {
                ShippingManager.SpecialOfferPreview offer = _shippingManager.PendingSpecialOffer.Value;
                bool highlighted = _shippingManager.HighlightedOfferIndex == _shippingManager.JobOffers.Count;
                _specialRow.gameObject.SetActive(true);
                UpdateSpecialRow(offer, highlighted);
            }
            else
            {
                _specialRow.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// While the tutorial gate is closed (#52), shows only a header line pointing the player at
        /// the Initiation Project, and hides every offer row.
        /// </summary>
        private void ShowGatedHint()
        {
            _headerLabel.gameObject.SetActive(true);
            if (_headerLabel.text != GatedHeaderText)
            {
                _headerLabel.text = GatedHeaderText;
            }

            for (int i = 0; i < _rowPool.Count; i++)
            {
                _rowPool[i].gameObject.SetActive(false);
            }

            _specialRow.gameObject.SetActive(false);
        }

        /// <summary>
        /// Returns the pooled row at <paramref name="index"/>, growing the pool (and its parallel
        /// cache) to fit as needed. New rows are inserted just before the special row so the special
        /// line always renders last regardless of pool growth.
        /// </summary>
        private TextMeshProUGUI GetOrCreateRow(int index)
        {
            while (_poolLabelCache.Count <= index)
            {
                TextMeshProUGUI row = HudTextFactory.CreateLabel(_slot, string.Empty);
                row.transform.SetSiblingIndex(_specialRow.transform.GetSiblingIndex());
                _rowPool.Add(row);
                _poolLabelCache.Add(new CachedPoolLabel { Order = null });
            }

            return _rowPool[index];
        }

        /// <summary>
        /// Rebuilds pool slot <paramref name="index"/>'s row text only if the order occupying that
        /// slot, its whole-second remaining lifetime, or its highlighted state has changed since the
        /// last call.
        /// </summary>
        private void UpdatePoolRow(int index, TextMeshProUGUI row, OrderData order, int remainingKey, bool highlighted)
        {
            CachedPoolLabel cached = _poolLabelCache[index];
            if (cached.Order == order && cached.RemainingKey == remainingKey && cached.Highlighted == highlighted)
            {
                return;
            }

            _poolLabelCache[index] = new CachedPoolLabel { Order = order, RemainingKey = remainingKey, Highlighted = highlighted };

            string prefix = highlighted ? "> " : "  ";
            string goodsName = order.requiredGoods != null ? order.requiredGoods.displayName : "?";
            string kind = order.isUntimed && order.requiredGoods != null
                ? $" — no deadline · ~{order.requiredQuantity * order.requiredGoods.weightKg:N0}kg stockpile"
                : $" — deliver within {_shippingManager.Clock.FormatRemaining(order.deadlineSeconds)}";
            row.text = $"{prefix}{goodsName} x{order.requiredQuantity}{kind} — ${order.moneyReward} — offer expires in {GameClockData.FormatGameMinutes(remainingKey)}";
        }

        /// <summary>
        /// Rebuilds the pending Special offer's row text only if its weight target, money reward, or
        /// highlighted state has changed since the last call. Specials are untimed since #50, so the
        /// row reads "no deadline" with the stockpile weight instead of a countdown.
        /// </summary>
        private void UpdateSpecialRow(ShippingManager.SpecialOfferPreview offer, bool highlighted)
        {
            if (_specialLabelCache.HasValue)
            {
                CachedSpecialLabel cached = _specialLabelCache.Value;
                if (cached.TotalWeightTargetKg == offer.TotalWeightTargetKg
                    && cached.TotalMoneyReward == offer.TotalMoneyReward
                    && cached.Highlighted == highlighted)
                {
                    return;
                }
            }

            _specialLabelCache = new CachedSpecialLabel
            {
                TotalWeightTargetKg = offer.TotalWeightTargetKg,
                TotalMoneyReward = offer.TotalMoneyReward,
                Highlighted = highlighted
            };

            string prefix = highlighted ? "> " : "  ";
            _specialRow.text = $"{prefix}SPECIAL PROJECT — no deadline · ~{offer.TotalWeightTargetKg:N0}kg stockpile — ${offer.TotalMoneyReward}";
        }
    }
}
