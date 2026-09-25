using UnityEngine;

namespace StorageLord.Core
{
    /// <summary>
    /// Tunes the two global, retroactive upgrade tracks UpgradeManager owns (#27) — conveyor belt
    /// speed and platform weight capacity. Each track is a fixed, ordered list of tiers; a tier's
    /// multiplier applies to every belt/segment on the station at once the instant it's purchased,
    /// not just future ones. Seed values are placeholders (mirroring #8's own 30/60/90/180 PPM
    /// example as 1x/2x/3x/6x multipliers) — tune once real playtesting establishes a real economy
    /// curve against Random/Special order rewards.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Upgrade Data", fileName = "UpgradeData")]
    public class UpgradeData : ScriptableObject
    {
        /// <summary>One purchasable step on an upgrade track.</summary>
        [System.Serializable]
        public struct UpgradeTier
        {
            [Tooltip("Multiplier applied to the base stat once this tier is purchased.")]
            public float multiplier;

            [Tooltip("Money cost to purchase this tier.")]
            [Min(0)]
            public int costMoney;
        }

        [Tooltip("Ordered conveyor belt-speed tiers, cheapest/lowest first.")]
        public UpgradeTier[] conveyorSpeedTiers =
        {
            new UpgradeTier { multiplier = 2f, costMoney = 40 },
            new UpgradeTier { multiplier = 3f, costMoney = 120 },
            new UpgradeTier { multiplier = 6f, costMoney = 300 }
        };

        [Tooltip("Ordered platform weight-capacity tiers, cheapest/lowest first.")]
        public UpgradeTier[] platformCapacityTiers =
        {
            new UpgradeTier { multiplier = 1.5f, costMoney = 60 },
            new UpgradeTier { multiplier = 2f, costMoney = 180 },
            new UpgradeTier { multiplier = 3f, costMoney = 450 }
        };
    }
}
