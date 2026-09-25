using System.Collections.Generic;
using StorageLord.Storage;
using TMPro;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// Readout of platform segments approaching or over their weight capacity (#14) — styled UGUI
    /// as of #31, parented under UIManager's shared top-right stack region rather than drawing its
    /// own OnGUI Rect. Created via Bootstrapper alongside the real managers even though it isn't one
    /// itself, so every runtime object still comes from one place rather than needing a hand-placed
    /// scene object.
    /// </summary>
    public class WeightHUD : MonoBehaviour
    {
        private struct CachedRow
        {
            public float CurrentKg;
            public float CapacityKg;
        }

        private WeightManager _weightManager;
        private RectTransform _slot;
        private readonly List<TextMeshProUGUI> _rowPool = new List<TextMeshProUGUI>();
        private readonly List<CachedRow> _rowCache = new List<CachedRow>();

        /// <summary>
        /// Caches the WeightManager reference, then builds this HUD's (initially empty) row slot and
        /// inserts it into the shared top-right stack at its fixed visual position.
        /// </summary>
        private void Awake()
        {
            _weightManager = FindFirstObjectByType<WeightManager>();

            UIManager uiManager = FindFirstObjectByType<UIManager>();
            if (uiManager == null)
            {
                return;
            }

            _slot = HudTextFactory.CreateSlot();
            uiManager.TopRightStack.AddOrdered(_slot, UIManager.TopRightOrderWeight);
        }

        /// <summary>
        /// Draws one pooled row per segment currently at or above the warning threshold — growing
        /// the pool lazily as needed and deactivating any trailing rows left over from a frame that
        /// had more segments near capacity than this one does. Each row's text only rebuilds when
        /// its own current/capacity values actually changed since last frame.
        /// </summary>
        private void Update()
        {
            if (_weightManager == null || _slot == null)
            {
                return;
            }

            int index = 0;
            foreach (WeightManager.SegmentWeightStatus status in _weightManager.GetSegmentStatusesNearCapacity())
            {
                TextMeshProUGUI row = GetOrCreateRow(index);
                row.gameObject.SetActive(true);

                CachedRow cached = _rowCache[index];
                if (cached.CurrentKg != status.CurrentKg || cached.CapacityKg != status.CapacityKg)
                {
                    _rowCache[index] = new CachedRow { CurrentKg = status.CurrentKg, CapacityKg = status.CapacityKg };
                    string overloaded = status.CurrentKg > status.CapacityKg ? " (FAILING)" : "";
                    row.text = $"Segment {index + 1}: {status.CurrentKg:F0}/{status.CapacityKg:F0} kg{overloaded}";
                }

                index++;
            }

            for (int i = index; i < _rowPool.Count; i++)
            {
                _rowPool[i].gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Returns the pooled row at <paramref name="index"/>, growing the pool (and its parallel
        /// cache) to fit as needed.
        /// </summary>
        private TextMeshProUGUI GetOrCreateRow(int index)
        {
            while (_rowPool.Count <= index)
            {
                _rowPool.Add(HudTextFactory.CreateLabel(_slot, string.Empty));
                _rowCache.Add(new CachedRow { CurrentKg = float.NaN, CapacityKg = float.NaN });
            }

            return _rowPool[index];
        }
    }
}
