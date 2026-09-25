using StorageLord.Docks;
using StorageLord.Goods;
using TMPro;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// Readout of what ReceivingManager is currently spawning (#15). Styled UGUI as of #33,
    /// parented under UIManager's shared bottom-left region rather than drawing its own OnGUI Rect.
    /// Created via Bootstrapper alongside the real managers even though it isn't one itself, so
    /// every runtime object still comes from one place rather than needing a hand-placed scene
    /// object.
    /// </summary>
    public class ReceivingHUD : MonoBehaviour
    {
        private ReceivingManager _receivingManager;
        private TextMeshProUGUI _label;
        private GoodsData _lastGoods;
        private bool _hasLastGoods;

        /// <summary>
        /// Caches the ReceivingManager/UIManager references, then builds this HUD's single label
        /// and inserts it into the shared bottom-left region.
        /// </summary>
        private void Awake()
        {
            _receivingManager = FindFirstObjectByType<ReceivingManager>();

            UIManager uiManager = FindFirstObjectByType<UIManager>();
            if (uiManager == null)
            {
                return;
            }

            _label = HudTextFactory.CreateLabel(uiManager.BottomLeft.ContentRoot, "Now Receiving: — (click dock to change)");
        }

        /// <summary>
        /// Updates the label's text only when the currently-selected goods type has actually
        /// changed since last frame — the same single-frame responsiveness the OnGUI version had,
        /// since the goods-selection click is handled entirely inside ReceivingManager's own input
        /// polling, independent of this HUD's render timing.
        /// </summary>
        private void Update()
        {
            if (_receivingManager == null || _label == null)
            {
                return;
            }

            GoodsData current = _receivingManager.CurrentGoods;
            if (_hasLastGoods && current == _lastGoods)
            {
                return;
            }

            _hasLastGoods = true;
            _lastGoods = current;
            string goodsName = current != null ? current.displayName : "—";
            _label.text = $"Now Receiving: {goodsName} (click dock to change)";
        }
    }
}
