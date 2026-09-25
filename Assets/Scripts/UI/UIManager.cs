using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StorageLord.UI
{
    /// <summary>
    /// Owns the game's single root Canvas and the reusable, auto-stacking screen-region containers
    /// every HUD parents its content under — fulfills the long-"proposed" UIManager slot in
    /// CLAUDE.md's manager hierarchy. Populates no HUD content itself; this is the shell #31-#33
    /// plug into. Every existing HUD self-serves this reference the same way it already self-serves
    /// its own manager (<c>FindFirstObjectByType&lt;UIManager&gt;()</c> in its own <c>Awake</c>), so
    /// this manager must be created before Bootstrapper's *first* HUD creation call — HUD creation
    /// is interleaved throughout <c>Bootstrapper.Awake()</c>, not batched, so in practice this means
    /// created before every other manager too.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        private const float RegionMargin = 20f;

        /// <summary>Top-right stack — GameOverHUD, ScoreHUD, WeightHUD, JobOfferHUD, UpgradeHUD.</summary>
        public HudRegion TopRightStack { get; private set; }

        /// <summary>Top-left — ShippingHUD's active order list.</summary>
        public HudRegion TopLeft { get; private set; }

        /// <summary>Top-center, toggleable (inactive by default) — OrderGuideHUD.</summary>
        public HudRegion TopCenter { get; private set; }

        /// <summary>Bottom-left — ReceivingHUD's current-goods line.</summary>
        public HudRegion BottomLeft { get; private set; }

        /// <summary>Bottom-right — ControlsHUD's mode-aware keybinding reference.</summary>
        public HudRegion BottomRight { get; private set; }

        /// <summary>Standalone centered anchor for GameOverHUD's conditional "RUN OVER" popup.</summary>
        public RectTransform CenterPopupAnchor { get; private set; }

        /// <summary>
        /// Builds the root Canvas, its CanvasScaler, all 5 auto-stacking regions (each optionally
        /// backed by a decorative frame from <paramref name="theme"/>), and the standalone center
        /// popup anchor. Called once by Bootstrapper at startup.
        /// </summary>
        public void Initialize(UIThemeData theme)
        {
            GameObject canvasObject = new GameObject("UICanvas");
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Transform canvasTransform = canvasObject.transform;

            TopRightStack = CreateRegion(canvasTransform, "TopRightStack", new Vector2(1f, 1f), TextAnchor.UpperRight, theme != null ? theme.statPanelPrefab : null, new Vector2(360f, 130f));
            TopLeft = CreateRegion(canvasTransform, "TopLeft", new Vector2(0f, 1f), TextAnchor.UpperLeft, theme != null ? theme.listPanelPrefab : null, new Vector2(520f, 360f));
            TopCenter = CreateRegion(canvasTransform, "TopCenter", new Vector2(0.5f, 1f), TextAnchor.UpperCenter, theme != null ? theme.guideWindowPrefab : null, new Vector2(640f, 460f));
            TopCenter.gameObject.SetActive(false);
            BottomLeft = CreateRegion(canvasTransform, "BottomLeft", new Vector2(0f, 0f), TextAnchor.LowerLeft, theme != null ? theme.statPanelPrefab : null, new Vector2(360f, 130f));
            BottomRight = CreateRegion(canvasTransform, "BottomRight", new Vector2(1f, 0f), TextAnchor.LowerRight, theme != null ? theme.bottomBarPanelPrefab : null, new Vector2(460f, 160f));

            GameObject popupAnchorObject = new GameObject("CenterPopupAnchor", typeof(RectTransform));
            CenterPopupAnchor = (RectTransform)popupAnchorObject.transform;
            CenterPopupAnchor.SetParent(canvasTransform, false);
            AnchorToCorner(CenterPopupAnchor, new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// Creates one anchored, auto-stacking HudRegion at the given normalized screen corner, plus
        /// an optional purely decorative background frame instance sized to <paramref
        /// name="backgroundSize"/> — a layout-ignored child, so it never affects the region's
        /// dynamic content sizing, and it follows the region's own active/inactive state
        /// automatically (e.g. OrderGuideHUD's P-toggle). The pack's own authored size is
        /// deliberately overridden: these prefabs are sized for a full demo scene (several hundred
        /// to 1000+ px), not a compact corner HUD readout — found live during #30's own verification
        /// (an initial pass trusting the prefab's own size rendered two frames occupying most of the
        /// screen).
        /// </summary>
        private HudRegion CreateRegion(Transform parent, string name, Vector2 corner, TextAnchor childAlignment, GameObject backgroundFramePrefab, Vector2 backgroundSize)
        {
            GameObject regionObject = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            regionObject.transform.SetParent(parent, false);
            RectTransform regionRect = (RectTransform)regionObject.transform;
            AnchorToCorner(regionRect, corner);

            HudRegion region = regionObject.AddComponent<HudRegion>();
            region.Initialize(childAlignment);

            if (backgroundFramePrefab != null)
            {
                GameObject background = Instantiate(backgroundFramePrefab, regionRect, false);
                background.name = name + "Background";
                RectTransform backgroundRect = (RectTransform)background.transform;
                AnchorToCorner(backgroundRect, corner);
                backgroundRect.sizeDelta = backgroundSize;
                StripDemoContent(background);

                LayoutElement layoutElement = background.AddComponent<LayoutElement>();
                layoutElement.ignoreLayout = true;
                background.transform.SetAsFirstSibling();
            }

            return region;
        }

        /// <summary>
        /// Removes the SCI-FI UI Pack Pro's own baked-in example content (a "Text (TMP)..."-named
        /// TextMeshProUGUI label and/or a Button, present on every window/popup/tip prefab checked)
        /// from a background frame instance, leaving only its decorative frame/border/glow art —
        /// found live during #30's own verification pass (a first render showed real demo titles,
        /// "YES"/"NO" buttons, and a close "X", not a blank frame). The real content that replaces
        /// them is added as new children by whichever HUD uses this region (#31-#33), not by editing
        /// these baked-in objects.
        /// </summary>
        private static void StripDemoContent(GameObject instance)
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

        /// <summary>
        /// Anchors, pivots, and inset-margins a RectTransform to one of 9 normalized screen points
        /// (0/0.5/1 per axis) — a center axis (0.5) gets no margin on that axis.
        /// </summary>
        private static void AnchorToCorner(RectTransform rect, Vector2 corner)
        {
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;

            float xOffset = corner.x < 0.5f ? RegionMargin : (corner.x > 0.5f ? -RegionMargin : 0f);
            float yOffset = corner.y < 0.5f ? RegionMargin : (corner.y > 0.5f ? -RegionMargin : 0f);
            rect.anchoredPosition = new Vector2(xOffset, yOffset);
        }
    }
}
