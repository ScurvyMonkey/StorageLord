using StorageLord.Core;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// Minimal placeholder on-screen readout of the run's missed-order count, and a simple
    /// game-over message once the run ends (#12). Deliberately bare (OnGUI, no styling), same
    /// pattern as ShippingHUD — a real styled HUD/run-summary screen is Phase 2 UI polish
    /// territory. Created via Bootstrapper alongside the real managers even though it isn't one
    /// itself, so every runtime object still comes from one place rather than needing a
    /// hand-placed scene object.
    /// </summary>
    public class GameOverHUD : MonoBehaviour
    {
        private GameManager _gameManager;

        /// <summary>
        /// Caches the GameManager reference once rather than looking it up every OnGUI call.
        /// </summary>
        private void Awake()
        {
            _gameManager = FindFirstObjectByType<GameManager>();
        }

        /// <summary>
        /// Draws the missed-order count in the top-right corner, and a centered game-over message
        /// once the run has ended.
        /// </summary>
        private void OnGUI()
        {
            if (_gameManager == null)
            {
                return;
            }

            string missedLabel = $"Missed orders: {_gameManager.MissedCount}";
            GUI.Label(new Rect(Screen.width - 210f, 10f, 200f, 20f), missedLabel);

            if (_gameManager.IsGameOver)
            {
                string message = $"RUN OVER — {_gameManager.MissedCount} orders missed";
                Vector2 size = new Vector2(400f, 30f);
                Rect rect = new Rect((Screen.width - size.x) / 2f, (Screen.height - size.y) / 2f, size.x, size.y);
                GUI.Label(rect, message);
            }
        }
    }
}
