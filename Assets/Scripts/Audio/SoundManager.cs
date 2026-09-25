using System.Collections;
using StorageLord.Docks;
using StorageLord.Placement;
using UnityEngine;

namespace StorageLord.Audio
{
    /// <summary>
    /// General-purpose audio playback utility (#29) — any system can trigger a one-shot cue or
    /// report belt activity; AmbienceManager drives the looping ambience track through this too, so
    /// every audio source in the game lives in one place. Subscribes directly to
    /// PlacementEventChannel.OnPiecePlaced and OrderEventChannel.OnOrderFulfilled/OnOrderMissed for
    /// three of its four one-shot cues, needing zero new call sites in PlacementManager,
    /// ConveyorManager's confirm path, ScoreManager, or GameManager — the same direct-subscription
    /// pattern ScoreManager/GameManager already use for those exact channels. The one genuinely new
    /// call site is ConveyorManager.ConfirmDrag's rejection branch (PlayPlacementReject) — nothing
    /// else raises an event on a rejected placement today.
    ///
    /// Every one-shot cue plays on its own dedicated, non-looping AudioSource via Play() (restart
    /// semantics), never PlayOneShot — a single conveyor drag confirms via OnPiecePlaced once PER
    /// CELL, so a 4-cell drag fires the confirm handler 4 times in one frame; PlayOneShot would
    /// stack 4 overlapping instances into an audible stutter, while Play() just restarts the same
    /// source each time, collapsing to one audible instance by the time anything actually renders
    /// (#29 arch condition).
    ///
    /// Created and wired by Bootstrapper — not placed directly in a scene, since it has no
    /// serialized Inspector fields to wire (its data/event-channel references are injected via
    /// Initialize()).
    /// </summary>
    public class SoundManager : MonoBehaviour
    {
        private SoundLibraryData _library;
        private PlacementEventChannel _placementEventChannel;
        private OrderEventChannel _orderEventChannel;

        private AudioSource _confirmSource;
        private AudioSource _rejectSource;
        private AudioSource _orderFulfilledSource;
        private AudioSource _orderMissedSource;
        private AudioSource _beltLoopSource;
        private AudioSource _ambienceSourceA;
        private AudioSource _ambienceSourceB;
        private bool _ambienceUsingSourceA = true;
        private Coroutine _ambienceCrossfadeCoroutine;

        /// <summary>
        /// Creates every dedicated AudioSource this manager will ever use — one per cue, plus two
        /// alternating sources for ambience crossfading. None are created/destroyed later; only
        /// their clip/volume/play-state change.
        /// </summary>
        private void Awake()
        {
            _confirmSource = CreateSource(loop: false);
            _rejectSource = CreateSource(loop: false);
            _orderFulfilledSource = CreateSource(loop: false);
            _orderMissedSource = CreateSource(loop: false);
            _beltLoopSource = CreateSource(loop: true);
            _ambienceSourceA = CreateSource(loop: true);
            _ambienceSourceB = CreateSource(loop: true);
        }

        private AudioSource CreateSource(bool loop)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        /// <summary>
        /// Injects this manager's data/event-channel references and subscribes to the events that
        /// drive its cues. Called once by Bootstrapper immediately after creation — deliberately not
        /// relying on OnEnable() alone, since Bootstrapper creates this manager via AddComponent(),
        /// which fires OnEnable() synchronously before Initialize() has set these references (see
        /// CLAUDE.md's Camera.main precedent for the same pitfall).
        /// </summary>
        public void Initialize(SoundLibraryData library, PlacementEventChannel placementEventChannel, OrderEventChannel orderEventChannel)
        {
            _library = library;
            _placementEventChannel = placementEventChannel;
            _orderEventChannel = orderEventChannel;

            if (_placementEventChannel != null)
            {
                _placementEventChannel.OnPiecePlaced -= HandlePiecePlaced;
                _placementEventChannel.OnPiecePlaced += HandlePiecePlaced;
            }

            if (_orderEventChannel != null)
            {
                _orderEventChannel.OnOrderFulfilled -= HandleOrderFulfilled;
                _orderEventChannel.OnOrderFulfilled += HandleOrderFulfilled;
                _orderEventChannel.OnOrderMissed -= HandleOrderMissed;
                _orderEventChannel.OnOrderMissed += HandleOrderMissed;
            }
        }

        /// <summary>
        /// Subscribes to this manager's events — a no-op on the very first enable (Initialize()
        /// hasn't set the channel references yet), but correct for any later disable/re-enable cycle
        /// once it has.
        /// </summary>
        private void OnEnable()
        {
            if (_placementEventChannel != null)
            {
                _placementEventChannel.OnPiecePlaced += HandlePiecePlaced;
            }

            if (_orderEventChannel != null)
            {
                _orderEventChannel.OnOrderFulfilled += HandleOrderFulfilled;
                _orderEventChannel.OnOrderMissed += HandleOrderMissed;
            }
        }

        /// <summary>Unsubscribes from this manager's events.</summary>
        private void OnDisable()
        {
            if (_placementEventChannel != null)
            {
                _placementEventChannel.OnPiecePlaced -= HandlePiecePlaced;
            }

            if (_orderEventChannel != null)
            {
                _orderEventChannel.OnOrderFulfilled -= HandleOrderFulfilled;
                _orderEventChannel.OnOrderMissed -= HandleOrderMissed;
            }
        }

        private void HandlePiecePlaced(GameObject placedInstance, Vector3Int cell)
        {
            PlayCue(_confirmSource, _library != null ? _library.placementConfirmClip : null);
        }

        private void HandleOrderFulfilled(OrderData order)
        {
            PlayCue(_orderFulfilledSource, _library != null ? _library.orderFulfilledClip : null);
        }

        private void HandleOrderMissed(OrderData order)
        {
            PlayCue(_orderMissedSource, _library != null ? _library.orderMissedClip : null);
        }

        /// <summary>
        /// Plays the placement-reject cue — called directly by ConveyorManager.ConfirmDrag's
        /// rejection branch, the one placement outcome no existing event channel raises today
        /// (containers can never reject a placement once a ghost is shown, so this only ever fires
        /// from conveyor drags).
        /// </summary>
        public void PlayPlacementReject()
        {
            PlayCue(_rejectSource, _library != null ? _library.placementRejectClip : null);
        }

        /// <summary>
        /// Plays the given clip on the given dedicated source via Play() (restart semantics), not
        /// PlayOneShot — see this class's own doc comment for why that matters here.
        /// </summary>
        private static void PlayCue(AudioSource source, AudioClip clip)
        {
            if (source == null || clip == null)
            {
                return;
            }

            source.clip = clip;
            source.Play();
        }

        /// <summary>
        /// Reports how many GoodsAgent are currently active on the conveyor network (#29) — called
        /// by ConveyorManager once per frame from its own movement update. Starts/stops the shared
        /// belt-hum loop and scales its volume between beltMinVolume/beltMaxVolume based on activity
        /// relative to beltFullVolumeGoodsCount. Never creates a per-segment AudioSource — one
        /// shared, pooled loop regardless of network size (#29 spec's own deliberate scope, to avoid
        /// the per-object cost this project's #28 performance pass was built to eliminate).
        /// </summary>
        public void SetBeltActiveGoodsCount(int activeGoodsCount)
        {
            if (_library == null || _library.beltLoopClip == null || _beltLoopSource == null)
            {
                return;
            }

            if (activeGoodsCount <= 0)
            {
                if (_beltLoopSource.isPlaying)
                {
                    _beltLoopSource.Stop();
                }

                return;
            }

            if (_beltLoopSource.clip != _library.beltLoopClip)
            {
                _beltLoopSource.clip = _library.beltLoopClip;
            }

            if (!_beltLoopSource.isPlaying)
            {
                _beltLoopSource.Play();
            }

            float t = Mathf.Clamp01(activeGoodsCount / (float)Mathf.Max(1, _library.beltFullVolumeGoodsCount));
            _beltLoopSource.volume = Mathf.Lerp(_library.beltMinVolume, _library.beltMaxVolume, t);
        }

        /// <summary>
        /// Crossfades the ambience track to the given clip over the given duration, reaching the
        /// given target volume — called by AmbienceManager on every environment cycle. Alternates
        /// between two dedicated AudioSources so the outgoing track can fade out while the incoming
        /// one fades in, rather than a hard cut.
        /// </summary>
        public void PlayAmbience(AudioClip clip, float crossfadeSeconds, float targetVolume)
        {
            if (clip == null)
            {
                return;
            }

            if (_ambienceCrossfadeCoroutine != null)
            {
                StopCoroutine(_ambienceCrossfadeCoroutine);
            }

            _ambienceCrossfadeCoroutine = StartCoroutine(CrossfadeAmbience(clip, crossfadeSeconds, targetVolume));
        }

        private IEnumerator CrossfadeAmbience(AudioClip clip, float crossfadeSeconds, float targetVolume)
        {
            AudioSource incoming = _ambienceUsingSourceA ? _ambienceSourceB : _ambienceSourceA;
            AudioSource outgoing = _ambienceUsingSourceA ? _ambienceSourceA : _ambienceSourceB;

            incoming.clip = clip;
            incoming.volume = 0f;
            incoming.Play();

            float duration = Mathf.Max(0.01f, crossfadeSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                incoming.volume = Mathf.Lerp(0f, targetVolume, t);
                outgoing.volume = Mathf.Lerp(targetVolume, 0f, t);
                yield return null;
            }

            incoming.volume = targetVolume;
            outgoing.volume = 0f;
            outgoing.Stop();
            _ambienceUsingSourceA = !_ambienceUsingSourceA;
            _ambienceCrossfadeCoroutine = null;
        }
    }
}
