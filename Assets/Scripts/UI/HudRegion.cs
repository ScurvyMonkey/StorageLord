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

        /// <summary>
        /// Parents <paramref name="child"/> under this region and inserts it at the sibling position
        /// matching <paramref name="order"/> relative to whichever other ordered children are
        /// already present — NOT at an absolute index. Multiple independently-created HUDs can each
        /// call this for their own single slot, in whatever order Bootstrapper happens to create
        /// them, and still end up in the correct final visual sequence (#31's own arch review traced
        /// Bootstrapper's real creation order — Weight, Score, JobOffer, Upgrade, GameOver — against
        /// the intended visual order — GameOver, Score, Weight, JobOffer, Upgrade — and found they
        /// genuinely differ; an absolute-target-index approach was hand-verified to fail here, since
        /// <see cref="Transform.SetSiblingIndex"/> resolves against the *current* child count, not a
        /// fixed final-size layout). A decorative background frame (added by UIManager with no
        /// OrderMarker) is never displaced, since it never matches as an insertion point.
        /// </summary>
        public void AddOrdered(RectTransform child, int order)
        {
            child.SetParent(transform, false);
            child.gameObject.AddComponent<OrderMarker>().Order = order;

            int childCount = transform.childCount;
            int targetIndex = childCount - 1;
            for (int i = 0; i < childCount - 1; i++)
            {
                OrderMarker sibling = transform.GetChild(i).GetComponent<OrderMarker>();
                if (sibling != null && sibling.Order > order)
                {
                    targetIndex = i;
                    break;
                }
            }

            child.SetSiblingIndex(targetIndex);
        }

        /// <summary>Marks a child as participating in <see cref="AddOrdered"/>'s relative-position sort.</summary>
        private class OrderMarker : MonoBehaviour
        {
            public int Order;
        }
    }
}
