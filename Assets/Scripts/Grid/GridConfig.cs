using UnityEngine;

namespace StorageLord.Grid
{
    /// <summary>
    /// Tunable definition of the shared placement grid — cell size and rotation snap increment.
    /// Every placeable in Storage Lord (platform modules now, containers/conveyors later) snaps
    /// to this same grid, so there should be exactly one GridConfig asset in the project.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Grid Config", fileName = "GridConfig")]
    public class GridConfig : ScriptableObject
    {
        [Tooltip("World-space size of one grid cell, in meters. This default is a placeholder, " +
                 "not a measured value — verify it against SpacePlatformKit piece footprints using " +
                 "the grid gizmo (GridVisualizer) or the Platform Grid Placer window, and adjust " +
                 "until pieces align cleanly.")]
        [Min(0.01f)]
        public float cellSize = 4f;

        [Tooltip("Rotation snap increment in degrees, applied around the world Y axis.")]
        [Min(1f)]
        public float rotationSnapDegrees = 90f;

        [Tooltip("Vertical size of one grid level, in meters. Kept separate from cellSize since " +
                 "floor-to-floor height and horizontal tile width aren't necessarily equal. Same " +
                 "placeholder-not-measured caveat as cellSize applies.")]
        [Min(0.01f)]
        public float cellHeight = 3f;

        /// <summary>
        /// Snaps a world-space position to the nearest cell center on the X/Z grid plane, preserving Y.
        /// </summary>
        public Vector3 SnapPosition(Vector3 worldPosition)
        {
            float snappedX = Mathf.Round(worldPosition.x / cellSize) * cellSize;
            float snappedZ = Mathf.Round(worldPosition.z / cellSize) * cellSize;
            return new Vector3(snappedX, worldPosition.y, snappedZ);
        }

        /// <summary>
        /// Snaps a Y-axis rotation angle, in degrees, to the nearest rotation snap increment.
        /// </summary>
        public float SnapYRotation(float yAngleDegrees)
        {
            return Mathf.Round(yAngleDegrees / rotationSnapDegrees) * rotationSnapDegrees;
        }

        /// <summary>
        /// Converts a world-space position to its integer grid cell coordinate on the X/Z plane.
        /// </summary>
        public Vector2Int WorldToCell(Vector3 worldPosition)
        {
            int cellX = Mathf.RoundToInt(worldPosition.x / cellSize);
            int cellZ = Mathf.RoundToInt(worldPosition.z / cellSize);
            return new Vector2Int(cellX, cellZ);
        }

        /// <summary>
        /// Converts an integer grid cell coordinate to its world-space cell-center position at the given height.
        /// </summary>
        public Vector3 CellToWorld(Vector2Int cell, float worldY)
        {
            return new Vector3(cell.x * cellSize, worldY, cell.y * cellSize);
        }

        /// <summary>
        /// Snaps a world-space position to the nearest cell center on all three axes: X/Z to
        /// cellSize, Y to cellHeight. Used by the runtime placement system, which places at
        /// variable height — the 2D-only SnapPosition above stays untouched for the Editor tool.
        /// </summary>
        public Vector3 SnapPosition3D(Vector3 worldPosition)
        {
            float snappedX = Mathf.Round(worldPosition.x / cellSize) * cellSize;
            float snappedY = Mathf.Round(worldPosition.y / cellHeight) * cellHeight;
            float snappedZ = Mathf.Round(worldPosition.z / cellSize) * cellSize;
            return new Vector3(snappedX, snappedY, snappedZ);
        }

        /// <summary>
        /// Converts a world-space position to its integer 3D grid cell coordinate — X/Z are
        /// horizontal cell indices (in cellSize units), Y is the vertical height level (in
        /// cellHeight units, not raw world Y).
        /// </summary>
        public Vector3Int WorldToCell3D(Vector3 worldPosition)
        {
            int cellX = Mathf.RoundToInt(worldPosition.x / cellSize);
            int heightLevel = Mathf.RoundToInt(worldPosition.y / cellHeight);
            int cellZ = Mathf.RoundToInt(worldPosition.z / cellSize);
            return new Vector3Int(cellX, heightLevel, cellZ);
        }

        /// <summary>
        /// Converts an integer 3D grid cell coordinate (X/Z as horizontal indices, Y as height
        /// level) to its world-space cell-center position.
        /// </summary>
        public Vector3 Cell3DToWorld(Vector3Int cell)
        {
            return new Vector3(cell.x * cellSize, cell.y * cellHeight, cell.z * cellSize);
        }
    }
}
