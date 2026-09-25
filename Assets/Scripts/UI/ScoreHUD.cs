using StorageLord.Core;
using TMPro;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// On-screen readout of the player's running money total (#25) — styled UGUI as of #31, parented
    /// under UIManager's shared top-right stack region rather than drawing its own OnGUI Rect.
    /// Created via Bootstrapper alongside the real managers even though it isn't one itself, so every
    /// runtime object still comes from one place rather than needing a hand-placed scene object.
    /// </summary>
    public class ScoreHUD : MonoBehaviour
    {
        private ScoreManager _scoreManager;
        private TextMeshProUGUI _label;
        private int _lastMoney = int.MinValue;

        /// <summary>
        /// Caches the ScoreManager reference, then builds this HUD's single label and inserts it
        /// into the shared top-right stack at its fixed visual position (UIManager.AddOrdered
        /// resolves the correct order regardless of Bootstrapper's actual creation sequence).
        /// </summary>
        private void Awake()
        {
            _scoreManager = FindFirstObjectByType<ScoreManager>();

            UIManager uiManager = FindFirstObjectByType<UIManager>();
            if (uiManager == null)
            {
                return;
            }

            _label = HudTextFactory.CreateLabel("Money: 0");
            uiManager.TopRightStack.AddOrdered(_label.rectTransform, UIManager.TopRightOrderScore);
        }

        /// <summary>
        /// Updates the label's text only when the money total has actually changed since last frame.
        /// </summary>
        private void Update()
        {
            if (_scoreManager == null || _label == null || _scoreManager.Money == _lastMoney)
            {
                return;
            }

            _lastMoney = _scoreManager.Money;
            _label.text = $"Money: {_lastMoney}";
        }
    }
}
