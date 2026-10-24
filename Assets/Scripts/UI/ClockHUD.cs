using StorageLord.Core;
using TMPro;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// The in-game clock line, "Day 3 · 14:00" (#53), in the top-right stack between the missed
    /// counter and the score. Reads GameManager's day and hour, which derive from its run clock and
    /// so freeze when the run ends. Rebuilds its text only when the displayed hour changes (every
    /// 12.5 real seconds at the default scale), never per frame. Created via Bootstrapper after
    /// GameManager.
    /// </summary>
    public class ClockHUD : MonoBehaviour
    {
        private GameManager _gameManager;
        private TextMeshProUGUI _label;
        private int _lastDay = int.MinValue;
        private int _lastHour = int.MinValue;

        /// <summary>Caches GameManager and builds the clock label into the top-right stack at its fixed position.</summary>
        private void Awake()
        {
            _gameManager = FindFirstObjectByType<GameManager>();

            UIManager uiManager = FindFirstObjectByType<UIManager>();
            if (uiManager == null)
            {
                return;
            }

            _label = HudTextFactory.CreateLabel("Day 1 · 00:00");
            uiManager.TopRightStack.AddOrdered(_label.rectTransform, UIManager.TopRightOrderClock);
        }

        /// <summary>Updates the label only when the game day or hour has changed.</summary>
        private void Update()
        {
            if (_gameManager == null || _label == null)
            {
                return;
            }

            int day = _gameManager.GameDay;
            int hour = _gameManager.GameHour;
            if (day == _lastDay && hour == _lastHour)
            {
                return;
            }

            _lastDay = day;
            _lastHour = hour;
            _label.text = $"Day {day} · {hour:00}:00";
        }
    }
}
