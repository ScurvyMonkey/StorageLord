using UnityEngine;

namespace StorageLord.Storage
{
    /// <summary>
    /// Tunable weight capacity for a platform floor segment (#14) — one shared asset for now, since
    /// every current platform piece (SpacePlatformLargeBlue) is the same type. Per-piece-type
    /// capacity variation is a natural future extension (a new PlatformSegmentData per piece type),
    /// not needed yet.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Platform Segment Data", fileName = "PlatformSegmentData")]
    public class PlatformSegmentData : ScriptableObject
    {
        [Tooltip("Combined weight (kg) of goods stored in containers on this segment before it " +
                 "fails, destroying everything built on it.")]
        [Min(0.01f)]
        public float capacityKg = 100f;
    }
}
