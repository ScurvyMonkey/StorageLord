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
    /// cell.
    ///
    /// Created and wired by Bootstrapper — not placed directly in a scene, since it has no
    /// serialized Inspector fields to wire (its data references are injected via Initialize()).
    /// </summary>
    public class StorageManager : MonoBehaviour
    {
        private PlacementEventChannel _eventChannel;
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
        /// Attempts to store one unit of the given goods at the container occupying the given cell.
        /// </summary>
        /// <returns>True if a container is there and accepted the goods; false if there's no
        /// container at that cell, or it rejected the goods (wrong type or already full).</returns>
        public bool TryStoreAt(Vector3Int cell, GoodsData goodsData)
        {
            return _containers.TryGetValue(cell, out ContainerInstance container) && container.TryAccept(goodsData);
        }

        /// <summary>
        /// Attempts to withdraw one unit of the given goods type from any container currently
        /// holding it. Used by ShippingManager for automatic fulfillment — no specific cell needed,
        /// since the caller only cares whether a matching unit existed somewhere in storage.
        /// </summary>
        /// <returns>True if some container held and released one unit; false if none did.</returns>
        public bool TryWithdraw(GoodsData goodsData)
        {
            foreach (ContainerInstance container in _containers.Values)
            {
                if (container.TryWithdraw(goodsData))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
