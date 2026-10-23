using UnityEngine;

namespace StorageLord.Core
{
    /// <summary>
    /// Tunable rules for a run: when it ends (#12) and how it's scored (#43, endless
    /// score-attack). Same pattern as every other *Data ScriptableObject in this project.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Game Rules Data", fileName = "GameRulesData")]
    public class GameRulesData : ScriptableObject
    {
        [Tooltip("How many missed orders end the run.")]
        [Min(1)]
        public int maxMissedOrders = 10;

        [Tooltip("Score awarded per unit of a fulfilled order, for any tier (#43). A fulfilled order scores pointsPerUnit × its requiredQuantity; missed orders score nothing.")]
        [Min(0)]
        public int pointsPerUnit = 10;
    }
}
