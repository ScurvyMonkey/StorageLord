using System.Collections.Generic;
using StorageLord.Goods;
using StorageLord.Placement;
using UnityEngine;

namespace StorageLord.Storage
{
    /// <summary>
    /// Single source of truth for "what's stored where." Subscribes to PlacementEventChannel to
    /// register containers as they're placed — ignoring any other kind of placement (e.g. conveyor
    /// segments, which raise the same shared event) via the ContainerInstance marker component —
    /// and exposes TryStoreAt for ConveyorManager to call when a GoodsAgent reaches a container's
    /// cell. Reports every deliver/withdraw's weight delta to WeightManager (#14), which tracks
    /// per-platform-segment totals and destroys everything on a segment that goes over capacity.
    ///
    /// Created and wired by Bootstrapper — not placed directly in a scene, since it has no
    /// serialized Inspector fields to wire (its data references are injected via Initialize()).
    /// </summary>
    public class StorageManager : MonoBehaviour
    {
        private PlacementEventChannel _eventChannel;
        private WeightManager _weightManager;
        private readonly Dictionary<Vector3Int, ContainerInstance> _containers = new Dictionary<Vector3Int, ContainerInstance>();

        /// <summary>
        /// Injects this manager's data references and subscribes to the placement event channel.
        /// Called once by Bootstrapper immediately after creation.
        /// </summary>
        public void Initialize(PlacementEventChannel eventChannel)
        {
            _eventChannel = eventChannel;
            if (_eventChannel != null)
            {
                _eventChannel.OnPiecePlaced += HandlePiecePlaced;
            }
        }

        /// <summary>
        /// Unsubscribes from the placement event channel.
        /// </summary>
        private void OnDestroy()
        {
            if (_eventChannel != null)
            {
                _eventChannel.OnPiecePlaced -= HandlePiecePlaced;
            }
        }

        /// <summary>
        /// Wires this manager's reference to WeightManager (#14), notified after every deliver/
        /// withdraw so it can keep each platform segment's tracked weight in sync. Called once by
        /// Bootstrapper after both managers exist.
        /// </summary>
        public void SetWeightManager(WeightManager weightManager)
        {
            _weightManager = weightManager;
        }

        /// <summary>
        /// Registers a newly placed piece as a container if it carries a ContainerInstance marker —
        /// silently ignores any other kind of placement.
        /// </summary>
        private void HandlePiecePlaced(GameObject instance, Vector3Int cell)
        {
            if (instance.TryGetComponent(out ContainerInstance container))
            {
                _containers[cell] = container;
            }
        }

        /// <summary>
        /// Every cell currently holding a tracked container — read-only snapshot for WeightManager
        /// (#14) to find which containers sit on a given platform segment, without exposing this
        /// manager's internal dictionary directly.
        /// </summary>
        public IEnumerable<Vector3Int> GetContainerCells()
        {
            return _containers.Keys;
        }

        /// <summary>
        /// Destroys the container at the given cell (if any) and stops tracking it. Used by
        /// PlacementManager on player-initiated removal, and by WeightManager (#14) when a segment
        /// collapse destroys everything on it. Previously, PlacementManager.HandleRemoveInput
        /// destroyed the container GameObject directly without telling StorageManager — leaving a
        /// dangling ContainerInstance reference in _containers until a new container happened to be
        /// placed at the same cell and silently overwrote it. Fixed as part of #14, since that gap
        /// would otherwise interfere with #14's own removal path too.
        /// </summary>
        public void RemoveContainerAt(Vector3Int cell)
        {
            if (!_containers.TryGetValue(cell, out ContainerInstance container))
            {
                return;
            }

            _containers.Remove(cell);
            if (container != null)
            {
                Destroy(container.gameObject);
            }
        }

        /// <summary>
        /// Attempts to store one unit of the given goods at the container occupying the given cell.
        /// </summary>
        /// <returns>True if a container is there and accepted the goods; false if there's no
        /// container at that cell, or it rejected the goods (wrong type or already full).</returns>
        public bool TryStoreAt(Vector3Int cell, GoodsData goodsData)
        {
            if (!_containers.TryGetValue(cell, out ContainerInstance container))
            {
                return false;
            }

            float weightBefore = container.CurrentWeightKg;
            bool accepted = container.TryAccept(goodsData);
            if (accepted)
            {
                _weightManager?.NotifyContainerWeightChanged(cell, container.CurrentWeightKg, weightBefore);
            }

            return accepted;
        }

        /// <summary>
        /// Attempts to withdraw one unit of the given goods type from any container currently
        /// holding it. Used by ShippingManager for automatic fulfillment — no specific cell needed,
        /// since the caller only cares whether a matching unit existed somewhere in storage.
        /// </summary>
        /// <returns>True if some container held and released one unit; false if none did.</returns>
        public bool TryWithdraw(GoodsData goodsData)
        {
            foreach (KeyValuePair<Vector3Int, ContainerInstance> entry in _containers)
            {
                ContainerInstance container = entry.Value;
                float weightBefore = container.CurrentWeightKg;

                if (container.TryWithdraw(goodsData))
                {
                    _weightManager?.NotifyContainerWeightChanged(entry.Key, container.CurrentWeightKg, weightBefore);
                    return true;
                }
            }

            return false;
        }
    }
}
