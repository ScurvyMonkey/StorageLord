using System.Collections.Generic;
using StorageLord.Core;
using StorageLord.Conveyors;
using StorageLord.Grid;
using StorageLord.Placement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace StorageLord.Storage
{
    /// <summary>
    /// Tracks each platform floor segment's current stored-goods weight against its capacity (#14),
    /// and destroys the whole segment — the floor piece itself, plus everything built on it
    /// (containers, their stored goods, conveyor cells) — the moment it goes over. The segment's
    /// full footprint is then permanently blocked from future placement, since this project has no
    /// runtime floor-placement mechanic to ever put a tile back. Notified by StorageManager after
    /// every deliver/withdraw — goods in transit on a conveyor never count, only goods actually
    /// stored in a placed container.
    ///
    /// Resolves which segment owns a given grid cell by measuring each PlatformSegment instance's
    /// own real Renderer bounds (X/Z only — height level never matters, since a segment is a floor
    /// tile and stacked containers at any height on the same column still bear down on it) rather
    /// than assuming a footprint-in-cells formula, per this project's established practice of
    /// measuring real geometry instead of guessing it.
    ///
    /// Gained its first-ever per-frame input handling in #27 — a left-click on a placed platform
    /// floor tile, outside both placement modes, attempts to purchase the next platform-capacity
    /// upgrade tier (see EffectiveCapacityKg/Update()). Every capacity read in this file goes
    /// through EffectiveCapacityKg so an upgrade purchase is reflected instantly everywhere,
    /// including segments already carrying stored goods.
    ///
    /// Created and wired by Bootstrapper, after both StorageManager and ConveyorManager exist — not
    /// placed directly in a scene, since it has no serialized Inspector fields to wire (its data
    /// references are injected via Initialize(), with PlacementManager/UpgradeManager wired late via
    /// SetPlacementManager/SetUpgradeManager once those managers exist too).
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

        // How many height levels to permanently block per collapsed column -- generous headroom
        // above any realistic stack (container auto-stacking, conveyor PageUp/PageDown), not a
        // measured limit; GridManager's occupancy set is a plain HashSet, so over-blocking a few
        // never-reached levels costs nothing.
        private const int PermanentlyBlockedHeightLevels = 20;

        private GridConfig _gridConfig;
        private GridManager _gridManager;
        private StorageManager _storageManager;
        private ConveyorManager _conveyorManager;
        private PlacementManager _placementManager;
        private UpgradeManager _upgradeManager;
        private Camera _mainCamera;
        private bool _isGameActive = true;

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
        /// Caches the main camera once rather than querying Camera.main every Update (#27) — needed
        /// now that this manager has its own input handling for the first time (the platform-tile
        /// upgrade-purchase click).
        /// </summary>
        private void Awake()
        {
            _mainCamera = Camera.main;
        }

        /// <summary>
        /// Wires this manager's reference to PlacementManager (#27), used to gate the platform-tile
        /// upgrade-purchase click against container placement mode, mirroring every other
        /// outside-both-modes click handler in the project (ReceivingManager's #15 dock click,
        /// ConveyorManager's #17 branch-filter click). Called once by Bootstrapper after both
        /// managers exist.
        /// </summary>
        public void SetPlacementManager(PlacementManager placementManager)
        {
            _placementManager = placementManager;
        }

        /// <summary>
        /// Wires this manager's reference to UpgradeManager (#27), used to read the current platform
        /// capacity multiplier and to attempt a purchase on the upgrade-click input. Called once by
        /// Bootstrapper after both managers exist.
        /// </summary>
        public void SetUpgradeManager(UpgradeManager upgradeManager)
        {
            _upgradeManager = upgradeManager;
        }

        /// <summary>
        /// Halts (or resumes) this manager's upgrade-purchase click handling — called by GameManager
        /// (#27) when the run ends, mirroring PlacementManager/ConveyorManager/ReceivingManager/
        /// ShippingManager's own SetGameActive(bool) so platform-tile upgrade purchases stop working
        /// once the run is over too, the same as every other player interaction in the game.
        /// </summary>
        public void SetGameActive(bool active)
        {
            _isGameActive = active;
        }

        /// <summary>
        /// Polls for a left-click on a placed platform floor tile, outside both placement modes
        /// (#27) — attempts to purchase the next platform capacity upgrade tier if one is hit. This
        /// is this manager's first-ever per-frame input handling; everything else about it stays
        /// purely reactive (NotifyContainerWeightChanged, called by StorageManager). Skips entirely
        /// when the click lands on a UI element (#36's arch condition) — a stray world raycast
        /// behind an open popup/dropdown could otherwise spend real money the player never intended
        /// to spend.
        /// </summary>
        private void Update()
        {
            if (!_isGameActive || Keyboard.current == null || Mouse.current == null || _mainCamera == null)
            {
                return;
            }

            if (!Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if ((_placementManager != null && _placementManager.IsPlacementModeActive)
                || (_conveyorManager != null && _conveyorManager.IsPlacementModeActive))
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit))
            {
                return;
            }

            PlatformSegment segment = hit.collider.GetComponentInParent<PlatformSegment>();
            if (segment == null || _upgradeManager == null)
            {
                return;
            }

            _upgradeManager.TryPurchaseNextPlatformTier();
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

            if (segment.Segment.Data != null && segment.CurrentWeightKg > EffectiveCapacityKg(segment))
            {
                CollapseSegment(segment);
            }
        }

        /// <summary>
        /// Returns the given segment's real, current capacity — its data's raw capacityKg times the
        /// current platform-capacity upgrade multiplier (#27, 1 if no tier purchased yet or no
        /// UpgradeManager wired). Every capacity read in this file goes through this one method, so
        /// an upgrade purchase is reflected everywhere instantly, including for segments already
        /// carrying stored goods. Caller must have already checked segment.Segment.Data != null.
        /// </summary>
        private float EffectiveCapacityKg(SegmentRuntime segment)
        {
            float multiplier = _upgradeManager != null ? _upgradeManager.PlatformCapacityMultiplier : 1f;
            return segment.Segment.Data.capacityKg * multiplier;
        }

        /// <summary>
        /// Destroys every container and conveyor cell physically on the given segment (containers
        /// via StorageManager.RemoveContainerAt + GridManager.Unregister; conveyor cells via
        /// ConveyorManager.RemoveCells, which already unregisters and drops any riding GoodsAgent
        /// itself), then destroys the floor piece itself and permanently blocks every cell in its
        /// footprint — the whole tile is gone, not just what was built on it (direct designer
        /// correction, post-ship: the first pass kept the tile and only wiped its contents, since the
        /// original spec's "rebuild there... if they want to use that tile again" language implied
        /// the floor persisted; the designer clarified the platform itself should disappear).
        /// Permanently blocking (not freeing) the footprint matters because this project has no
        /// runtime floor-placement mechanic at all — platform assembly is Editor-time only (see
        /// CLAUDE.md's Platform Assembly entry) — so once a tile is gone there is no way for a player
        /// to ever get a floor back there during a session; leaving those cells simply "free" would
        /// let a container/conveyor be confirmed floating over open space, a direct Pillar 3 ("clean
        /// by construction") violation.
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

            BlockFootprintPermanently(segment);

            if (segment.Segment != null)
            {
                Destroy(segment.Segment.gameObject);
            }

            _segments.Remove(segment);
            _cellToSegmentCache.Clear();
        }

        /// <summary>
        /// Registers every cell in the segment's full X/Z footprint (not just cells that happened to
        /// hold a container/conveyor) as permanently occupied in GridManager, at every height level a
        /// player could plausibly reach (containers auto-stack, conveyors go up via PageUp/PageDown)
        /// — done before the segment is dropped from _segments/its floor destroyed, since it still
        /// needs FindSegmentForCell to resolve candidate cells to exactly this segment (not a
        /// neighboring one whose measured bounds happen to overlap at the shared edge).
        /// </summary>
        private void BlockFootprintPermanently(SegmentRuntime segment)
        {
            int cellXMin = Mathf.RoundToInt(segment.MinX / _gridConfig.cellSize);
            int cellXMax = Mathf.RoundToInt(segment.MaxX / _gridConfig.cellSize);
            int cellZMin = Mathf.RoundToInt(segment.MinZ / _gridConfig.cellSize);
            int cellZMax = Mathf.RoundToInt(segment.MaxZ / _gridConfig.cellSize);

            for (int x = cellXMin; x <= cellXMax; x++)
            {
                for (int z = cellZMin; z <= cellZMax; z++)
                {
                    Vector3Int column = new Vector3Int(x, 0, z);
                    if (FindSegmentForCell(column) != segment)
                    {
                        continue;
                    }

                    for (int level = 0; level < PermanentlyBlockedHeightLevels; level++)
                    {
                        _gridManager.Register(new Vector3Int(x, level, z));
                    }
                }
            }
        }

        /// <summary>
        /// Returns the summed capacity (kg) of every segment currently surviving (#26) — used by
        /// ShippingManager to size a Special-tier event's weight target as a fraction of the
        /// platform's *current* total capacity. Computed fresh each call rather than cached, so a
        /// platform that has already lost segments to overload (CollapseSegment removes them from
        /// _segments) correctly yields a smaller total, never a frozen session-start number.
        /// </summary>
        public float GetTotalCapacityKg()
        {
            float total = 0f;
            foreach (SegmentRuntime segment in _segments)
            {
                if (segment.Segment.Data != null)
                {
                    total += EffectiveCapacityKg(segment);
                }
            }

            return total;
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
                float capacity = segment.Segment.Data != null ? EffectiveCapacityKg(segment) : 0f;
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
