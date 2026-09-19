using UnityEngine;

namespace StorageLord.Conveyors
{
    /// <summary>
    /// Tunable definition of a conveyor segment: its placeable visual and belt speed. Same minimal
    /// ScriptableObject-first pattern as ContainerData/GoodsData.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Conveyor Data", fileName = "ConveyorData")]
    public class ConveyorData : ScriptableObject
    {
        [Tooltip("The piece shown at a cell that's a real topological anchor (#10 follow-up) — a " +
                 "genuine start, end, junction, or bend, derived from the whole network's current " +
                 "flow graph and recomputed after every placement/removal, not just 'wherever a " +
                 "drag happened to start/end.' A real, functional placeable, not decoration — every " +
                 "cell a drag touches is still individually registered with GridManager/flow-" +
                 "direction tracking regardless of which prefab renders there.")]
        public GameObject beltPlatformPrefab;

        [Tooltip("The fill piece instantiated once per flow-connected cell-gap that isn't spanned " +
                 "by an anchor at both ends (#10 follow-up) — scaled along its own local Z per edge " +
                 "to the real open gap (a BeltPlatform anchor's fins eat into the gap on its side; " +
                 "a plain fill cell doesn't), not used at a fixed, unscaled length everywhere.")]
        public GameObject beltSystemPrefab;

        [Tooltip("How many grid cells a good on this belt travels per second.")]
        [Min(0.01f)]
        public float beltSpeed = 2f;

        [Tooltip("How far above the deck surface this segment hovers, in meters — the 'floating " +
                 "conveyor' look. 0 sits flush on the deck.")]
        [Min(0f)]
        public float hoverHeight = 0.3f;

        [Tooltip("How far above the segment's own hover position goods ride, in meters — measured " +
                 "to match TurretPlatformFlyingBlue's own top surface (0.32) so a good's base-pivoted " +
                 "prefab (e.g. Pallet1) sits flush on top of the segment rather than sunk inside it.")]
        [Min(0f)]
        public float goodsRideHeight = 0.32f;
    }
}
