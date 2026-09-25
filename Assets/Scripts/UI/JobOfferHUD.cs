using System.Collections.Generic;
using StorageLord.Docks;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// Minimal placeholder on-screen readout of the current rotating pool of Random-tier job
    /// offers (#25) — goods, quantity, money reward, and time left before the offer expires and is
    /// replaced — plus the rare pending Special-tier offer (#26), if one exists, shown as one more
    /// line past the pool. The currently-highlighted offer (left-click the Shipping dock to cycle,
    /// right-click to accept) is prefixed so it's visually distinguished; ShippingManager treats
    /// the Special offer as a virtual extra slot past the pool's own indices (see its
    /// PendingSpecialOffer doc comment), so index == JobOffers.Count means "the Special line" here
    /// too. Deliberately bare (OnGUI, no styling), same pattern as every other Phase 1/2-stopgap
    /// HUD. Stacked below ScoreHUD's money line in the top-right corner. Created via Bootstrapper
    /// alongside the real managers even though it isn't one itself, so every runtime object still
    /// comes from one place rather than needing a hand-placed scene object.
    ///
    /// Caches each line's label string (#28), same motivation as ShippingHUD. Unlike ShippingHUD's
    /// cache, this one needs no pruning: the pool cache is keyed by slot index (0..poolSize-1),
    /// naturally bounded since JobOffers is a fixed-size, in-place-replaced rotating pool (#25) —
    /// its length never changes at runtime — and the Special line's cache is a single field, not a
    /// growing collection.
    /// </summary>
    public class JobOfferHUD : MonoBehaviour
    {
        private struct CachedPoolLabel
        {
            public OrderData Order;
            public int RemainingWholeSeconds;
            public bool Highlighted;
            public string Text;
        }

        private struct CachedSpecialLabel
        {
            public float TotalWeightTargetKg;
            public int TotalMoneyReward;
            public int DeadlineWholeSeconds;
            public bool Highlighted;
            public string Text;
        }

        private ShippingManager _shippingManager;

        private readonly List<CachedPoolLabel> _poolLabelCache = new List<CachedPoolLabel>();
        private CachedSpecialLabel? _specialLabelCache;

        /// <summary>
        /// Caches the ShippingManager reference once rather than looking it up every OnGUI call.
        /// </summary>
        private void Awake()
        {
            _shippingManager = FindFirstObjectByType<ShippingManager>();
        }

        /// <summary>
        /// Draws one text line per pool offer, plus one more for a pending Special offer if any,
        /// in the top-right corner below the money total — wordWrap disabled and clipping set to
        /// Overflow (CLAUDE.md's Bare OnGUI HUD Convention), since a goods display name's length
        /// can vary.
        /// </summary>
        private void OnGUI()
        {
            bool hasSpecialOffer = _shippingManager != null && _shippingManager.PendingSpecialOffer.HasValue;
            if (_shippingManager == null || (_shippingManager.JobOffers.Count == 0 && !hasSpecialOffer))
            {
                return;
            }

            GUIStyle style = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow };

            float y = 55f;
            GUI.Label(new Rect(Screen.width - 320f, y, 310f, 20f), "Job Offers (left-click dock to cycle, right-click to accept):", style);
            y += 20f;

            for (int i = 0; i < _shippingManager.JobOffers.Count; i++)
            {
                (OrderData order, float remainingLifetime) = _shippingManager.JobOffers[i];
                bool highlighted = i == _shippingManager.HighlightedOfferIndex;
                int remainingWholeSeconds = Mathf.Max(0, Mathf.RoundToInt(remainingLifetime));

                GUI.Label(new Rect(Screen.width - 320f, y, 310f, 20f), GetPoolLabel(i, order, remainingWholeSeconds, highlighted), style);
                y += 20f;
            }

            if (hasSpecialOffer)
            {
                ShippingManager.SpecialOfferPreview offer = _shippingManager.PendingSpecialOffer.Value;
                bool highlighted = _shippingManager.HighlightedOfferIndex == _shippingManager.JobOffers.Count;
                int deadlineWholeSeconds = Mathf.Max(0, Mathf.RoundToInt(offer.DeadlineSeconds));

                GUI.Label(new Rect(Screen.width - 320f, y, 310f, 20f), GetSpecialLabel(offer, deadlineWholeSeconds, highlighted), style);
            }
        }

        /// <summary>
        /// Returns pool slot i's display label, rebuilding it only if the order occupying that
        /// slot, its whole-second remaining lifetime, or its highlighted state has changed since
        /// the last call. Grows _poolLabelCache to fit as needed — its length settles at poolSize
        /// after the first frame and never changes again, since JobOffers is a fixed-size pool.
        /// </summary>
        private string GetPoolLabel(int index, OrderData order, int remainingWholeSeconds, bool highlighted)
        {
            while (_poolLabelCache.Count <= index)
            {
                _poolLabelCache.Add(default);
            }

            CachedPoolLabel cached = _poolLabelCache[index];
            if (cached.Text != null && cached.Order == order && cached.RemainingWholeSeconds == remainingWholeSeconds && cached.Highlighted == highlighted)
            {
                return cached.Text;
            }

            string prefix = highlighted ? "> " : "  ";
            string goodsName = order.requiredGoods != null ? order.requiredGoods.displayName : "?";
            string text = $"{prefix}{goodsName} x{order.requiredQuantity} — ${order.moneyReward} — expires in {remainingWholeSeconds}s";

            _poolLabelCache[index] = new CachedPoolLabel
            {
                Order = order,
                RemainingWholeSeconds = remainingWholeSeconds,
                Highlighted = highlighted,
                Text = text
            };

            return text;
        }

        /// <summary>
        /// Returns the pending Special offer's display label, rebuilding it only if its weight
        /// target, money reward, whole-second deadline, or highlighted state has changed since the
        /// last call.
        /// </summary>
        private string GetSpecialLabel(ShippingManager.SpecialOfferPreview offer, int deadlineWholeSeconds, bool highlighted)
        {
            if (_specialLabelCache.HasValue)
            {
                CachedSpecialLabel cached = _specialLabelCache.Value;
                if (cached.TotalWeightTargetKg == offer.TotalWeightTargetKg
                    && cached.TotalMoneyReward == offer.TotalMoneyReward
                    && cached.DeadlineWholeSeconds == deadlineWholeSeconds
                    && cached.Highlighted == highlighted)
                {
                    return cached.Text;
                }
            }

            string prefix = highlighted ? "> " : "  ";
            string text = $"{prefix}SPECIAL PROJECT — {offer.TotalWeightTargetKg:F0}kg total — ${offer.TotalMoneyReward} — deadline {deadlineWholeSeconds}s";

            _specialLabelCache = new CachedSpecialLabel
            {
                TotalWeightTargetKg = offer.TotalWeightTargetKg,
                TotalMoneyReward = offer.TotalMoneyReward,
                DeadlineWholeSeconds = deadlineWholeSeconds,
                Highlighted = highlighted,
                Text = text
            };

            return text;
        }
    }
}
