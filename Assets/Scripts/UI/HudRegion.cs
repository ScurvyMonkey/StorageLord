using UnityEngine;
using UnityEngine.UI;

namespace StorageLord.UI
{
    /// <summary>
    /// One auto-stacking screen region — a Vertical Layout Group + Content Size Fitter that grows or
    /// shrinks as HUD panels add/remove children, replacing every HUD's previous independent,
    /// hardcoded pixel Y-offset (the source of the real WeightHUD/ScoreHUD and JobOfferHUD/WeightHUD
    /// overlaps confirmed during #30's arch review) with one shared reflow mechanism per screen
    /// corner. Presentation-agnostic — knows nothing about UIThemeData or which HUD uses it;
    /// UIManager owns anchoring this region to a screen corner and instantiating any decorative
    /// background frame as a layout-ignored child.
    /// </summary>
    [RequireComponent(typeof(VerticalLayoutGroup), typeof(ContentSizeFitter))]
    public class HudRegion : MonoBehaviour
    {
        /// <summary>The transform HUD panels should parent their own content under — this region itself.</summary>
        public RectTransform ContentRoot => (RectTransform)transform;

        /// <summary>
        /// Configures this region's auto-stacking layout behavior. Called once by UIManager at
        /// startup, never re-run per frame.
        /// </summary>
        public void Initialize(TextAnchor childAlignment)
        {
            VerticalLayoutGroup layout = GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = childAlignment;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.spacing = 4f;
            layout.padding = new RectOffset(8, 8, 8, 8);

            ContentSizeFitter fitter = GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }
}
