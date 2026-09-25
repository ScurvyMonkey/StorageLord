using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StorageLord.UI
{
    /// <summary>
    /// Creates the plain building blocks every HUD panel needs — a self-sizing TMP_Text label and a
    /// self-sizing vertical "slot" container for a HUD that needs more than one line — so the 5+
    /// HUD scripts migrating off OnGUI don't each duplicate the same TextMeshProUGUI/layout setup.
    /// Applies this project's established "never silently truncate growing content" convention
    /// (word wrap off, clipping set to Overflow — CLAUDE.md's Bare OnGUI HUD Convention, translated
    /// to TMP's equivalent settings) once, here, rather than per call site.
    /// </summary>
    public static class HudTextFactory
    {
        /// <summary>
        /// Creates a new, unparented TMP_Text label pre-configured with HUD-readout defaults and a
        /// Content Size Fitter (so it sizes itself to its own text and reflows correctly inside a
        /// parent Vertical Layout Group with childControlWidth/Height disabled).
        /// </summary>
        public static TextMeshProUGUI CreateLabel(string initialText)
        {
            GameObject labelObject = new GameObject("Label", typeof(RectTransform));

            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = initialText;
            label.fontSize = 20f;
            label.color = Color.white;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.TopLeft;

            ContentSizeFitter fitter = labelObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return label;
        }

        /// <summary>Creates a label (see <see cref="CreateLabel(string)"/>) already parented under <paramref name="parent"/>.</summary>
        public static TextMeshProUGUI CreateLabel(Transform parent, string initialText)
        {
            TextMeshProUGUI label = CreateLabel(initialText);
            label.rectTransform.SetParent(parent, false);
            return label;
        }

        /// <summary>
        /// Creates a new, unparented vertical "slot" container for a HUD that needs more than one
        /// line stacked inside a single ordered position in a shared <see cref="HudRegion"/> (e.g.
        /// WeightHUD's 0..N near-capacity rows) — itself a self-sizing Vertical Layout Group, so it
        /// reflows correctly as its own children are added/removed, one nesting level below the
        /// region's own auto-stacking.
        /// </summary>
        public static RectTransform CreateSlot()
        {
            GameObject slotObject = new GameObject("Slot", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));

            VerticalLayoutGroup layout = slotObject.GetComponent<VerticalLayoutGroup>();
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.spacing = 2f;

            ContentSizeFitter fitter = slotObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return (RectTransform)slotObject.transform;
        }
    }
}
