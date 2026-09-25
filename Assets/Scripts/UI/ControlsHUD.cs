using StorageLord.Conveyors;
using StorageLord.Placement;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// Bare OnGUI reference of the player's current key bindings (#19) — a small baseline of
    /// always-relevant global bindings (Tab, C, P) plus extra lines that only appear while
    /// container or conveyor placement mode is actually active, so the list never shows a binding
    /// that doesn't currently apply. Follows the same Bare OnGUI HUD Convention as every other
    /// Phase 1 HUD. Positioned bottom-right — the only screen corner not already claimed by
    /// GameOverHUD/WeightHUD (top-right), ShippingHUD (top-left, unbounded growth from #13's
    /// escalating waves), or ReceivingHUD (bottom-left), per /arch's review of #19.
    /// </summary>
    public class ControlsHUD : MonoBehaviour
    {
        private PlacementManager _placementManager;
        private ConveyorManager _conveyorManager;

        /// <summary>
        /// Caches manager references once rather than looking them up every OnGUI call.
        /// </summary>
        private void Awake()
        {
            _placementManager = FindFirstObjectByType<PlacementManager>();
            _conveyorManager = FindFirstObjectByType<ConveyorManager>();
        }

        /// <summary>
        /// Draws the baseline binding list bottom-right, plus mode-specific lines above it while
        /// container or conveyor placement mode is active.
        /// </summary>
        private void OnGUI()
        {
            // Built fresh here, not cached as a static field — GUI.skin can only be accessed from
            // inside an OnGUI call; a static readonly initializer runs at type-load time (the
            // moment AddComponent first touches this type), well outside that window, and throws.
            GUIStyle style = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow };

            string[] lines = BuildLines();

            float width = 260f;
            float lineHeight = 18f;
            float y = Screen.height - 10f - lines.Length * lineHeight;

            foreach (string line in lines)
            {
                GUI.Label(new Rect(Screen.width - width - 10f, y, width, lineHeight), line, style);
                y += lineHeight;
            }
        }

        /// <summary>
        /// Returns the baseline global bindings plus whichever mode-specific bindings apply right
        /// now, bottom line last so the list reads top-to-bottom as "mode-specific, then global."
        /// </summary>
        private string[] BuildLines()
        {
            if (_placementManager != null && _placementManager.IsPlacementModeActive)
            {
                return new[]
                {
                    "Left-click: confirm placement",
                    "Right-click: remove (cascades up)",
                    "R: rotate",
                    "Tab: toggle container placement",
                    "C: toggle conveyor placement",
                    "P: parts guide",
                };
            }

            if (_conveyorManager != null && _conveyorManager.IsPlacementModeActive)
            {
                return new[]
                {
                    "Left-click-drag: place belt run",
                    "Right-click: remove segment",
                    "Q / R: cycle flow direction",
                    "PageUp / PageDown: height level",
                    "Escape: cancel drag",
                    "Tab: toggle container placement",
                    "C: toggle conveyor placement",
                    "P: parts guide",
                };
            }

            return new[]
            {
                "Tab: toggle container placement",
                "C: toggle conveyor placement",
                "P: parts guide",
            };
        }
    }
}
