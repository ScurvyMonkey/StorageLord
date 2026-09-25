using StorageLord.Core;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// Minimal placeholder on-screen readout of both upgrade tracks (#27) — conveyor belt speed
    /// (Shift+left-click a placed belt) and platform weight capacity (left-click a placed floor
    /// tile) — showing each track's current tier and the next tier's cost, or "MAX" once exhausted.
    /// Deliberately bare (OnGUI, no styling), same convention as every other Phase 1/2-stopgap HUD.
    /// Stacked below JobOfferHUD in the top-right corner. Created via Bootstrapper alongside the
    /// real managers even though it isn't one itself, so every runtime object still comes from one
    /// place rather than needing a hand-placed scene object.
    /// </summary>
    public class UpgradeHUD : MonoBehaviour
    {
        private UpgradeManager _upgradeManager;

        /// <summary>
        /// Caches the UpgradeManager reference once rather than looking it up every OnGUI call.
        /// </summary>
        private void Awake()
        {
            _upgradeManager = FindFirstObjectByType<UpgradeManager>();
        }

        /// <summary>
        /// Draws one line per upgrade track in the top-right corner, below JobOfferHUD's own lines.
        /// wordWrap disabled and clipping set to Overflow (CLAUDE.md's Bare OnGUI HUD Convention).
        /// </summary>
        private void OnGUI()
        {
            if (_upgradeManager == null)
            {
                return;
            }

            GUIStyle style = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow };

            float y = 165f;
            string conveyorLine = $"Belt Speed (Shift+click a belt): tier {_upgradeManager.ConveyorTierIndex}/{_upgradeManager.ConveyorTierCount}"
                + (_upgradeManager.NextConveyorCost.HasValue ? $" — next ${_upgradeManager.NextConveyorCost.Value}" : " — MAX");
            GUI.Label(new Rect(Screen.width - 340f, y, 330f, 20f), conveyorLine, style);
            y += 20f;

            string platformLine = $"Platform Capacity (click a floor tile): tier {_upgradeManager.PlatformTierIndex}/{_upgradeManager.PlatformTierCount}"
                + (_upgradeManager.NextPlatformCost.HasValue ? $" — next ${_upgradeManager.NextPlatformCost.Value}" : " — MAX");
            GUI.Label(new Rect(Screen.width - 340f, y, 330f, 20f), platformLine, style);
        }
    }
}
