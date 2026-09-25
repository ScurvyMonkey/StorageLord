using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// Central registry of SCI-FI UI Pack Pro prefab references every HUD panel is built from — no
    /// HUD script hardcodes a prefab path directly, so mixing the pack's Blue/Cyan variants per role
    /// is a data edit on this asset, not a code change.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/UI Theme Data", fileName = "UIThemeData")]
    public class UIThemeData : ScriptableObject
    {
        [Header("Stat Panel — single/multi-line readouts (top-right stack, ReceivingHUD)")]
        public GameObject statPanelPrefab;

        [Header("List Panel — multi-row readouts (ShippingHUD)")]
        public GameObject listPanelPrefab;
        public GameObject listRowPrefab;

        [Header("Guide Window — toggleable reference screen (OrderGuideHUD)")]
        public GameObject guideWindowPrefab;

        [Header("Popup — centered conditional message (GameOverHUD)")]
        public GameObject popupPrefab;

        [Header("Bottom Bar — keybinding reference (ControlsHUD)")]
        public GameObject bottomBarPanelPrefab;
    }
}
