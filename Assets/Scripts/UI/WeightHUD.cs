using StorageLord.Storage;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// Minimal placeholder readout of platform segments approaching or over their weight capacity
    /// (#14). Deliberately bare (OnGUI, no styling), same pattern as ShippingHUD/GameOverHUD — a
    /// real styled HUD is Phase 2 UI polish territory. Created via Bootstrapper alongside the real
    /// managers even though it isn't one itself, so every runtime object still comes from one place
    /// rather than needing a hand-placed scene object.
    /// </summary>
    public class WeightHUD : MonoBehaviour
    {
        private WeightManager _weightManager;

        /// <summary>
        /// Caches the WeightManager reference once rather than looking it up every OnGUI call.
        /// </summary>
        private void Awake()
        {
            _weightManager = FindFirstObjectByType<WeightManager>();
        }

        /// <summary>
        /// Draws one line per segment currently at or above the warning threshold, below
        /// GameOverHUD's own top-right readout.
        /// </summary>
        private void OnGUI()
        {
            if (_weightManager == null)
            {
                return;
            }

            float y = 40f;
            int index = 1;
            foreach (WeightManager.SegmentWeightStatus status in _weightManager.GetSegmentStatusesNearCapacity())
            {
                string overloaded = status.CurrentKg > status.CapacityKg ? " (FAILING)" : "";
                string label = $"Segment {index}: {status.CurrentKg:F0}/{status.CapacityKg:F0} kg{overloaded}";
                GUI.Label(new Rect(Screen.width - 210f, y, 200f, 20f), label);
                y += 20f;
                index++;
            }
        }
    }
}
