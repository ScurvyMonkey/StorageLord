using UnityEngine;

namespace StorageLord.Docks
{
    /// <summary>
    /// Marks a scene GameObject as a fixed Receiving dock — hand-placed once in the Editor, not
    /// through the runtime placement system, per the GDD's "fixed dock" design (resolved via /ba
    /// for #6 rather than assumed). ReceivingManager finds all instances at startup and spawns
    /// goods onto each one's output cell.
    /// </summary>
    public class ReceivingDock : MonoBehaviour
    {
        /// <summary>
        /// Returns the cardinal direction this dock currently faces (its local forward, rounded to
        /// the nearest grid axis) — the output cell is one cell away from the dock's own cell in
        /// this direction. Rotate this GameObject in 90° increments in the Inspector to change it,
        /// matching the project's rotation-snap convention.
        /// </summary>
        public Vector3Int GetOutputDirection()
        {
            Vector3 forward = transform.forward;
            return new Vector3Int(Mathf.RoundToInt(forward.x), 0, Mathf.RoundToInt(forward.z));
        }
    }
}
