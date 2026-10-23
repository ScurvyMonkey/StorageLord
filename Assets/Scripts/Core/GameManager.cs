using StorageLord.Conveyors;
using StorageLord.Docks;
using StorageLord.Placement;
using StorageLord.Storage;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StorageLord.Core
{
    /// <summary>
    /// Owns the run's single loss condition (#12): counts every missed order
    /// (OrderEventChannel.OnOrderMissed) and, once the count reaches GameRulesData.maxMissedOrders,
    /// ends the run — halting placement/removal input, conveyor movement, order
    /// spawning/activation, and (#27) upgrade-purchase clicks across PlacementManager,
    /// ConveyorManager, ReceivingManager, ShippingManager, and WeightManager via each one's own
    /// SetGameActive(bool). Runs are an endless score-attack (#43): there's no win condition, so on
    /// run end this also freezes the run clock and records the local best score (PlayerPrefs), and
    /// <see cref="Restart"/> starts a fresh run by reloading the scene.
    ///
    /// Created and wired by Bootstrapper, last among the managers it depends on (it needs all five
    /// to already exist to halt them, plus a live OrderEventChannel reference) — not placed directly
    /// in a scene, since it has no serialized Inspector fields to wire (its data references are
    /// injected via Initialize()).
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        private OrderEventChannel _eventChannel;
        private GameRulesData _rules;
        private PlacementManager _placementManager;
        private ConveyorManager _conveyorManager;
        private ReceivingManager _receivingManager;
        private ShippingManager _shippingManager;
        private WeightManager _weightManager;
        private ScoreManager _scoreManager;
        private ReceivingBuildingPlacer _receivingBuildingPlacer;

        private const string BestScoreKey = "StorageLord.BestScore";

        /// <summary>How many orders have been missed so far this run.</summary>
        public int MissedCount { get; private set; }

        /// <summary>True once the run has ended (missed-order limit reached).</summary>
        public bool IsGameOver { get; private set; }

        /// <summary>Seconds of run time survived so far (#43). Frozen once the run ends.</summary>
        public float RunSeconds { get; private set; }

        /// <summary>The best score ever recorded on this machine, including this run once it has ended (#43).</summary>
        public int BestScore { get; private set; }

        /// <summary>True if this run's final score beat the previously stored best (#43). Only meaningful once IsGameOver.</summary>
        public bool IsNewBest { get; private set; }

        /// <summary>
        /// Injects this manager's data/manager references and subscribes to the missed-order event.
        /// Called once by Bootstrapper immediately after creation — deliberately not relying on
        /// OnEnable() alone for the subscription, since Bootstrapper creates this manager via
        /// AddComponent(), which fires OnEnable() synchronously before Initialize() has set
        /// _eventChannel (see CLAUDE.md's Camera.main precedent for the same pitfall) — OnEnable()'s
        /// own subscribe attempt is a no-op against a still-null channel at that point, so this is
        /// the subscription that actually takes effect. The unsubscribe-then-subscribe here is
        /// idempotent against a second Initialize() call (Bootstrapper's "reuse existing manager"
        /// short-circuit never calls Initialize() twice today, but this stays safe if that changes).
        /// </summary>
        public void Initialize(
            OrderEventChannel eventChannel,
            GameRulesData rules,
            PlacementManager placementManager,
            ConveyorManager conveyorManager,
            ReceivingManager receivingManager,
            ShippingManager shippingManager,
            WeightManager weightManager,
            ScoreManager scoreManager)
        {
            _eventChannel = eventChannel;
            _rules = rules;
            _placementManager = placementManager;
            _conveyorManager = conveyorManager;
            _receivingManager = receivingManager;
            _shippingManager = shippingManager;
            _weightManager = weightManager;
            _scoreManager = scoreManager;
            BestScore = PlayerPrefs.GetInt(BestScoreKey, 0);

            if (_eventChannel != null)
            {
                _eventChannel.OnOrderMissed -= HandleOrderMissed;
                _eventChannel.OnOrderMissed += HandleOrderMissed;
            }
        }

        /// <summary>
        /// Subscribes to the missed-order event — a no-op on the very first enable (Initialize()
        /// hasn't set _eventChannel yet, see Initialize()'s own doc comment), but correct for any
        /// later disable/re-enable cycle once it has.
        /// </summary>
        private void OnEnable()
        {
            if (_eventChannel != null)
            {
                _eventChannel.OnOrderMissed += HandleOrderMissed;
            }
        }

        /// <summary>Unsubscribes from the missed-order event.</summary>
        private void OnDisable()
        {
            if (_eventChannel != null)
            {
                _eventChannel.OnOrderMissed -= HandleOrderMissed;
            }
        }

        /// <summary>Advances the run clock while the run is active, so the summary's survival time stops at run end.</summary>
        private void Update()
        {
            if (!IsGameOver)
            {
                RunSeconds += Time.deltaTime;
            }
        }

        /// <summary>
        /// Increments the miss counter and, once it reaches the configured limit, ends the run.
        /// Only Priority-tier orders count (#24) — the Company's endless demands are the only
        /// thing that can end a run; Random/Special misses (once those tiers exist) cost the
        /// player their potential reward, not the run itself.
        /// </summary>
        private void HandleOrderMissed(OrderData order)
        {
            if (IsGameOver || order.tier != OrderTier.Priority)
            {
                return;
            }

            MissedCount++;

            int limit = _rules != null ? _rules.maxMissedOrders : 10;
            if (MissedCount >= limit)
            {
                EndRun();
            }
        }

        /// <summary>
        /// Ends the run: halts every manager that would otherwise keep accepting input or advancing
        /// state, and records the best score (#43). "New best" is decided against the value stored
        /// before this run, and PlayerPrefs is saved once here rather than per frame.
        /// </summary>
        private void EndRun()
        {
            IsGameOver = true;

            int score = _scoreManager != null ? _scoreManager.Score : 0;
            int previousBest = PlayerPrefs.GetInt(BestScoreKey, 0);
            IsNewBest = score > previousBest;
            BestScore = Mathf.Max(score, previousBest);
            if (IsNewBest)
            {
                PlayerPrefs.SetInt(BestScoreKey, score);
                PlayerPrefs.Save();
            }

            _placementManager?.SetGameActive(false);
            _conveyorManager?.SetGameActive(false);
            _receivingManager?.SetGameActive(false);
            _shippingManager?.SetGameActive(false);
            _weightManager?.SetGameActive(false);
            _receivingBuildingPlacer?.SetGameActive(false);
        }

        /// <summary>
        /// Wires the Receiving-building placer (#51) so the run's end halts it like every other
        /// placer. Called once by Bootstrapper right after this manager is created.
        /// </summary>
        public void SetReceivingBuildingPlacer(ReceivingBuildingPlacer receivingBuildingPlacer)
        {
            _receivingBuildingPlacer = receivingBuildingPlacer;
        }

        /// <summary>
        /// Starts a fresh run by reloading the active scene (#43). Every manager, HUD, and placed
        /// piece is scene-lifetime (no DontDestroyOnLoad), so the reload destroys all of it, and the
        /// scene's own Bootstrapper builds a clean set, including restoring platform segments that
        /// collapsed (#14). The reload completes at end of frame, so callers must not touch any
        /// manager after calling this.
        /// </summary>
        public void Restart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
