using System;
using UnityEngine;

namespace StorageLord.Placement
{
    /// <summary>
    /// ScriptableObject event channel raised whenever a piece is successfully placed on the grid.
    /// Per CLAUDE.md's Event-Driven Communication convention, cross-system broadcasts like this use
    /// an event channel rather than a direct manager-to-manager reference — no listener exists yet,
    /// but StorageManager will subscribe once it exists.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Placement Event Channel", fileName = "PlacementEventChannel")]
    public class PlacementEventChannel : ScriptableObject
    {
        /// <summary>
        /// Raised after a piece is instantiated and anchored, with the placed instance and the
        /// 3D grid cell it occupies.
        /// </summary>
        public event Action<GameObject, Vector3Int> OnPiecePlaced;

        /// <summary>
        /// Raises <see cref="OnPiecePlaced"/> for the given placed instance and cell.
        /// </summary>
        public void RaisePiecePlaced(GameObject placedInstance, Vector3Int cell)
        {
            OnPiecePlaced?.Invoke(placedInstance, cell);
        }
    }
}
