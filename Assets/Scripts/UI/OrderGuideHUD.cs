using StorageLord.Docks;
using StorageLord.Goods;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StorageLord.UI
{
    /// <summary>
    /// Toggleable reference screen listing every authored order's required goods and quantity
    /// (#16), so the player can plan ahead of what an order will need before it even activates.
    /// Static content only — no live fulfilled/missed status, that's a future enhancement, not core
    /// scope. Styled UGUI as of #32: content is built once here (it never changes at runtime) into
    /// UIManager's shared top-center region, and P just toggles that region's own active state
    /// instead of a local visibility bool driving OnGUI every frame. Created via Bootstrapper
    /// alongside the real managers even though it isn't one itself, so every runtime object still
    /// comes from one place rather than needing a hand-placed scene object.
    /// </summary>
    public class OrderGuideHUD : MonoBehaviour
    {
        private UIManager _uiManager;

        /// <summary>
        /// Caches the UIManager reference, matching every other HUD's self-service pattern.
        /// </summary>
        private void Awake()
        {
            _uiManager = FindFirstObjectByType<UIManager>();
        }

        /// <summary>
        /// Injects this HUD's data references and builds its (static, one-time) content into the
        /// shared top-center region. Called once by Bootstrapper immediately after creation.
        /// </summary>
        public void Initialize(ShippingScheduleData schedule, WaveEscalationData waveData)
        {
            if (_uiManager == null)
            {
                return;
            }

            HudTextFactory.CreateLabel(_uiManager.TopCenter.ContentRoot, "Order Parts Guide (P to close)");

            if (schedule != null && schedule.scheduledOrders != null)
            {
                foreach (OrderData order in schedule.scheduledOrders)
                {
                    if (order == null)
                    {
                        continue;
                    }

                    string goodsName = order.requiredGoods != null ? order.requiredGoods.displayName : "?";
                    HudTextFactory.CreateLabel(_uiManager.TopCenter.ContentRoot, $"{order.displayName}: {order.requiredQuantity} x {goodsName}");
                }
            }

            HudTextFactory.CreateLabel(_uiManager.TopCenter.ContentRoot, $"Escalating waves may also request: {WaveGoodsNames(waveData)}");
        }

        /// <summary>
        /// P toggles the guide's visibility on/off — works at any time, even during either
        /// placement mode, since it's a passive reference display with no gameplay interaction of
        /// its own and P isn't bound to anything else (confirmed during #16's /arch review).
        /// </summary>
        private void Update()
        {
            if (_uiManager == null)
            {
                return;
            }

            if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
            {
                GameObject region = _uiManager.TopCenter.gameObject;
                region.SetActive(!region.activeSelf);
            }
        }

        /// <summary>
        /// Returns a comma-separated list of every goods display name in WaveEscalationData's pool,
        /// or a placeholder dash if none is configured.
        /// </summary>
        private static string WaveGoodsNames(WaveEscalationData waveData)
        {
            if (waveData == null || waveData.goodsPool == null || waveData.goodsPool.Length == 0)
            {
                return "—";
            }

            string[] names = new string[waveData.goodsPool.Length];
            for (int i = 0; i < waveData.goodsPool.Length; i++)
            {
                GoodsData goods = waveData.goodsPool[i];
                names[i] = goods != null ? goods.displayName : "?";
            }

            return string.Join(", ", names);
        }
    }
}
