using System.Collections.Generic;
using StorageLord.Conveyors;
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
        private ConveyorManager _conveyorManager;
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
        /// Wires this manager's reference to ConveyorManager (#22), needed so TryDispatch can
        /// check whether a container has a real, currently-open belt segment against one of its
        /// cardinal neighbors before withdrawing a unit onto it. Called once by Bootstrapper after
        /// both managers exist — the same reciprocal-wiring pattern PlacementManager/ConveyorManager
        /// already established, completing the pair ConveyorManager's own SetStorageManager started.
        /// </summary>
        public void SetConveyorManager(ConveyorManager conveyorManager)
        {
            _conveyorManager = conveyorManager;
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
        /// Attempts to store one unit of the given goods at the container occupying the given cell —
        /// only accepted if the agent is arriving from the container's own door side (#23); arrival
        /// from any other neighbor is rejected exactly like a wrong-type/full rejection, holding the
        /// agent in place with no new failure state.
        /// </summary>
        /// <returns>True if a container is there, the agent arrived from its door side, and it
        /// accepted the goods; false if there's no container at that cell, the agent arrived from a
        /// non-door side, or the container rejected the goods (wrong type or already full).</returns>
        public bool TryStoreAt(Vector3Int cell, GoodsData goodsData, Vector3Int fromCell)
        {
            if (!_containers.TryGetValue(cell, out ContainerInstance container))
            {
                return false;
            }

            if (fromCell != cell + container.DoorDirection)
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
        /// Attempts to dispatch one unit of the given goods type onto a belt (#22) — finds whichever
        /// container currently holds this type and has a real, currently-open conveyor segment on
        /// its single output cell (the neighbor directly opposite its door, #23), and only then
        /// withdraws a unit, so a matching container with no belt built there yet is left untouched
        /// rather than losing a unit with nowhere to send it. Replaces the old TryWithdraw, which
        /// credited an order the instant a matching unit existed anywhere in storage, with no belt
        /// or Shipping dock involved at all — orders now only count goods that actually travel to
        /// and arrive at Shipping.
        /// </summary>
        /// <returns>True if a unit was withdrawn, with spawnCell set to the belt cell to spawn it
        /// onto; false if no container holds this type with a currently-available output belt.
        /// </returns>
        public bool TryDispatch(GoodsData goodsData, out Vector3Int spawnCell)
        {
            spawnCell = default;

            if (_conveyorManager == null)
            {
                return false;
            }

            foreach (KeyValuePair<Vector3Int, ContainerInstance> entry in _containers)
            {
                ContainerInstance container = entry.Value;
                if (container.LockedType != goodsData || container.CurrentCount <= 0)
                {
                    continue;
                }

                Vector3Int? outputCell = FindFreeOutputBeltCell(entry.Key, container.DoorDirection);
                if (!outputCell.HasValue)
                {
                    continue;
                }

                float weightBefore = container.CurrentWeightKg;
                if (!container.TryWithdraw(goodsData))
                {
                    continue;
                }

                _weightManager?.NotifyContainerWeightChanged(entry.Key, container.CurrentWeightKg, weightBefore);
                spawnCell = outputCell.Value;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks a container's single output cell — the neighbor directly opposite its door (#23)
        /// — for a real placed conveyor segment that's currently free to accept a new agent *and*
        /// doesn't flow straight back into this same container. The flow check matters: a belt
        /// placed against a container with its flow pointing back at it (e.g. the player never
        /// cycled Q/R away from a previous drag's direction) would otherwise let a dispatched good
        /// hand right back into storage on its very first hop, before it ever visibly moves — found
        /// live as a real bug (#22 hotfix), silently round-tripping through the whole dispatch loop
        /// with nothing ever actually leaving; still applies here even though there's now only one
        /// candidate cell to check instead of four.
        /// </summary>
        private Vector3Int? FindFreeOutputBeltCell(Vector3Int containerCell, Vector3Int doorDirection)
        {
            Vector3Int outputCell = containerCell - doorDirection;
            if (_conveyorManager.HasSegmentAt(outputCell)
                && _conveyorManager.IsCellFreeForAgent(outputCell)
                && !_conveyorManager.DoesCellFlowToward(outputCell, containerCell))
            {
                return outputCell;
            }

            return null;
        }
    }
}
