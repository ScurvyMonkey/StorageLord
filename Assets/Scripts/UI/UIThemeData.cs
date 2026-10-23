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
        public HudFrameStyle statPanelFrame = new HudFrameStyle();

        [Header("List Panel — multi-row readouts (ShippingHUD)")]
        public GameObject listPanelPrefab;
        public GameObject listRowPrefab;
        public HudFrameStyle listPanelFrame = new HudFrameStyle();

        [Header("Guide Window — toggleable reference screen (OrderGuideHUD)")]
        public GameObject guideWindowPrefab;
        public HudFrameStyle guideWindowFrame = new HudFrameStyle();

        [Header("Popup — centered panels (GameOverHUD run summary, ContainerInspectorHUD)")]
        public GameObject popupPrefab;
        public HudFrameStyle popupFrame = new HudFrameStyle();

        [Header("Bottom Bar — keybinding reference (ControlsHUD)")]
        public GameObject bottomBarPanelPrefab;
        public HudFrameStyle bottomBarPanelFrame = new HudFrameStyle();

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

    /// <summary>
    /// How one region role's background frame hugs its content (#41). The frame stretches to the
    /// region's own content-driven rect, so these only control the inset and floor — never a fixed
    /// size. Per role, not global: the pack's frames have wildly different 9-slice borders
    /// (Window001 ~8px vs. Window003's L390/T120/R390/B88), so one shared padding/min size can't fit
    /// every frame without squashing some of them.
    /// </summary>
    [System.Serializable]
    public class HudFrameStyle
    {
        [Tooltip("Image.pixelsPerUnitMultiplier for the frame — higher draws thinner 9-slice borders, letting a large-bordered frame shrink to compact content without its slices being squashed.")]
        [Min(0.01f)] public float borderPixelsPerUnitMultiplier = 1f;

        [Tooltip("Extra inset between the frame's drawn border and the content, added on top of the border itself — x=left, y=right, z=top, w=bottom. A Vector4 rather than a RectOffset: RectOffset can't be constructed in a ScriptableObject field initializer.")]
        public Vector4 extraPadding = new Vector4(12f, 12f, 10f, 10f);

        [Tooltip("Smallest size the region (and so its frame) can shrink to, in canvas units.")]
        public Vector2 minSize = new Vector2(160f, 0f);
    }
}
