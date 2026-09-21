using StorageLord.Docks;
using StorageLord.Goods;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StorageLord.UI
{
    /// <summary>
    /// Toggleable, bare OnGUI reference screen listing every authored order's required goods and
    /// quantity (#16), so the player can plan ahead of what an order will need before it even
    /// activates. Static content only for this first pass — no live fulfilled/missed status, that's
    /// a future enhancement, not core scope. Created via Bootstrapper alongside the real managers
    /// even though it isn't one itself, so every runtime object still comes from one place rather
    /// than needing a hand-placed scene object.
    /// </summary>
    public class OrderGuideHUD : MonoBehaviour
    {
        private ShippingScheduleData _schedule;
        private WaveEscalationData _waveData;
        private bool _visible;

        /// <summary>
        /// Injects this HUD's data references. Called once by Bootstrapper immediately after
        /// creation — unlike every other bare HUD in the project, this one has no manager to find
        /// via FindFirstObjectByType; it reads two ScriptableObject assets directly.
        /// </summary>
        public void Initialize(ShippingScheduleData schedule, WaveEscalationData waveData)
        {
            _schedule = schedule;
            _waveData = waveData;
        }

        /// <summary>
        /// P toggles the guide's visibility on/off — works at any time, even during either
        /// placement mode, since it's a passive reference display with no gameplay interaction of
        /// its own and P isn't bound to anything else (confirmed during #16's /arch review).
        /// </summary>
        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
            {
                _visible = !_visible;
            }
        }

        /// <summary>
        /// Draws the authored order list and a note on the escalating-wave goods pool while visible;
        /// no-ops otherwise. Non-wrapping label style avoids the clipping bug found live in
        /// ReceivingHUD (#15) — the default GUI.skin.label word-wraps, which silently truncates any
        /// line too long for its Rect's single-line height.
        /// </summary>
        private void OnGUI()
        {
            if (!_visible)
            {
                return;
            }

            // wordWrap=false alone isn't enough (found live, same clipping class of bug as #15's
            // ReceivingHUD fix) — IMGUI's default style still clips non-wrapped text horizontally
            // at the Rect's own edge, and this line's content is unbounded in length by design (it
            // grows with authored content). Overflow clipping is the only setting that guarantees
            // nothing here is ever silently truncated, at the cost of possibly drawing past the
            // Rect (acceptable for a bare Phase-1 reference display).
            GUIStyle style = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow };

            float width = 460f;
            float x = (Screen.width - width) / 2f;
            float y = 60f;

            GUI.Label(new Rect(x, y, width, 20f), "Order Parts Guide (P to close)", style);
            y += 24f;

            if (_schedule != null && _schedule.scheduledOrders != null)
            {
                foreach (OrderData order in _schedule.scheduledOrders)
                {
                    if (order == null)
                    {
                        continue;
                    }

                    string goodsName = order.requiredGoods != null ? order.requiredGoods.displayName : "?";
                    string line = $"{order.displayName}: {order.requiredQuantity} x {goodsName}";
                    GUI.Label(new Rect(x, y, width, 20f), line, style);
                    y += 20f;
                }
            }

            y += 4f;
            GUI.Label(new Rect(x, y, width, 20f), $"Escalating waves may also request: {WaveGoodsNames()}", style);
        }

        /// <summary>
        /// Returns a comma-separated list of every goods display name in WaveEscalationData's pool,
        /// or a placeholder dash if none is configured.
        /// </summary>
        private string WaveGoodsNames()
        {
            if (_waveData == null || _waveData.goodsPool == null || _waveData.goodsPool.Length == 0)
            {
                return "—";
            }

            string[] names = new string[_waveData.goodsPool.Length];
            for (int i = 0; i < _waveData.goodsPool.Length; i++)
            {
                GoodsData goods = _waveData.goodsPool[i];
                names[i] = goods != null ? goods.displayName : "?";
            }

            return string.Join(", ", names);
        }
    }
}
