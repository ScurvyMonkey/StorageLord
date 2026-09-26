using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

        [Header("Dropdown — real clickable option list (Order Dropdown, #35)")]
        public GameObject dropdownPrefab;

        [Header("Store Item Card — buyable upgrade card (Upgrade Store Panel, #37)")]
        public GameObject storeItemCardPrefab;

        /// <summary>
        /// Removes this pack's own baked-in example content (a "Text (TMP)..."-named
        /// TextMeshProUGUI label and/or a Button, present on every window/popup/tip prefab checked)
        /// from an instantiated prefab, leaving only its decorative frame/border/glow art — found
        /// live during #30's own verification pass (a first render showed real demo titles, "YES"/
        /// "NO" buttons, and a close "X", not a blank frame). The real content that replaces them is
        /// added as new children by whichever HUD uses the instance, not by editing these baked-in
        /// objects. Public so any HUD instantiating a theme prefab directly (not just UIManager's
        /// own region backgrounds) can reuse it — e.g. GameOverHUD's popup.
        /// </summary>
        public static void StripDemoContent(GameObject instance)
        {
            Transform[] children = instance.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child == instance.transform)
                {
                    continue;
                }

                if (child.GetComponent<TextMeshProUGUI>() != null || child.GetComponent<Button>() != null)
                {
                    Destroy(child.gameObject);
                }
            }
        }
    }
}
