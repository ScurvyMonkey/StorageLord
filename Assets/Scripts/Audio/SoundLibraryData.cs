using UnityEngine;

namespace StorageLord.Audio
{
    /// <summary>
    /// Tunable set of one-shot and looping clips SoundManager plays (#29) — sourced from the
    /// already-imported Universal Sound FX pack. voiceLines is deliberately reserved and unused —
    /// no delivery mechanism exists yet, so recording real VO later is a content-only addition to
    /// this asset, not an architecture change.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Sound Library Data")]
    public class SoundLibraryData : ScriptableObject
    {
        [Header("Placement")]
        public AudioClip placementConfirmClip;
        public AudioClip placementRejectClip;

        [Header("Orders")]
        public AudioClip orderFulfilledClip;
        public AudioClip orderMissedClip;

        [Header("Belt")]
        [Tooltip("Looping hum played on a single shared AudioSource while any belt has goods moving.")]
        public AudioClip beltLoopClip;
        [Range(0f, 1f)] public float beltMinVolume = 0.1f;
        [Range(0f, 1f)] public float beltMaxVolume = 0.6f;
        [Tooltip("Active-goods count at which the belt loop reaches beltMaxVolume.")]
        [Min(1)] public int beltFullVolumeGoodsCount = 10;

        [Header("Voice (reserved, none recorded yet)")]
        [Tooltip("Reserved for future voice-over lines -- no playback mechanism exists yet.")]
        public AudioClip[] voiceLines;
    }
}
