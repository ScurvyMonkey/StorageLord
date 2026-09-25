using StorageLord.Core;
using TMPro;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// Readout of the run's missed-order count (#12), plus a centered game-over message once the run
    /// has ended. Styled UGUI as of #31: the miss count is a label in UIManager's shared top-right
    /// stack region; the "RUN OVER" message is a separate popup instantiated (on first need, from
    /// UIManager.Theme.popupPrefab) at UIManager.CenterPopupAnchor, matching the old OnGUI version's
    /// "one line in the corner, one centered message" split rather than folding the popup into the
    /// stacking region. Created via Bootstrapper alongside the real managers even though it isn't
    /// one itself, so every runtime object still comes from one place rather than needing a
    /// hand-placed scene object.
    /// </summary>
    public class GameOverHUD : MonoBehaviour
    {
        private GameManager _gameManager;
        private UIManager _uiManager;
        private TextMeshProUGUI _missedLabel;
        private GameObject _popupInstance;
        private TextMeshProUGUI _popupLabel;

        private int _lastMissedCount = int.MinValue;
        private bool _popupShown;

        /// <summary>
        /// Caches the GameManager/UIManager references, then builds the miss-count label and
        /// inserts it into the shared top-right stack at its fixed visual position (first).
        /// </summary>
        private void Awake()
        {
            _gameManager = FindFirstObjectByType<GameManager>();
            _uiManager = FindFirstObjectByType<UIManager>();

            if (_uiManager == null)
            {
                return;
            }

            _missedLabel = HudTextFactory.CreateLabel("Missed orders: 0");
            _uiManager.TopRightStack.AddOrdered(_missedLabel.rectTransform, UIManager.TopRightOrderGameOver);
        }

        /// <summary>
        /// Updates the miss-count label only when it's changed, and instantiates/shows the "RUN
        /// OVER" popup the first time the run ends (it never needs to hide again afterward, since
        /// the run doesn't resume).
        /// </summary>
        private void Update()
        {
            if (_gameManager == null || _missedLabel == null)
            {
                return;
            }

            if (_gameManager.MissedCount != _lastMissedCount)
            {
                _lastMissedCount = _gameManager.MissedCount;
                _missedLabel.text = $"Missed orders: {_lastMissedCount}";
            }

            if (_gameManager.IsGameOver && !_popupShown)
            {
                ShowPopup();
            }
        }

        /// <summary>
        /// Instantiates the popup prefab (stripped of the pack's own demo content) at the standalone
        /// center anchor, adds a fresh text label into it, and shows the game-over message.
        /// </summary>
        private void ShowPopup()
        {
            _popupShown = true;

            if (_uiManager.CenterPopupAnchor == null || _uiManager.Theme == null || _uiManager.Theme.popupPrefab == null)
            {
                return;
            }

            _popupInstance = Instantiate(_uiManager.Theme.popupPrefab, _uiManager.CenterPopupAnchor, false);
            UIThemeData.StripDemoContent(_popupInstance);

            _popupLabel = HudTextFactory.CreateLabel(_popupInstance.transform, string.Empty);
            _popupLabel.alignment = TextAlignmentOptions.Center;
            _popupLabel.text = $"RUN OVER — {_gameManager.MissedCount} orders missed";
        }
    }
}
