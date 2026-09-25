using UnityEngine;

namespace StorageLord.Audio
{
    /// <summary>
    /// One environment mood a station can cycle to (#29) — pairs a skybox material (from the
    /// already-imported SpaceSkies 2 pack) with the ambience track that plays while it's active.
    /// </summary>
    [System.Serializable]
    public struct AmbiencePreset
    {
        public Material skybox;
        public AudioClip ambienceClip;
    }

    /// <summary>
    /// Tunable ordered list of environment presets AmbienceManager cycles through (#29), plus the
    /// cycle cadence and ambience crossfade tuning. Presets cycle sequentially with wraparound, not
    /// shuffled — simplest, most predictable behavior.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Ambience Data")]
    public class AmbienceData : ScriptableObject
    {
        public AmbiencePreset[] presets;

        [Min(1f)] public float cycleIntervalSeconds = 180f;
        [Min(0f)] public float ambienceCrossfadeSeconds = 3f;
        [Range(0f, 1f)] public float ambienceVolume = 0.4f;
    }
}
