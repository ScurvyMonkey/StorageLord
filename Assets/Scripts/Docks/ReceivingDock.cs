using UnityEngine;

namespace StorageLord.Docks
{
    /// <summary>
    /// Marks a GameObject as a Receiving dock. The starting dock is fixed — hand-placed once in the
    /// Editor, per the GDD's "fixed dock" design (#6) — and found by ReceivingManager at startup;
    /// extra Receiving buildings the player buys (#51) carry the same component and are registered
    /// at runtime by ReceivingBuildingPlacer. Either way, ReceivingManager spawns goods onto each
    /// one's output cell.
    /// </summary>
    public class ReceivingDock : MonoBehaviour
    {
        [Tooltip("The child transform goods should visually connect at — e.g. the embedded " +
                 "TurretPlatformFlyingX piece on a hand-built compound dock prefab. The dock's own " +
                 "registered cell is derived from this point's world position, not the root's, so " +
                 "the belt hand-off lines up with wherever the artist actually placed the connector " +
                 "inside the prefab. Falls back to this GameObject's own transform if left unassigned.")]
        [SerializeField] private Transform connectionPoint;

        /// <summary>
        /// The world-space point ReceivingManager should treat as this dock's own cell position —
        /// the assigned connectionPoint if one exists, otherwise this dock's own transform.
        /// </summary>
        public Transform ConnectionPoint => connectionPoint != null ? connectionPoint : transform;

        /// <summary>
        /// True for a Receiving building the player bought and placed at runtime (#51), false for
        /// the fixed, Editor-placed starting dock — only player-placed ones can be removed, and only
        /// they count toward the next building's price.
        /// </summary>
        public bool IsPlayerPlaced { get; private set; }

        /// <summary>
        /// Marks this dock as a player-placed Receiving building (#51). Called once by
        /// ReceivingBuildingPlacer right after instantiating it.
        /// </summary>
        public void MarkPlayerPlaced()
        {
            IsPlayerPlaced = true;
        }

        /// <summary>
        /// Returns the cardinal direction this dock currently faces (its local forward, rounded to
        /// the nearest grid axis) — the output cell is one cell away from ConnectionPoint in this
        /// direction. Rotate this GameObject in 90° increments in the Inspector to change it,
        /// matching the project's rotation-snap convention.
        /// </summary>
        public Vector3Int GetOutputDirection()
        {
            Vector3 forward = transform.forward;
            return new Vector3Int(Mathf.RoundToInt(forward.x), 0, Mathf.RoundToInt(forward.z));
        }
    }
}
