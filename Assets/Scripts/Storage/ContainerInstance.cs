using UnityEngine;
using StorageLord.Goods;

namespace StorageLord.Storage
{
    /// <summary>
    /// Marks a placed GameObject as a storage container and holds its per-instance state (locked
    /// goods type, current count, door Animator). Attached by PlacementManager when a container is
    /// confirmed — its presence (or absence) is how StorageManager distinguishes a container
    /// placement from any other kind of piece raised on the shared PlacementEventChannel (e.g. a
    /// conveyor segment, which raises the same event with no other way to tell them apart).
    /// </summary>
    public class ContainerInstance : MonoBehaviour
    {
        private ContainerData _data;
        private Animator _doorAnimator;

        /// <summary>
        /// The goods type this container is currently locked to, or null if empty.
        /// </summary>
        public GoodsData LockedType { get; private set; }

        /// <summary>
        /// How many units of LockedType are currently stored.
        /// </summary>
        public int CurrentCount { get; private set; }

        /// <summary>
        /// Combined weight of everything currently stored, in kilograms — zero while empty. Read by
        /// StorageManager after every deliver/withdraw to report the change to WeightManager (#14).
        /// </summary>
        public float CurrentWeightKg => LockedType != null ? CurrentCount * LockedType.weightKg : 0f;

        /// <summary>
        /// Initializes this instance with its defining data, caches its door Animator (if any), and
        /// snaps the door to its closed idle state — HangarGrey's own default state is a demo
        /// open/close loop, not a real idle state, so this must be set explicitly rather than left
        /// to whatever the Animator starts in. Called once by PlacementManager immediately after
        /// instantiation.
        /// </summary>
        public void Initialize(ContainerData data)
        {
            _data = data;
            _doorAnimator = GetComponentInChildren<Animator>();
            _doorAnimator?.Play("HangarClosed");
        }

        /// <summary>
        /// Attempts to accept one unit of the given goods type: accepted if this container is empty
        /// (locking to that type) or already locked to it and under capacity; rejected if locked to
        /// a different type or already full. Opens the door on the first successful accept, closes
        /// it exactly when capacity is reached.
        /// </summary>
        /// <returns>True if the good was accepted and stored; false if rejected.</returns>
        public bool TryAccept(GoodsData goodsData)
        {
            if (LockedType != null && LockedType != goodsData)
            {
                return false;
            }

            if (LockedType != null && CurrentCount >= _data.capacity)
            {
                return false;
            }

            bool wasEmpty = LockedType == null;
            LockedType = goodsData;
            CurrentCount++;

            if (wasEmpty)
            {
                _doorAnimator?.Play("HangarOpen");
            }

            if (CurrentCount >= _data.capacity)
            {
                _doorAnimator?.Play("HangarClose");
            }

            return true;
        }

        /// <summary>
        /// Attempts to withdraw one unit of this container's locked type — succeeds only if
        /// actually locked to that exact type and holding at least one unit. Reaching zero fully
        /// unlocks the container (LockedType resets to null), free to lock onto a different type
        /// the next time TryAccept is called, and returns the door to its idle-closed state;
        /// otherwise the door opens to reflect the withdrawal.
        /// </summary>
        /// <returns>True if one unit was withdrawn; false if this container isn't locked to that
        /// type or has nothing stored.</returns>
        public bool TryWithdraw(GoodsData goodsData)
        {
            if (LockedType == null || LockedType != goodsData || CurrentCount <= 0)
            {
                return false;
            }

            CurrentCount--;

            if (CurrentCount <= 0)
            {
                LockedType = null;
                _doorAnimator?.Play("HangarClosed");
            }
            else
            {
                _doorAnimator?.Play("HangarOpen");
            }

            return true;
        }
    }
}
