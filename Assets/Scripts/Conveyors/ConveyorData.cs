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
        public GameObject prefab;

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
