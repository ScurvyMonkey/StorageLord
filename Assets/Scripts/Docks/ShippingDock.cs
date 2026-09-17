using UnityEngine;

namespace StorageLord.Docks
{
    /// <summary>
    /// Marks a scene GameObject as a fixed Shipping dock — hand-placed once in the Editor, not
    /// through the runtime placement system, mirroring ReceivingDock (#6). ShippingManager finds
    /// all instances at startup and consumes goods arriving at each one's input cell toward
    /// whatever active order needs them.
    /// </summary>
    public class ShippingDock : MonoBehaviour
    {
        [Tooltip("The child transform goods should visually connect at — e.g. the embedded " +
                 "TurretPlatformFlyingX piece on a hand-built compound dock prefab. The dock's own " +
                 "registered cell is derived from this point's world position, not the root's, so " +
                 "the belt hand-off lines up with wherever the artist actually placed the connector " +
                 "inside the prefab. Falls back to this GameObject's own transform if left unassigned.")]
        [SerializeField] private Transform connectionPoint;

        /// <summary>
        /// The world-space point ShippingManager should treat as this dock's own cell position —
        /// the assigned connectionPoint if one exists, otherwise this dock's own transform.
        /// </summary>
        public Transform ConnectionPoint => connectionPoint != null ? connectionPoint : transform;

        /// <summary>
        /// Returns the cardinal direction this dock currently faces (its local forward, rounded to
        /// the nearest grid axis) — the input cell is one cell away from ConnectionPoint in this
        /// direction. Rotate this GameObject in 90° increments in the Inspector to change which way
        /// it accepts deliveries from.
        /// </summary>
        public Vector3Int GetInputDirection()
        {
            Vector3 forward = transform.forward;
            return new Vector3Int(Mathf.RoundToInt(forward.x), 0, Mathf.RoundToInt(forward.z));
        }
    }
}
