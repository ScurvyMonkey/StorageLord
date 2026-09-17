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
        /// Returns the lowest unoccupied height level in the given X/Z column, walking up from 0 —
        /// 0 itself if the deck is free, or the first gap above whatever's already there. Used by
        /// support-required auto-stack placement. Deliberately gap-aware rather than "highest
        /// occupied + 1" (#7): once multi-level conveyors can occupy a column non-contiguously
        /// (e.g. a belt flying over an empty deck cell), "highest + 1" would skip right past a
        /// genuinely free cell below it, silently blocking a container from ever landing there.
        /// For any column that's still contiguous from 0 (every column containing only containers,
        /// which is every column before #7) this returns exactly what the old formula did.
        /// </summary>
        public int NextFreeHeightLevel(Vector2Int column)
        {
            int level = 0;
            while (IsOccupied(new Vector3Int(column.x, level, column.y)))
            {
                level++;
            }

            return level;
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
