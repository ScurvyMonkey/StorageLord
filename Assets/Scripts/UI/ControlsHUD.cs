using StorageLord.Conveyors;
using StorageLord.Placement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StorageLord.UI
{
    /// <summary>
    /// Readout of the player's current key bindings (#19) — a small baseline of always-relevant
    /// global bindings (Tab, C, P) plus extra lines that only appear while container or conveyor
    /// placement mode is actually active, so the list never shows a binding that doesn't currently
    /// apply. Styled UGUI as of #33, parented under UIManager's shared bottom-right region rather
    /// than drawing its own OnGUI Rect.
    ///
    /// Content is exactly one of 3 fixed, precomputed strings (no-mode / container-mode /
    /// conveyor-mode) — not a genuinely dynamic-length list like WeightHUD/JobOfferHUD's pooled
    /// rows — so a single multi-line TMP_Text (newline-joined, precomputed once at Awake) is
    /// simpler and sufficient; TMP honors explicit '\n' as hard line breaks regardless of
    /// enableWordWrapping (#33's own arch review).
    /// </summary>
    public class ControlsHUD : MonoBehaviour
    {
        private PlacementManager _placementManager;
        private ConveyorManager _conveyorManager;
        private TextMeshProUGUI _label;
        private RectTransform _regionRect;

        private string _noModeText;
        private string _containerModeText;
        private string _conveyorModeText;
        private string _currentText;

        /// <summary>
        /// Caches manager references, precomputes the 3 possible content strings, and builds this
        /// HUD's single label into the shared bottom-right region.
        /// </summary>
        private void Awake()
        {
            _placementManager = FindFirstObjectByType<PlacementManager>();
            _conveyorManager = FindFirstObjectByType<ConveyorManager>();

            _noModeText = string.Join("\n", new[]
            {
                "Tab: toggle container placement",
                "C: toggle conveyor placement",
                "P: parts guide",
            });

            _containerModeText = string.Join("\n", new[]
            {
                "Left-click: confirm placement",
                "Right-click: remove (cascades up)",
                "R: rotate",
                "Tab: toggle container placement",
                "C: toggle conveyor placement",
                "P: parts guide",
            });

            _conveyorModeText = string.Join("\n", new[]
            {
                "Left-click-drag: place belt run",
                "Right-click: remove segment",
                "Q / R: cycle flow direction",
                "PageUp / PageDown: height level",
                "Escape: cancel drag",
                "Tab: toggle container placement",
                "C: toggle conveyor placement",
                "P: parts guide",
            });

            UIManager uiManager = FindFirstObjectByType<UIManager>();
            if (uiManager == null)
            {
                return;
            }

            _regionRect = uiManager.BottomRight.ContentRoot;
            _currentText = _noModeText;
            _label = HudTextFactory.CreateLabel(_regionRect, _currentText);
        }

        /// <summary>
        /// Swaps the label's text only when the active mode has actually changed since last frame.
        /// Forces an immediate layout rebuild of the region afterward — found live during #33's own
        /// verification: a nested Content Size Fitter (the label) inside another Content Size
        /// Fitter (the region) does not reliably recompute the region's own size when the label's
        /// line count changes at runtime via a plain `.text` reassignment, even across several
        /// settled frames (measured directly: the region's sizeDelta stayed stuck at its original
        /// 3-line height while the label's own sizeDelta correctly grew to its real 8-line height) —
        /// a known Unity UGUI nested-fitter propagation gap, not a same-frame timing lag.
        /// </summary>
        private void Update()
        {
            if (_label == null)
            {
                return;
            }

            string desired = CurrentModeText();
            if (desired != _currentText)
            {
                _currentText = desired;
                _label.text = desired;
                LayoutRebuilder.ForceRebuildLayoutImmediate(_regionRect);
            }
        }

        /// <summary>Returns whichever of the 3 precomputed strings matches the currently active mode.</summary>
        private string CurrentModeText()
        {
            if (_placementManager != null && _placementManager.IsPlacementModeActive)
            {
                return _containerModeText;
            }

            if (_conveyorManager != null && _conveyorManager.IsPlacementModeActive)
            {
                return _conveyorModeText;
            }

            return _noModeText;
        }
    }
}
