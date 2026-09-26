using System;
using StorageLord.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace StorageLord.UI
{
    /// <summary>
    /// Real store-item cards for both upgrade tracks (#37) — conveyor belt speed and platform weight
    /// capacity — each showing the track's current tier/next cost (or "MAX") and a real Buy button,
    /// replacing this HUD's original plain-text readout (#31). U toggles the whole panel, matching
    /// OrderGuideHUD's P-toggle precedent — a dedicated, toggleable UIManager.BottomCenter region
    /// (#37's own arch review measured Panel_StoreItem's real 600x350 authored size against
    /// TopRightStack's much smaller, already-shared footprint and found them genuinely incompatible).
    /// Each Buy button calls UpgradeManager.TryPurchaseNextConveyorTier()/TryPurchaseNextPlatformTier()
    /// directly — the same methods the existing Shift+click-a-belt/click-a-floor-tile world
    /// interactions already call, so a purchase via either path is instantly reflected by the other.
    /// Created via Bootstrapper alongside the real managers even though it isn't one itself, so every
    /// runtime object still comes from one place rather than needing a hand-placed scene object.
    /// </summary>
    public class UpgradeHUD : MonoBehaviour
    {
        private UpgradeManager _upgradeManager;
        private UIManager _uiManager;

        private TextMeshProUGUI _conveyorTierLabel;
        private TextMeshProUGUI _platformTierLabel;

        private int _lastConveyorTier = int.MinValue;
        private int _lastConveyorCost = int.MinValue;
        private int _lastPlatformTier = int.MinValue;
        private int _lastPlatformCost = int.MinValue;

        /// <summary>
        /// Caches the UpgradeManager/UIManager references, then builds both store cards into the
        /// shared bottom-center region, hidden until the player toggles it with U.
        /// </summary>
        private void Awake()
        {
            _upgradeManager = FindFirstObjectByType<UpgradeManager>();
            _uiManager = FindFirstObjectByType<UIManager>();

            if (_uiManager == null || _uiManager.BottomCenter == null || _uiManager.Theme == null || _uiManager.Theme.storeItemCardPrefab == null)
            {
                return;
            }

            _conveyorTierLabel = BuildCard(_uiManager.BottomCenter.ContentRoot, "Belt Speed", () => _upgradeManager?.TryPurchaseNextConveyorTier());
            _platformTierLabel = BuildCard(_uiManager.BottomCenter.ContentRoot, "Platform Capacity", () => _upgradeManager?.TryPurchaseNextPlatformTier());
        }

        /// <summary>
        /// Instantiates one Panel_StoreItem card, sets its title, strips the pack's own demo
        /// placeholder icon and dual-currency price row (Storage Lord has a single Money currency,
        /// not the pack's coin+gem pair), wires its Buy button to <paramref name="onBuy"/>, and
        /// returns the one surviving text field for the tier/cost readout. Deliberately does not use
        /// UIThemeData.StripDemoContent — that helper destroys every TextMeshProUGUI/Button
        /// wholesale, which would remove the title, tier label, and Buy button this card actually
        /// needs to keep.
        /// </summary>
        private TextMeshProUGUI BuildCard(Transform parent, string title, Action onBuy)
        {
            GameObject instance = Instantiate(_uiManager.Theme.storeItemCardPrefab, parent, false);

            Transform placeholderIcon = instance.transform.Find("ItemShow/Your item");
            if (placeholderIcon != null)
            {
                Destroy(placeholderIcon.gameObject);
            }

            TextMeshProUGUI titleLabel = instance.transform.Find("ItemTitle").GetComponentInChildren<TextMeshProUGUI>();
            titleLabel.text = title;

            Transform priceRow = instance.transform.Find("GameObject");
            TextMeshProUGUI[] priceTexts = priceRow.GetComponentsInChildren<TextMeshProUGUI>();
            Image[] priceIcons = priceRow.GetComponentsInChildren<Image>();
            TextMeshProUGUI tierLabel = priceTexts[1];
            Destroy(priceTexts[0].gameObject);
            Destroy(priceTexts[2].gameObject);
            foreach (Image icon in priceIcons)
            {
                Destroy(icon.gameObject);
            }

            // The pack authored this field to show a short number ("100"), not a sentence-length
            // readout ("tier 1/3 — next $120") — found live, #37: word-wrap in its own narrow width
            // was splitting even single words ("next") mid-character. Widened and set to the same
            // never-truncate convention HudTextFactory.CreateLabel already establishes for
            // HUD text whose length can vary.
            tierLabel.enableWordWrapping = false;
            tierLabel.overflowMode = TextOverflowModes.Overflow;
            RectTransform tierLabelRect = tierLabel.rectTransform;
            tierLabelRect.sizeDelta = new Vector2(360f, tierLabelRect.sizeDelta.y);

            Button buyButton = instance.GetComponentInChildren<Button>();
            buyButton.onClick.AddListener(() => onBuy());

            return tierLabel;
        }

        /// <summary>
        /// U toggles the whole panel's visibility. Updates each card's tier/cost text only when it's
        /// actually changed since last frame — a purchase happens on a discrete click, not
        /// continuously, so this rarely rebuilds.
        /// </summary>
        private void Update()
        {
            if (_upgradeManager == null || _uiManager == null || _uiManager.BottomCenter == null)
            {
                return;
            }

            if (Keyboard.current != null && Keyboard.current.uKey.wasPressedThisFrame)
            {
                GameObject region = _uiManager.BottomCenter.gameObject;
                region.SetActive(!region.activeSelf);
            }

            if (_conveyorTierLabel == null)
            {
                return;
            }

            int conveyorCost = _upgradeManager.NextConveyorCost ?? -1;
            if (_upgradeManager.ConveyorTierIndex != _lastConveyorTier || conveyorCost != _lastConveyorCost)
            {
                _lastConveyorTier = _upgradeManager.ConveyorTierIndex;
                _lastConveyorCost = conveyorCost;
                string costText = _upgradeManager.NextConveyorCost.HasValue ? $" — next ${_upgradeManager.NextConveyorCost.Value}" : " — MAX";
                _conveyorTierLabel.text = $"tier {_lastConveyorTier}/{_upgradeManager.ConveyorTierCount}{costText}";
            }

            int platformCost = _upgradeManager.NextPlatformCost ?? -1;
            if (_upgradeManager.PlatformTierIndex != _lastPlatformTier || platformCost != _lastPlatformCost)
            {
                _lastPlatformTier = _upgradeManager.PlatformTierIndex;
                _lastPlatformCost = platformCost;
                string costText = _upgradeManager.NextPlatformCost.HasValue ? $" — next ${_upgradeManager.NextPlatformCost.Value}" : " — MAX";
                _platformTierLabel.text = $"tier {_lastPlatformTier}/{_upgradeManager.PlatformTierCount}{costText}";
            }
        }
    }
}
