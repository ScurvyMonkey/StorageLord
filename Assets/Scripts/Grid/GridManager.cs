using System.Collections.Generic;
using UnityEngine;

namespace StorageLord.Grid
{
    /// <summary>
    /// Single shared runtime registry of which 3D grid cells are occupied, so every runtime
    /// placement system (PlacementManager, ConveyorManager) validates against the same occupancy
    /// state instead of maintaining independent, unaware-of-each-other registries — introduced by
    /// #4 once a second placer (conveyors) needed to coexist with the first (containers). Each
    /// placer still keeps its own local piece dictionary for hit-testing/removal; this manager only
    /// answers "is this cell taken at all."
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        private readonly HashSet<Vector3Int> _occupiedCells = new HashSet<Vector3Int>();

        /// <summary>
        /// Returns true if the given 3D grid cell is already occupied by any placed piece.
        /// </summary>
        public bool IsOccupied(Vector3Int cell)
        {
            return _occupiedCells.Contains(cell);
        }

        /// <summary>
        /// Returns the height level one above the highest occupied cell in the given X/Z column, or
        /// 0 (the deck) if the column is empty. Used by support-required auto-stack placement.
        /// </summary>
        public int NextFreeHeightLevel(Vector2Int column)
        {
            int nextLevel = 0;
            foreach (Vector3Int occupied in _occupiedCells)
            {
                if (occupied.x == column.x && occupied.z == column.y)
                {
                    nextLevel = Mathf.Max(nextLevel, occupied.y + 1);
                }
            }

            return nextLevel;
        }

        /// <summary>
        /// Marks the given cell as occupied.
        /// </summary>
        public void Register(Vector3Int cell)
        {
            _occupiedCells.Add(cell);
        }

        /// <summary>
        /// Clears the given cell's occupied status, if it was registered.
        /// </summary>
        public void Unregister(Vector3Int cell)
        {
            _occupiedCells.Remove(cell);
        }
    }
}
