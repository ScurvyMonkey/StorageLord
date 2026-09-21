using UnityEngine;

namespace StorageLord.Core
{
    /// <summary>
    /// Tunable rules governing when a run ends. Deliberately minimal — just the one rule #12
    /// actually needs (a hard loss condition on missed orders), same pattern as every other
    /// *Data ScriptableObject in this project.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Game Rules Data", fileName = "GameRulesData")]
    public class GameRulesData : ScriptableObject
    {
        [Tooltip("How many missed orders end the run.")]
        [Min(1)]
        public int maxMissedOrders = 10;
    }
}
