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
        /// </summary>
        private void OnGUI()
        {
            if (_receivingManager == null)
            {
                return;
            }

            GoodsData current = _receivingManager.CurrentGoods;
            string goodsName = current != null ? current.displayName : "—";
            string label = $"Now Receiving: {goodsName} (click dock to change)";
            GUI.Label(new Rect(10f, Screen.height - 30f, 400f, 20f), label);
        }
    }
}
