using StorageLord.Docks;
using StorageLord.Goods;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// Minimal placeholder readout of what ReceivingManager is currently spawning (#15).
    /// Deliberately bare (OnGUI, no styling), same pattern as ShippingHUD/WeightHUD/GameOverHUD — a
    /// real styled HUD is Phase 2 UI polish territory. Created via Bootstrapper alongside the real
    /// managers even though it isn't one itself, so every runtime object still comes from one place
    /// rather than needing a hand-placed scene object.
    /// </summary>
    public class ReceivingHUD : MonoBehaviour
    {
        private ReceivingManager _receivingManager;
        private GUIStyle _labelStyle;

        /// <summary>
        /// Caches the ReceivingManager reference once rather than looking it up every OnGUI call.
        /// </summary>
        private void Awake()
        {
            _receivingManager = FindFirstObjectByType<ReceivingManager>();
        }

        /// <summary>
        /// Draws a single line naming the currently-selected goods type, in the bottom-left corner
        /// so it doesn't collide with ShippingHUD (top-left) or WeightHUD/GameOverHUD (top-right).
        /// Uses a non-wrapping, overflow-clipped style (found live, #15 UX pass) — the default
        /// GUI.skin.label style word-wraps, which combined with the Rect's single-line height
        /// silently clipped the tail of this label's text ("...click dock to" with "change)" cut
        /// off); wordWrap alone wasn't sufficient either, since IMGUI still clips non-wrapped text
        /// at the Rect's own edge by default (found again while building #16's OrderGuideHUD, same
        /// bug class) — TextClipping.Overflow guarantees nothing here is ever silently truncated
        /// even if a future goods display name runs longer than today's content.
        /// </summary>
        private void OnGUI()
        {
            if (_receivingManager == null)
            {
                return;
            }

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow };
            }

            GoodsData current = _receivingManager.CurrentGoods;
            string goodsName = current != null ? current.displayName : "—";
            string label = $"Now Receiving: {goodsName} (click dock to change)";
            GUI.Label(new Rect(10f, Screen.height - 30f, 500f, 20f), label, _labelStyle);
        }
    }
}
