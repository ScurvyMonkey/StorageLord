using UnityEngine;
using StorageLord.Docks;

namespace StorageLord.UI
{
    /// <summary>
    /// Minimal placeholder on-screen readout of every currently active order — goods name,
    /// delivered/required count, and time remaining. Deliberately bare (OnGUI, no styling); a real
    /// styled HUD is Phase 2 UI polish territory (CLAUDE.md's Roadmap, no UIManager exists yet).
    /// Created via Bootstrapper alongside the real managers even though it isn't one itself, so
    /// every runtime object still comes from one place rather than needing a hand-placed scene
    /// object.
    /// </summary>
    public class ShippingHUD : MonoBehaviour
    {
        private ShippingManager _shippingManager;

        /// <summary>
        /// Caches the ShippingManager reference once rather than looking it up every OnGUI call —
        /// OnGUI runs at similar frequency to Update, sometimes multiple times per frame across
        /// different event types.
        /// </summary>
        private void Awake()
        {
            _shippingManager = FindFirstObjectByType<ShippingManager>();
        }

        /// <summary>
        /// Draws one text line per active order in the top-left corner of the screen.
        /// </summary>
        private void OnGUI()
        {
            if (_shippingManager == null)
            {
                return;
            }

            float y = 10f;
            foreach (ActiveOrder order in _shippingManager.ActiveOrders)
            {
                string goodsName = order.Data.requiredGoods != null ? order.Data.requiredGoods.displayName : "?";
                string label = $"{order.Data.displayName}: {order.Delivered}/{order.Data.requiredQuantity} {goodsName} — {Mathf.Max(0f, order.RemainingDeadlineSeconds):F0}s left";
                GUI.Label(new Rect(10f, y, 600f, 20f), label);
                y += 20f;
            }
        }
    }
}
