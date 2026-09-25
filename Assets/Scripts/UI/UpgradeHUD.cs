using StorageLord.Core;
using TMPro;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// On-screen readout of both upgrade tracks (#27) — conveyor belt speed (Shift+left-click a
    /// placed belt) and platform weight capacity (left-click a placed floor tile) — showing each
    /// track's current tier and the next tier's cost, or "MAX" once exhausted. Styled UGUI as of
    /// #31, parented under UIManager's shared top-right stack region rather than drawing its own
    /// OnGUI Rect. Created via Bootstrapper alongside the real managers even though it isn't one
    /// itself, so every runtime object still comes from one place rather than needing a hand-placed
    /// scene object.
    /// </summary>
    public class UpgradeHUD : MonoBehaviour
    {
        private UpgradeManager _upgradeManager;
        private TextMeshProUGUI _conveyorLabel;
        private TextMeshProUGUI _platformLabel;

        private int _lastConveyorTier = int.MinValue;
        private int _lastConveyorCost = int.MinValue;
        private int _lastPlatformTier = int.MinValue;
        private int _lastPlatformCost = int.MinValue;

        /// <summary>
        /// Caches the UpgradeManager reference, then builds this HUD's two-line slot and inserts it
        /// into the shared top-right stack at its fixed visual position (last).
        /// </summary>
        private void Awake()
        {
            _upgradeManager = FindFirstObjectByType<UpgradeManager>();

            UIManager uiManager = FindFirstObjectByType<UIManager>();
            if (uiManager == null)
            {
                return;
            }

            RectTransform slot = HudTextFactory.CreateSlot();
            uiManager.TopRightStack.AddOrdered(slot, UIManager.TopRightOrderUpgrade);

            _conveyorLabel = HudTextFactory.CreateLabel(slot, string.Empty);
            _platformLabel = HudTextFactory.CreateLabel(slot, string.Empty);
        }

        /// <summary>
        /// Updates each line's text only when its own tier/cost has actually changed since last
        /// frame — a purchase happens on a discrete click, not continuously, so this rarely rebuilds.
        /// </summary>
        private void Update()
        {
            if (_upgradeManager == null || _conveyorLabel == null)
            {
                return;
            }

            int conveyorCost = _upgradeManager.NextConveyorCost ?? -1;
            if (_upgradeManager.ConveyorTierIndex != _lastConveyorTier || conveyorCost != _lastConveyorCost)
            {
                _lastConveyorTier = _upgradeManager.ConveyorTierIndex;
                _lastConveyorCost = conveyorCost;
                string costText = _upgradeManager.NextConveyorCost.HasValue ? $" — next ${_upgradeManager.NextConveyorCost.Value}" : " — MAX";
                _conveyorLabel.text = $"Belt Speed (Shift+click a belt): tier {_lastConveyorTier}/{_upgradeManager.ConveyorTierCount}{costText}";
            }

            int platformCost = _upgradeManager.NextPlatformCost ?? -1;
            if (_upgradeManager.PlatformTierIndex != _lastPlatformTier || platformCost != _lastPlatformCost)
            {
                _lastPlatformTier = _upgradeManager.PlatformTierIndex;
                _lastPlatformCost = platformCost;
                string costText = _upgradeManager.NextPlatformCost.HasValue ? $" — next ${_upgradeManager.NextPlatformCost.Value}" : " — MAX";
                _platformLabel.text = $"Platform Capacity (click a floor tile): tier {_lastPlatformTier}/{_upgradeManager.PlatformTierCount}{costText}";
            }
        }
    }
}
