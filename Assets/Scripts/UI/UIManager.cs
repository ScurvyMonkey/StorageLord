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

        /// <summary>Canvas.referencePixelsPerUnit — left at Unity's default on the root Canvas, so 9-slice borders convert to canvas units against it.</summary>
        private const float ReferencePixelsPerUnit = 100f;

        /// <summary>
        /// Fixed visual order for the top-right stack's 4 HUDs, passed to <see
        /// cref="HudRegion.AddOrdered"/> — a single, named source of truth so GameOverHUD/ScoreHUD/
        /// WeightHUD/JobOfferHUD (4 independent scripts, each self-serving this manager in its own
        /// Awake, created in a different order by Bootstrapper than this visual sequence) never need
        /// to duplicate or guess these numbers (#31's own arch review). UpgradeHUD moved out of this
        /// stack in #37 (its own dedicated, toggleable BottomCenter region), so it no longer needs an
        /// entry here.
        /// </summary>
        public const int TopRightOrderGameOver = 0;
        public const int TopRightOrderClock = 1;
        public const int TopRightOrderRunScore = 2;
        public const int TopRightOrderScore = 3;
        public const int TopRightOrderWeight = 4;
        public const int TopRightOrderJobOffer = 5;

        /// <summary>The active theme asset, exposed so a HUD can instantiate an additional themed element beyond its region's own pre-built background (e.g. GameOverHUD's popup).</summary>
        public UIThemeData Theme { get; private set; }

        /// <summary>Top-right stack — GameOverHUD, ScoreHUD, WeightHUD, JobOfferHUD, UpgradeHUD.</summary>
        public HudRegion TopRightStack { get; private set; }

        /// <summary>Top-left — ShippingHUD's active order list.</summary>
        public HudRegion TopLeft { get; private set; }

        /// <summary>Top-center, toggleable (inactive by default) — OrderGuideHUD.</summary>
        public HudRegion TopCenter { get; private set; }

        /// <summary>
        /// Bottom-center, toggleable (inactive by default) — UpgradeHUD's store cards (#37). A
        /// dedicated region rather than squeezed into TopRightStack: #37's own arch review measured
        /// Panel_StoreItem's real authored size (600x350 per card) against TopRightStack's much
        /// smaller footprint (already shared with 4 other HUDs) and found the two genuinely
        /// incompatible — a toggleable overlay (matching OrderGuideHUD's own P-toggle precedent)
        /// avoids that clash entirely rather than trying to cram both into the same corner.
        /// </summary>
        public HudRegion BottomCenter { get; private set; }

        /// <summary>Bottom-left — ReceivingHUD's current-goods line.</summary>
        public HudRegion BottomLeft { get; private set; }

        /// <summary>Bottom-right — ControlsHUD's mode-aware keybinding reference.</summary>
        public HudRegion BottomRight { get; private set; }

        /// <summary>Standalone centered anchor for GameOverHUD's conditional "RUN OVER" popup.</summary>
        public RectTransform CenterPopupAnchor { get; private set; }

        /// <summary>
        /// Builds the root Canvas, its CanvasScaler, a GraphicRaycaster (required for the EventSystem
        /// Bootstrapper creates alongside this manager (#34) to route pointer events to any UI
        /// element at all — an EventSystem alone is not sufficient), all 5 auto-stacking regions
        /// (each optionally backed by a decorative frame from <paramref name="theme"/>), and the
        /// standalone center popup anchor. Called once by Bootstrapper at startup.
        /// </summary>
        public void Initialize(UIThemeData theme)
        {
            Theme = theme;

            GameObject canvasObject = new GameObject("UICanvas");
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            canvasObject.AddComponent<GraphicRaycaster>();

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Transform canvasTransform = canvasObject.transform;

            TopRightStack = CreateRegion(canvasTransform, "TopRightStack", new Vector2(1f, 1f), TextAnchor.UpperRight, theme != null ? theme.statPanelPrefab : null, theme != null ? theme.statPanelFrame : null);
            TopLeft = CreateRegion(canvasTransform, "TopLeft", new Vector2(0f, 1f), TextAnchor.UpperLeft, theme != null ? theme.listPanelPrefab : null, theme != null ? theme.listPanelFrame : null);
            TopCenter = CreateRegion(canvasTransform, "TopCenter", new Vector2(0.5f, 1f), TextAnchor.UpperCenter, theme != null ? theme.guideWindowPrefab : null, theme != null ? theme.guideWindowFrame : null);
            TopCenter.gameObject.SetActive(false);
            BottomLeft = CreateRegion(canvasTransform, "BottomLeft", new Vector2(0f, 0f), TextAnchor.LowerLeft, theme != null ? theme.statPanelPrefab : null, theme != null ? theme.statPanelFrame : null);
            BottomRight = CreateRegion(canvasTransform, "BottomRight", new Vector2(1f, 0f), TextAnchor.LowerRight, theme != null ? theme.bottomBarPanelPrefab : null, theme != null ? theme.bottomBarPanelFrame : null);
            BottomCenter = CreateRegion(canvasTransform, "BottomCenter", new Vector2(0.5f, 0f), TextAnchor.LowerCenter, null, null);
            BottomCenter.gameObject.SetActive(false);

            GameObject popupAnchorObject = new GameObject("CenterPopupAnchor", typeof(RectTransform));
            CenterPopupAnchor = (RectTransform)popupAnchorObject.transform;
            CenterPopupAnchor.SetParent(canvasTransform, false);
            AnchorToCorner(CenterPopupAnchor, new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// Creates a centred popup panel (#48): a HudRegion under CenterPopupAnchor whose solid
        /// background frame hugs its content, the same mechanism #41 gave the corner regions, so a
        /// popup is only as big as what it shows. CenterPopupAnchor is created last in Initialize,
        /// so popups render above every corner region. The caller parents content under the
        /// returned region's ContentRoot and destroys the region's GameObject to close it.
        /// </summary>
        public HudRegion CreatePopupPanel(string name)
        {
            return CreateRegion(
                CenterPopupAnchor, name, new Vector2(0.5f, 0.5f), TextAnchor.MiddleCenter,
                Theme != null ? Theme.popupPrefab : null, Theme != null ? Theme.popupFrame : null);
        }

        /// <summary>
        /// Creates one anchored, auto-stacking HudRegion at the given normalized screen corner, plus
        /// an optional decorative background frame that hugs the region's content (#41). The frame
        /// is a layout-ignored child stretched to the region's own rect (anchors 0,0 to 1,1), and that
        /// rect is already sized to real content by the region's Content Size Fitter, so the frame
        /// tracks content automatically with no per-frame code. It also follows the region's
        /// active/inactive state (e.g. OrderGuideHUD's P-toggle). This replaces #30-#38's
        /// hand-picked fixed background sizes, which kept ending up either far too big or too small
        /// for real content. Only the frame's root sliced Image is kept: every pack prefab's child
        /// art (title bars, lights, demo text/buttons) is authored at fixed, center-anchored
        /// positions for its native 1300x800-ish size and would not track a resized root. The
        /// region's padding is the frame's actually-drawn 9-slice border plus
        /// <paramref name="frameStyle"/>'s extra inset, so content always clears the border.
        /// </summary>
        private HudRegion CreateRegion(Transform parent, string name, Vector2 corner, TextAnchor childAlignment, GameObject backgroundFramePrefab, HudFrameStyle frameStyle)
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
                for (int i = background.transform.childCount - 1; i >= 0; i--)
                {
                    Destroy(background.transform.GetChild(i).gameObject);
                }

                RectTransform backgroundRect = (RectTransform)background.transform;
                backgroundRect.anchorMin = Vector2.zero;
                backgroundRect.anchorMax = Vector2.one;
                backgroundRect.pivot = new Vector2(0.5f, 0.5f);
                backgroundRect.anchoredPosition = Vector2.zero;
                backgroundRect.sizeDelta = Vector2.zero;

                LayoutElement layoutElement = background.AddComponent<LayoutElement>();
                layoutElement.ignoreLayout = true;
                background.transform.SetAsFirstSibling();

                HudFrameStyle style = frameStyle ?? new HudFrameStyle();
                region.ApplyFrameLayout(FramePadding(background.GetComponent<Image>(), style), style.minSize);
            }

            return region;
        }

        /// <summary>
        /// The region padding needed for content to clear <paramref name="frameImage"/>'s drawn
        /// 9-slice border: the sprite's border (left, bottom, right, top) converted to canvas units
        /// at the style's pixels-per-unit multiplier, plus the style's extra inset.
        /// </summary>
        private static RectOffset FramePadding(Image frameImage, HudFrameStyle style)
        {
            Vector4 border = Vector4.zero;
            if (frameImage != null && frameImage.sprite != null)
            {
                frameImage.pixelsPerUnitMultiplier = style.borderPixelsPerUnitMultiplier;
                float spriteUnitsPerCanvasUnit = (frameImage.sprite.pixelsPerUnit / ReferencePixelsPerUnit) * style.borderPixelsPerUnitMultiplier;
                border = frameImage.sprite.border / spriteUnitsPerCanvasUnit;
            }

            Vector4 extra = style.extraPadding;
            return new RectOffset(
                Mathf.CeilToInt(border.x + extra.x),
                Mathf.CeilToInt(border.z + extra.y),
                Mathf.CeilToInt(border.w + extra.z),
                Mathf.CeilToInt(border.y + extra.w));
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
