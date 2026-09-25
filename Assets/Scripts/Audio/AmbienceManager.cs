using UnityEngine;

namespace StorageLord.Audio
{
    /// <summary>
    /// Owns the slow environment mood cycle (#29) — every AmbienceData.cycleIntervalSeconds,
    /// advances to the next preset in order (sequential with wraparound, not shuffled) and swaps
    /// the skybox and ambience track together, so the two always change as one cohesive shift
    /// rather than two independently-ticking timers. The skybox swap is an instant cut
    /// (RenderSettings.skybox, matching the already-imported SpaceSkies 2 pack's own demo
    /// mechanism, confirmed live to render correctly under this project's URP setup) — only the
    /// ambience audio actually crossfades, via SoundManager.PlayAmbience.
    ///
    /// Deliberately does not touch RenderSettings.skybox on its own first frame — the scene already
    /// has a real skybox configured (confirmed live: SampleScene's own Lighting settings already
    /// point at a SpaceSkies 2 material), so forcing an immediate swap the instant Play Mode starts
    /// would silently override that existing choice. The cycle timer starts immediately, so the
    /// first real skybox change happens at the first scheduled interval, same as every later one.
    /// Ambience audio has no equivalent pre-existing state to preserve, so it starts playing
    /// (crossfading in from silence) immediately on Start().
    ///
    /// Created and wired by Bootstrapper, after SoundManager (needs a live reference to it) — not
    /// placed directly in a scene, since it has no serialized Inspector fields to wire (its data
    /// reference is injected via Initialize()).
    /// </summary>
    public class AmbienceManager : MonoBehaviour
    {
        private AmbienceData _data;
        private SoundManager _soundManager;

        private int _currentPresetIndex = -1;
        private float _elapsedSeconds;

        /// <summary>Index into AmbienceData.presets of the currently active environment.</summary>
        public int CurrentPresetIndex => _currentPresetIndex;

        /// <summary>
        /// Injects this manager's data/manager references. Called once by Bootstrapper immediately
        /// after creation.
        /// </summary>
        public void Initialize(AmbienceData data, SoundManager soundManager)
        {
            _data = data;
            _soundManager = soundManager;
        }

        /// <summary>
        /// Starts the ambience track for the first preset immediately, without touching the
        /// skybox — see this class's own doc comment for why the two aren't symmetric on startup.
        /// </summary>
        private void Start()
        {
            if (!HasPresets())
            {
                return;
            }

            _currentPresetIndex = 0;
            AmbiencePreset preset = _data.presets[0];
            _soundManager?.PlayAmbience(preset.ambienceClip, _data.ambienceCrossfadeSeconds, _data.ambienceVolume);
        }

        /// <summary>
        /// Advances the environment cycle timer, swapping to the next preset (skybox and ambience
        /// together) once cycleIntervalSeconds has elapsed.
        /// </summary>
        private void Update()
        {
            if (!HasPresets())
            {
                return;
            }

            _elapsedSeconds += Time.deltaTime;
            if (_elapsedSeconds < _data.cycleIntervalSeconds)
            {
                return;
            }

            _elapsedSeconds = 0f;
            AdvancePreset();
        }

        private bool HasPresets()
        {
            return _data != null && _data.presets != null && _data.presets.Length > 0;
        }

        /// <summary>
        /// Moves to the next preset in order (wrapping around), swapping the skybox instantly and
        /// starting an ambience crossfade to match.
        /// </summary>
        private void AdvancePreset()
        {
            _currentPresetIndex = (_currentPresetIndex + 1) % _data.presets.Length;
            AmbiencePreset preset = _data.presets[_currentPresetIndex];

            if (preset.skybox != null)
            {
                RenderSettings.skybox = preset.skybox;
            }

            _soundManager?.PlayAmbience(preset.ambienceClip, _data.ambienceCrossfadeSeconds, _data.ambienceVolume);
        }
    }
}
