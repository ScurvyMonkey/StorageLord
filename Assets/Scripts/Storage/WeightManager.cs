using System.Collections.Generic;
using StorageLord.Conveyors;
using StorageLord.Grid;
using UnityEngine;

namespace StorageLord.Storage
{
    /// <summary>
    /// Tracks each platform floor segment's current stored-goods weight against its capacity (#14),
    /// and destroys everything on a segment (containers, their stored goods, conveyor cells) the
    /// moment it goes over. Notified by StorageManager after every deliver/withdraw — goods in
    /// transit on a conveyor never count, only goods actually stored in a placed container.
    ///
    /// Resolves which segment owns a given grid cell by measuring each PlatformSegment instance's
    /// own real Renderer bounds (X/Z only — height level never matters, since a segment is a floor
    /// tile and stacked containers at any height on the same column still bear down on it) rather
    /// than assuming a footprint-in-cells formula, per this project's established practice of
    /// measuring real geometry instead of guessing it.
    ///
    /// Created and wired by Bootstrapper, after both StorageManager and ConveyorManager exist — not
    /// placed directly in a scene, since it has no serialized Inspector fields to wire (its data
    /// references are injected via Initialize()).
    /// </summary>
    public class WeightManager : MonoBehaviour
    {
        /// <summary>Runtime state for one platform segment — its measured XZ bounds and current load.</summary>
        private class SegmentRuntime
        {
            public PlatformSegment Segment;
            public float MinX, MaxX, MinZ, MaxZ;
            public float CurrentWeightKg;
        }

        /// <summary>Current/capacity snapshot for one segment, for HUD display.</summary>
        public readonly struct SegmentWeightStatus
        {
            public readonly float CurrentKg;
            public readonly float CapacityKg;

            public SegmentWeightStatus(float currentKg, float capacityKg)
            {
                CurrentKg = currentKg;
                CapacityKg = capacityKg;
            }
        }

        private const float WarnThresholdFraction = 0.75f;

        private GridConfig _gridConfig;
        private GridManager _gridManager;
        private StorageManager _storageManager;
        private ConveyorManager _conveyorManager;

        private readonly List<SegmentRuntime> _segments = new List<SegmentRuntime>();
        private readonly Dictionary<Vector3Int, SegmentRuntime> _cellToSegmentCache = new Dictionary<Vector3Int, SegmentRuntime>();

        /// <summary>
        /// Injects this manager's references and measures every PlatformSegment currently in the
        /// scene. Called once by Bootstrapper immediately after creation.
        /// </summary>
        public void Initialize(GridConfig gridConfig, GridManager gridManager, StorageManager storageManager, ConveyorManager conveyorManager)
        {
            _gridConfig = gridConfig;
            _gridManager = gridManager;
            _storageManager = storageManager;
            _conveyorManager = conveyorManager;

            foreach (PlatformSegment segment in FindObjectsByType<PlatformSegment>(FindObjectsSortMode.None))
            {
                Renderer[] renderers = segment.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    continue;
                }

                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                _segments.Add(new SegmentRuntime
                {
                    Segment = segment,
                    MinX = bounds.min.x,
                    MaxX = bounds.max.x,
                    MinZ = bounds.min.z,
                    MaxZ = bounds.max.z,
                    CurrentWeightKg = 0f
                });
            }
        }

        /// <summary>
        /// Returns which segment (if any) owns the given cell, by checking whether the cell's
        /// world-space X/Z position falls within a measured segment's bounds. Cached per cell, since
        /// deliver/withdraw calls this frequently for the same handful of container cells.
        /// </summary>
        private SegmentRuntime FindSegmentForCell(Vector3Int cell)
        {
            if (_cellToSegmentCache.TryGetValue(cell, out SegmentRuntime cached))
            {
                return cached;
            }

            Vector3 world = _gridConfig.Cell3DToWorld(cell);
            foreach (SegmentRuntime segment in _segments)
            {
                if (world.x >= segment.MinX && world.x <= segment.MaxX && world.z >= segment.MinZ && world.z <= segment.MaxZ)
                {
                    _cellToSegmentCache[cell] = segment;
                    return segment;
                }
            }

            return null;
        }

        /// <summary>
        /// Applies a container's weight change to whatever segment owns its cell — called by
        /// StorageManager after every successful deliver/withdraw with the container's weight before
        /// and after. Triggers CollapseSegment if the new total exceeds the segment's capacity.
        /// No-ops if the cell doesn't resolve to any known segment (e.g. PlatformSegmentData
        /// unassigned, or the scene has no measured segments yet).
        /// </summary>
        public void NotifyContainerWeightChanged(Vector3Int cell, float newContainerWeightKg, float previousContainerWeightKg)
        {
            SegmentRuntime segment = FindSegmentForCell(cell);
            if (segment == null)
            {
                return;
            }

            segment.CurrentWeightKg += newContainerWeightKg - previousContainerWeightKg;

            if (segment.Segment.Data != null && segment.CurrentWeightKg > segment.Segment.Data.capacityKg)
            {
                CollapseSegment(segment);
            }
        }

        /// <summary>
        /// Destroys every container and conveyor cell physically on the given segment (containers
        /// via StorageManager.RemoveContainerAt + GridManager.Unregister; conveyor cells via
        /// ConveyorManager.RemoveCells, which already unregisters and drops any riding GoodsAgent
        /// itself), then resets the segment's tracked weight to zero — the tile itself stays and is
        /// immediately re-usable, only what was built on it is lost.
        /// </summary>
        private void CollapseSegment(SegmentRuntime segment)
        {
            List<Vector3Int> containerCellsToRemove = new List<Vector3Int>();
            foreach (Vector3Int cell in _storageManager.GetContainerCells())
            {
                if (FindSegmentForCell(cell) == segment)
                {
                    containerCellsToRemove.Add(cell);
                }
            }

            foreach (Vector3Int cell in containerCellsToRemove)
            {
                _storageManager.RemoveContainerAt(cell);
                _gridManager.Unregister(cell);
            }

            List<Vector3Int> conveyorCellsToRemove = new List<Vector3Int>();
            foreach (Vector3Int cell in _conveyorManager.GetSegmentCells())
            {
                if (FindSegmentForCell(cell) == segment)
                {
                    conveyorCellsToRemove.Add(cell);
                }
            }

            _conveyorManager.RemoveCells(conveyorCellsToRemove);

            segment.CurrentWeightKg = 0f;
        }

        /// <summary>
        /// Returns a status snapshot for every segment at or above WarnThresholdFraction of its
        /// capacity (including over) — read by WeightHUD so the display only ever shows segments
        /// that actually matter, not all of them all the time.
        /// </summary>
        public IEnumerable<SegmentWeightStatus> GetSegmentStatusesNearCapacity()
        {
            foreach (SegmentRuntime segment in _segments)
            {
                float capacity = segment.Segment.Data != null ? segment.Segment.Data.capacityKg : 0f;
                if (capacity <= 0f)
                {
                    continue;
                }

                if (segment.CurrentWeightKg >= capacity * WarnThresholdFraction)
                {
                    yield return new SegmentWeightStatus(segment.CurrentWeightKg, capacity);
                }
            }
        }
    }
}
