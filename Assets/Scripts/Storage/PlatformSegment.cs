using UnityEngine;

namespace StorageLord.Storage
{
    /// <summary>
    /// Marks a placed platform floor piece as a weight-tracked segment (#14) and carries its
    /// capacity data. WeightManager finds every instance of this at startup and resolves which
    /// segment owns a given grid cell by measuring the instance's own real Renderer bounds (X/Z)
    /// rather than assuming a footprint-in-cells formula — real SpacePlatformKit piece spacing
    /// doesn't necessarily divide as cleanly as the nominal cell math would suggest (per /arch's own
    /// note on #14), and this project's established practice is to measure real geometry directly
    /// rather than guess it (see PATTERNS.md's repeated precedent from the #10/#11 conveyor and
    /// platform work). Purely a marker + data reference — no behavior of its own.
    /// </summary>
    public class PlatformSegment : MonoBehaviour
    {
        [SerializeField] private PlatformSegmentData data;

        /// <summary>This segment's capacity data.</summary>
        public PlatformSegmentData Data => data;
    }
}
