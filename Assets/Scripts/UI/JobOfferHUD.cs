using System.Collections.Generic;
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
            public int RemainingWholeSeconds;
            public bool Highlighted;
        }

        private struct CachedSpecialLabel
        {
            public float TotalWeightTargetKg;
            public int TotalMoneyReward;
            public int DeadlineWholeSeconds;
            public bool Highlighted;
        }

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

            _headerLabel = HudTextFactory.CreateLabel(_slot, "Job Offers (left-click dock to cycle, right-click to accept):");
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
                int remainingWholeSeconds = Mathf.Max(0, Mathf.RoundToInt(remainingLifetime));

                TextMeshProUGUI row = GetOrCreateRow(i);
                row.gameObject.SetActive(true);
                UpdatePoolRow(i, row, order, remainingWholeSeconds, highlighted);
            }

            for (int i = _shippingManager.JobOffers.Count; i < _rowPool.Count; i++)
            {
                _rowPool[i].gameObject.SetActive(false);
            }

            if (hasSpecialOffer)
            {
                ShippingManager.SpecialOfferPreview offer = _shippingManager.PendingSpecialOffer.Value;
                bool highlighted = _shippingManager.HighlightedOfferIndex == _shippingManager.JobOffers.Count;
                int deadlineWholeSeconds = Mathf.Max(0, Mathf.RoundToInt(offer.DeadlineSeconds));

                _specialRow.gameObject.SetActive(true);
                UpdateSpecialRow(offer, deadlineWholeSeconds, highlighted);
            }
            else
            {
                _specialRow.gameObject.SetActive(false);
            }
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
        private void UpdatePoolRow(int index, TextMeshProUGUI row, OrderData order, int remainingWholeSeconds, bool highlighted)
        {
            CachedPoolLabel cached = _poolLabelCache[index];
            if (cached.Order == order && cached.RemainingWholeSeconds == remainingWholeSeconds && cached.Highlighted == highlighted)
            {
                return;
            }

            _poolLabelCache[index] = new CachedPoolLabel { Order = order, RemainingWholeSeconds = remainingWholeSeconds, Highlighted = highlighted };

            string prefix = highlighted ? "> " : "  ";
            string goodsName = order.requiredGoods != null ? order.requiredGoods.displayName : "?";
            row.text = $"{prefix}{goodsName} x{order.requiredQuantity} — ${order.moneyReward} — expires in {remainingWholeSeconds}s";
        }

        /// <summary>
        /// Rebuilds the pending Special offer's row text only if its weight target, money reward,
        /// whole-second deadline, or highlighted state has changed since the last call.
        /// </summary>
        private void UpdateSpecialRow(ShippingManager.SpecialOfferPreview offer, int deadlineWholeSeconds, bool highlighted)
        {
            if (_specialLabelCache.HasValue)
            {
                CachedSpecialLabel cached = _specialLabelCache.Value;
                if (cached.TotalWeightTargetKg == offer.TotalWeightTargetKg
                    && cached.TotalMoneyReward == offer.TotalMoneyReward
                    && cached.DeadlineWholeSeconds == deadlineWholeSeconds
                    && cached.Highlighted == highlighted)
                {
                    return;
                }
            }

            _specialLabelCache = new CachedSpecialLabel
            {
                TotalWeightTargetKg = offer.TotalWeightTargetKg,
                TotalMoneyReward = offer.TotalMoneyReward,
                DeadlineWholeSeconds = deadlineWholeSeconds,
                Highlighted = highlighted
            };

            string prefix = highlighted ? "> " : "  ";
            _specialRow.text = $"{prefix}SPECIAL PROJECT — {offer.TotalWeightTargetKg:F0}kg total — ${offer.TotalMoneyReward} — deadline {deadlineWholeSeconds}s";
        }
    }
}
