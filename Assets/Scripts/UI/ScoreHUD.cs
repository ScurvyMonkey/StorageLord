using StorageLord.Core;
using UnityEngine;

namespace StorageLord.UI
{
    /// <summary>
    /// Minimal placeholder on-screen readout of the player's running money total (#25).
    /// Deliberately bare (OnGUI, no styling), same pattern as every other Phase 1/2-stopgap HUD.
    /// Stacked directly below GameOverHUD's "Missed orders" line in the top-right corner. Created
    /// via Bootstrapper alongside the real managers even though it isn't one itself, so every
    /// runtime object still comes from one place rather than needing a hand-placed scene object.
    /// </summary>
    public class ScoreHUD : MonoBehaviour
    {
        private ScoreManager _scoreManager;

        /// <summary>
        /// Caches the ScoreManager reference once rather than looking it up every OnGUI call.
        /// </summary>
        private void Awake()
        {
            _scoreManager = FindFirstObjectByType<ScoreManager>();
        }

        /// <summary>
        /// Draws the current money total in the top-right corner, directly below GameOverHUD's
        /// missed-order line.
        /// </summary>
        private void OnGUI()
        {
            if (_scoreManager == null)
            {
                return;
            }

            string label = $"Money: {_scoreManager.Money}";
            GUI.Label(new Rect(Screen.width - 210f, 30f, 200f, 20f), label);
        }
    }
}
