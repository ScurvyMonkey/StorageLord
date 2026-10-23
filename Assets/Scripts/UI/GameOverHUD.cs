using StorageLord.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StorageLord.UI
{
    /// <summary>
    /// Readout of the run's missed-order count (#12), plus a centered run summary with a Restart
    /// button once the run has ended (#43, endless score-attack). Styled UGUI as of #31: the miss count is a label in UIManager's shared top-right
    /// stack region; the "RUN OVER" message is a separate popup instantiated (on first need, from
    /// UIManager.Theme.popupPrefab) at UIManager.CenterPopupAnchor, matching the old OnGUI version's
    /// "one line in the corner, one centered message" split rather than folding the popup into the
    /// stacking region. Created via Bootstrapper alongside the real managers even though it isn't
    /// one itself, so every runtime object still comes from one place rather than needing a
    /// hand-placed scene object.
    /// </summary>
    public class GameOverHUD : MonoBehaviour
    {
        private static readonly Color RestartButtonColor = new Color(0.05f, 0.45f, 0.6f, 1f);

        private GameManager _gameManager;
        private ScoreManager _scoreManager;
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
            _scoreManager = FindFirstObjectByType<ScoreManager>();
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
        /// Opens a centred popup panel (UIManager.CreatePopupPanel, #48: a solid frame sized to its
        /// content) and fills it with the run summary (#43): score against the local best (with a
        /// "New best!" flag), orders fulfilled/missed, time survived, money earned, and a Restart
        /// button.
        /// </summary>
        private void ShowPopup()
        {
            _popupShown = true;

            if (_uiManager.CenterPopupAnchor == null)
            {
                return;
            }

            HudRegion panel = _uiManager.CreatePopupPanel("RunSummaryPopup");
            _popupInstance = panel.gameObject;

            RectTransform content = HudTextFactory.CreateSlot();
            content.SetParent(panel.ContentRoot, false);
            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 16f;

            _popupLabel = HudTextFactory.CreateLabel(content, BuildSummaryText());
            _popupLabel.alignment = TextAlignmentOptions.Center;

            CreateRestartButton(content);

            LayoutRebuilder.ForceRebuildLayoutImmediate(panel.ContentRoot);
        }

        /// <summary>Builds the summary text from GameManager/ScoreManager's end-of-run state.</summary>
        private string BuildSummaryText()
        {
            int score = _scoreManager != null ? _scoreManager.Score : 0;
            int fulfilled = _scoreManager != null ? _scoreManager.OrdersFulfilled : 0;
            int earned = _scoreManager != null ? _scoreManager.MoneyEarned : 0;
            int totalSeconds = Mathf.FloorToInt(_gameManager.RunSeconds);
            string best = _gameManager.IsNewBest ? "New best!" : $"Best {_gameManager.BestScore:N0}";

            return "RUN OVER\n"
                + $"Score {score:N0} · {best}\n"
                + $"Fulfilled {fulfilled} · Missed {_gameManager.MissedCount}\n"
                + $"Survived {totalSeconds / 60}:{totalSeconds % 60:00} · Earned ${earned}";
        }

        /// <summary>
        /// Adds a real UGUI Restart button (#43, through the #34 EventSystem) under
        /// <paramref name="parent"/>. Its click handler only calls GameManager.Restart, since the
        /// scene reload it triggers tears down this HUD at end of frame.
        /// </summary>
        private void CreateRestartButton(Transform parent)
        {
            RectTransform buttonRect = HudTextFactory.CreateHorizontalSlot();
            buttonRect.name = "RestartButton";
            buttonRect.SetParent(parent, false);

            HorizontalLayoutGroup buttonLayout = buttonRect.GetComponent<HorizontalLayoutGroup>();
            buttonLayout.padding = new RectOffset(28, 28, 10, 10);
            buttonLayout.childAlignment = TextAnchor.MiddleCenter;

            Image background = buttonRect.gameObject.AddComponent<Image>();
            background.color = RestartButtonColor;

            Button button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => _gameManager.Restart());

            TextMeshProUGUI label = HudTextFactory.CreateLabel(buttonRect, "Restart");
            label.alignment = TextAlignmentOptions.Center;
        }
    }
}
