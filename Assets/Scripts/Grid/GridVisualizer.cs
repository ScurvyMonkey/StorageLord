using UnityEngine;

namespace StorageLord.Grid
{
    /// <summary>
    /// Draws the shared placement grid as Scene view gizmos so pieces can be visually checked
    /// against it. Editor-only visualization — draws nothing in a built player.
    /// </summary>
    public class GridVisualizer : MonoBehaviour
    {
        [SerializeField]
        private GridConfig gridConfig;

        [Tooltip("How many cells out from this object's position to draw lines, in each direction.")]
        [SerializeField]
        [Min(1)]
        private int extentInCells = 25;

        [SerializeField]
        private Color lineColor = new Color(0f, 1f, 1f, 0.35f);

#if UNITY_EDITOR
        /// <summary>
        /// Draws the grid lines in the Scene view, centered on this object's position.
        /// </summary>
        private void OnDrawGizmos()
        {
            if (gridConfig == null || gridConfig.cellSize <= 0f)
            {
                return;
            }

            Gizmos.color = lineColor;
            float cellSize = gridConfig.cellSize;
            float half = extentInCells * cellSize;
            Vector3 origin = transform.position;

            for (int i = -extentInCells; i <= extentInCells; i++)
            {
                float offset = i * cellSize;
                Gizmos.DrawLine(origin + new Vector3(offset, 0f, -half), origin + new Vector3(offset, 0f, half));
                Gizmos.DrawLine(origin + new Vector3(-half, 0f, offset), origin + new Vector3(half, 0f, offset));
            }
        }
#endif
    }
}
