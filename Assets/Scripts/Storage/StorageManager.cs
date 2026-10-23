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
        /// Attempts to store one unit of the given goods in the storage column at the given cell
        /// (#47). A stack of containers acts as one column whose only interface is its base
        /// (deck-level) container: the arrival must target the base cell and come from the base's
        /// door side (#23). An arrival aimed at an upper container's cell, e.g. from an elevated
        /// belt, is rejected, so a column has exactly one way in. The unit goes to the lowest
        /// container already locked to this type with room, otherwise the lowest empty one, so a
        /// fresh container isn't locked to a type while a matching one still has space.
        /// Rejections hold the agent in place exactly like a wrong-type/full rejection always has.
        /// </summary>
        /// <returns>True if the column accepted the unit; false if there's no container at that
        /// cell, the cell isn't a column base, the agent didn't arrive from the base's door side,
        /// or no container in the column can take this type.</returns>
        public bool TryStoreAt(Vector3Int cell, GoodsData goodsData, Vector3Int fromCell)
        {
            if (!_containers.TryGetValue(cell, out ContainerInstance baseContainer) || ColumnBase(cell) != cell)
            {
                return false;
            }

            if (fromCell != cell + baseContainer.DoorDirection)
            {
                return false;
            }

            Vector3Int? target = FindStorageTarget(cell, goodsData);
            if (!target.HasValue)
            {
                return false;
            }

            ContainerInstance container = _containers[target.Value];
            float weightBefore = container.CurrentWeightKg;
            bool accepted = container.TryAccept(goodsData);
            if (accepted)
            {
                _weightManager?.NotifyContainerWeightChanged(target.Value, container.CurrentWeightKg, weightBefore);
            }

            return accepted;
        }

        /// <summary>
        /// The column's preferred container for a new unit of <paramref name="goodsData"/>: the
        /// lowest container already locked to it with room, otherwise the lowest empty one, or null
        /// if neither exists.
        /// </summary>
        private Vector3Int? FindStorageTarget(Vector3Int baseCell, GoodsData goodsData)
        {
            Vector3Int? firstEmpty = null;
            for (Vector3Int cell = baseCell; _containers.TryGetValue(cell, out ContainerInstance container); cell += Vector3Int.up)
            {
                if (container.LockedType == goodsData && container.CurrentCount < container.Capacity)
                {
                    return cell;
                }

                if (container.LockedType == null && !firstEmpty.HasValue)
                {
                    firstEmpty = cell;
                }
            }

            return firstEmpty;
        }

        /// <summary>
        /// The base (lowest) cell of the storage column containing <paramref name="cell"/> (#47).
        /// Columns are always contiguous, because placement is support-required and removal
        /// cascades upward (#3).
        /// </summary>
        private Vector3Int ColumnBase(Vector3Int cell)
        {
            while (_containers.ContainsKey(cell + Vector3Int.down))
            {
                cell += Vector3Int.down;
            }

            return cell;
        }

        /// <summary>
        /// Total units stored and total capacity across the whole storage column containing
        /// <paramref name="cell"/> (#47), for the Container Inspector's column summary. Returns
        /// (0, 0) if there's no container at that cell.
        /// </summary>
        public (int Stored, int Capacity) GetColumnTotals(Vector3Int cell)
        {
            if (!_containers.ContainsKey(cell))
            {
                return (0, 0);
            }

            int stored = 0;
            int capacity = 0;
            for (Vector3Int c = ColumnBase(cell); _containers.TryGetValue(c, out ContainerInstance container); c += Vector3Int.up)
            {
                stored += container.CurrentCount;
                capacity += container.Capacity;
            }

            return (stored, capacity);
        }

        /// <summary>
        /// Fills <paramref name="counts"/> with the units of each goods type currently stored across
        /// all containers (#49 stockpile thresholds). Clears it first; the caller reuses one
        /// dictionary so the per-frame stockpile check doesn't allocate.
        /// </summary>
        public void CountStoredByType(Dictionary<GoodsData, int> counts)
        {
            counts.Clear();
            foreach (ContainerInstance container in _containers.Values)
            {
                if (container.LockedType == null || container.CurrentCount <= 0)
                {
                    continue;
                }

                counts.TryGetValue(container.LockedType, out int current);
                counts[container.LockedType] = current + container.CurrentCount;
            }
        }

        /// <summary>The cell <paramref name="container"/> is registered at, if it's a tracked container (#47 inspector). Linear scan; call once per inspection, not per frame.</summary>
        public bool TryGetCell(ContainerInstance container, out Vector3Int cell)
        {
            foreach (KeyValuePair<Vector3Int, ContainerInstance> entry in _containers)
            {
                if (entry.Value == container)
                {
                    cell = entry.Key;
                    return true;
                }
            }

            cell = default;
            return false;
        }

        /// <summary>
        /// Attempts to dispatch one unit of the given goods type onto a belt (#22). Works per storage
        /// column (#47): for each column base with a real, currently-open conveyor segment on its
        /// single output cell (the neighbor directly opposite the base's door, #23), takes the unit
        /// from the uppermost container in that column holding this type, and only then withdraws, so a matching container with no belt built there yet is left untouched
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
                if (_containers.ContainsKey(entry.Key + Vector3Int.down))
                {
                    continue;
                }

                Vector3Int? outputCell = FindFreeOutputBeltCell(entry.Key, entry.Value.DoorDirection);
                if (!outputCell.HasValue)
                {
                    continue;
                }

                Vector3Int? source = FindDispatchSource(entry.Key, goodsData);
                if (!source.HasValue)
                {
                    continue;
                }

                ContainerInstance container = _containers[source.Value];
                float weightBefore = container.CurrentWeightKg;
                if (!container.TryWithdraw(goodsData))
                {
                    continue;
                }

                _weightManager?.NotifyContainerWeightChanged(source.Value, container.CurrentWeightKg, weightBefore);
                spawnCell = outputCell.Value;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The uppermost container in the column at <paramref name="baseCell"/> holding
        /// <paramref name="goodsData"/> (#47), or null. Draining from the top empties and unlocks
        /// upper containers first.
        /// </summary>
        private Vector3Int? FindDispatchSource(Vector3Int baseCell, GoodsData goodsData)
        {
            Vector3Int? source = null;
            for (Vector3Int cell = baseCell; _containers.TryGetValue(cell, out ContainerInstance container); cell += Vector3Int.up)
            {
                if (container.LockedType == goodsData && container.CurrentCount > 0)
                {
                    source = cell;
                }
            }

            return source;
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
