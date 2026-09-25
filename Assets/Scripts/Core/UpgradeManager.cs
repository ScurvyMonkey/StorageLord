using UnityEngine;

namespace StorageLord.Core
{
    /// <summary>
    /// Owns the run's two global, retroactive upgrade tracks (#27) — conveyor belt speed and
    /// platform weight capacity — each a simple purchased-tier count against a fixed, data-defined
    /// tier list (UpgradeData). Both tracks start at tier 0 (nothing purchased, base stats apply).
    /// Purchasing spends money via ScoreManager.TrySpend — the one spend path both tracks share.
    /// Multipliers are read live by ConveyorManager/WeightManager wherever the underlying stat is
    /// used (belt movement speed, segment capacity checks) rather than baked into any per-piece
    /// state, so a purchase instantly affects every belt/segment already placed, not just future
    /// ones.
    ///
    /// Created and wired by Bootstrapper, after ScoreManager (needs a live reference for TrySpend) —
    /// not placed directly in a scene, since it has no serialized Inspector fields to wire (its data
    /// references are injected via Initialize()).
    /// </summary>
    public class UpgradeManager : MonoBehaviour
    {
        private UpgradeData _upgradeData;
        private ScoreManager _scoreManager;

        private int _conveyorTierIndex;
        private int _platformTierIndex;

        /// <summary>How many conveyor speed tiers have been purchased so far.</summary>
        public int ConveyorTierIndex => _conveyorTierIndex;

        /// <summary>Total number of conveyor speed tiers available.</summary>
        public int ConveyorTierCount => _upgradeData?.conveyorSpeedTiers?.Length ?? 0;

        /// <summary>How many platform capacity tiers have been purchased so far.</summary>
        public int PlatformTierIndex => _platformTierIndex;

        /// <summary>Total number of platform capacity tiers available.</summary>
        public int PlatformTierCount => _upgradeData?.platformCapacityTiers?.Length ?? 0;

        /// <summary>
        /// The multiplier ConveyorManager applies to ConveyorData.beltSpeed — 1 (no change) until the
        /// first conveyor tier is purchased, then the most recently purchased tier's multiplier.
        /// </summary>
        public float ConveyorSpeedMultiplier =>
            _conveyorTierIndex <= 0 || _upgradeData?.conveyorSpeedTiers == null
                ? 1f
                : _upgradeData.conveyorSpeedTiers[_conveyorTierIndex - 1].multiplier;

        /// <summary>
        /// The multiplier WeightManager applies to PlatformSegmentData.capacityKg — 1 (no change)
        /// until the first platform tier is purchased, then the most recently purchased tier's
        /// multiplier.
        /// </summary>
        public float PlatformCapacityMultiplier =>
            _platformTierIndex <= 0 || _upgradeData?.platformCapacityTiers == null
                ? 1f
                : _upgradeData.platformCapacityTiers[_platformTierIndex - 1].multiplier;

        /// <summary>
        /// The next conveyor tier's cost, or null if the track is already at max tier — read by
        /// UpgradeHUD to show "MAX" once exhausted.
        /// </summary>
        public int? NextConveyorCost =>
            _upgradeData?.conveyorSpeedTiers != null && _conveyorTierIndex < _upgradeData.conveyorSpeedTiers.Length
                ? _upgradeData.conveyorSpeedTiers[_conveyorTierIndex].costMoney
                : (int?)null;

        /// <summary>
        /// The next platform tier's cost, or null if the track is already at max tier — read by
        /// UpgradeHUD to show "MAX" once exhausted.
        /// </summary>
        public int? NextPlatformCost =>
            _upgradeData?.platformCapacityTiers != null && _platformTierIndex < _upgradeData.platformCapacityTiers.Length
                ? _upgradeData.platformCapacityTiers[_platformTierIndex].costMoney
                : (int?)null;

        /// <summary>
        /// Injects this manager's data/manager references. Called once by Bootstrapper immediately
        /// after creation.
        /// </summary>
        public void Initialize(UpgradeData upgradeData, ScoreManager scoreManager)
        {
            _upgradeData = upgradeData;
            _scoreManager = scoreManager;
        }

        /// <summary>
        /// Attempts to purchase the next conveyor speed tier — fails (no-op, returns false) if
        /// already at max tier or the player can't afford it (ScoreManager.TrySpend itself rejects
        /// insufficient funds). Succeeds by deducting the cost and advancing the tier index by one.
        /// </summary>
        public bool TryPurchaseNextConveyorTier()
        {
            if (_upgradeData?.conveyorSpeedTiers == null || _conveyorTierIndex >= _upgradeData.conveyorSpeedTiers.Length || _scoreManager == null)
            {
                return false;
            }

            UpgradeData.UpgradeTier nextTier = _upgradeData.conveyorSpeedTiers[_conveyorTierIndex];
            if (!_scoreManager.TrySpend(nextTier.costMoney))
            {
                return false;
            }

            _conveyorTierIndex++;
            return true;
        }

        /// <summary>
        /// Attempts to purchase the next platform capacity tier — same shape as
        /// TryPurchaseNextConveyorTier, applied to the independent platform-capacity track.
        /// </summary>
        public bool TryPurchaseNextPlatformTier()
        {
            if (_upgradeData?.platformCapacityTiers == null || _platformTierIndex >= _upgradeData.platformCapacityTiers.Length || _scoreManager == null)
            {
                return false;
            }

            UpgradeData.UpgradeTier nextTier = _upgradeData.platformCapacityTiers[_platformTierIndex];
            if (!_scoreManager.TrySpend(nextTier.costMoney))
            {
                return false;
            }

            _platformTierIndex++;
            return true;
        }
    }
}
