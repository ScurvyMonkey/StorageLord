using UnityEngine;

namespace StorageLord.Goods
{
    /// <summary>
    /// A real, physical in-transit good — not a shader/UV-scroll illusion. Holds its defining
    /// GoodsData and the grid cell it currently occupies; ConveyorManager drives its movement
    /// between cells each frame, this component just carries the state.
    /// </summary>
    public class GoodsAgent : MonoBehaviour
    {
        /// <summary>
        /// The GoodsData this instance was spawned from.
        /// </summary>
        public GoodsData Data { get; private set; }

        /// <summary>
        /// The 3D grid cell this good currently occupies (or is arriving at, while mid-move).
        /// </summary>
        public Vector3Int CurrentCell { get; private set; }

        /// <summary>
        /// Initializes this agent's data and starting cell. Called once, immediately after
        /// instantiation, by whatever spawns it (ConveyorManager's debug spawn action for now).
        /// </summary>
        public void Initialize(GoodsData data, Vector3Int startCell)
        {
            Data = data;
            CurrentCell = startCell;
        }

        /// <summary>
        /// Updates the cell this agent is considered to occupy, once it has physically arrived
        /// there. Does not move the transform itself — ConveyorManager handles that.
        /// </summary>
        public void SetCurrentCell(Vector3Int cell)
        {
            CurrentCell = cell;
        }
    }
}
