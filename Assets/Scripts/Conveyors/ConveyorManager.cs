using System.Collections.Generic;
using StorageLord.Docks;
using StorageLord.Goods;
using StorageLord.Grid;
using StorageLord.Placement;
using StorageLord.Storage;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StorageLord.Conveyors
{
    /// <summary>
    /// Runtime placement and movement system for conveyor belts. Players toggle conveyor placement
    /// mode (C), then left-click-drag from a start cell to an end cell up to 10 cells away, cycling
    /// flow direction with Q/R while dragging; releasing registers every cell the drag touches with
    /// GridManager and _segmentFlowDirections as one PlacedSpan (#10). What gets visually
    /// instantiated per cell — a real, functional BeltPlatform anchor or a plain BeltSystem fill
    /// tile — is deliberately NOT decided at confirm time; it's a topology-derived property of the
    /// whole network (RebuildBeltVisuals), recomputed after every placement/removal exactly like
    /// junctions and the main line already are (#7). A cell only gets a platform if it's a genuine
    /// start (nothing feeds it), a genuine end (its flow doesn't lead to another registered cell), a
    /// junction (2+ feeders), or a bend (its feeder's flow direction differs from its own) — this
    /// was a direct fix for a real bug: deciding anchors per-drag at confirm time put a redundant
    /// second platform at every point a player extended an existing run with a new drag, since that
    /// new drag's own start cell always got its own anchor regardless of what was already sitting
    /// right next to it. Removal (right-click) always targets the whole span a clicked anchor
    /// belongs to, never a single cell within it — see PlacedSpan/_cellOwnership. Also drives
    /// per-frame movement of any GoodsAgent sitting on a placed cell. Separate from PlacementManager
    /// (drag-based input differs enough from single-click container placement to warrant its own
    /// class), but shares GridManager's occupancy registry with it so the two placers can never
    /// confirm into the same cell, and enforces mutual exclusion with PlacementManager's own
    /// placement mode so a single R press is never ambiguous between "rotate the container ghost"
    /// and "cycle belt flow direction."
    ///
    /// Created and wired by Bootstrapper — not placed directly in a scene, since it has no
    /// serialized Inspector fields to wire (its data references are injected via Initialize()).
    /// </summary>
    public class ConveyorManager : MonoBehaviour
    {
        /// <summary>
        /// Tracks one anchor-to-anchor drag placed as a unit (#10) — just its cells. Ownership only
        /// matters for removal (right-click always removes a whole span's cells together, never a
        /// single cell within it — see _cellOwnership) and never for visuals: every visual instance,
        /// platform or fill tile alike, is purely derived from current network topology and rebuilt
        /// wholesale by RebuildBeltVisuals, not owned or created by whichever span happened to be
        /// confirmed first. A cell the drag merged onto (already existing from an earlier span) is
        /// deliberately excluded, since removing this span must never touch a cell another span
        /// still owns.
        ///
        /// OwnDirection (#17) records the *specific* direction this span is responsible for at every
        /// cell it touches, including a branch origin — a cell it starts by splitting rather than
        /// registering fresh. This matters once a cell can carry directions contributed by more than
        /// one span (its own original span, plus whichever later span branched off it): removing one
        /// span must only ever take back its own direction, never blindly wipe a cell another span's
        /// direction still lives at. OwnDirection is a superset of Cells (it also covers a branch
        /// origin, which isn't in Cells since that cell wasn't newly registered by this span) — Cells
        /// stays separate because it has its own distinct purpose: only newly-registered cells raise
        /// PlacementEventChannel's placement event in ConfirmDrag, a branch origin never does.
        /// </summary>
        private class PlacedSpan
        {
            public readonly List<Vector3Int> Cells = new List<Vector3Int>();
            public readonly Dictionary<Vector3Int, Vector3Int> OwnDirection = new Dictionary<Vector3Int, Vector3Int>();
        }

        private static readonly Vector3Int[] CardinalDirections =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(-1, 0, 0), new Vector3Int(0, 0, -1)
        };

        private static readonly Color ValidPreviewTint = new Color(0.4f, 1f, 0.4f, 0.6f);
        private static readonly Color InvalidPreviewTint = new Color(1f, 0.3f, 0.3f, 0.6f);
        private static readonly Color FlowArrowColor = new Color(1f, 0.75f, 0.1f);
        private static readonly Color EnergyConnectorColor = new Color(0.15f, 0.65f, 1f);

        private const int MaxDragSegments = 10;
        private const float FlowArrowHoverOffset = 0.4f;
        private const float EnergyConnectorWidth = 1.4f;
        private const float EnergyConnectorLength = 3.4f;
        private const float EnergyConnectorEmissionIntensity = 2.5f;

        // BeltSystem fill-tile sizing (#10 follow-up): BeltSystem.prefab's own authored length,
        // measured via PrefabUtility.InstantiatePrefab + Renderer.bounds this session — a single
        // fixed-length prefab instantiated unscaled doesn't actually fit every edge, because how
        // much of a 4m cell-to-cell gap is really open depends on whether either end is a real
        // BeltPlatform anchor (whose own fins eat into the gap) or a bare fill cell (nothing there
        // to eat into it at all). Used by CreateFillTile to scale each tile's Z to the real
        // available gap for its specific edge rather than assuming one uniform length everywhere —
        // this is what was actually causing "gaps on longer drags" (an unscaled 3.4m tile left
        // ~0.3m bare on each side of a full 4m fill-to-fill gap, and longer drags simply have more
        // of those interior edges than a short 2-cell drag does).
        private const float BeltSystemNativeLength = 3.4f;

        // How much of the 4m cell-to-cell gap a BeltPlatform anchor's own fins eat into, per end —
        // derived from the measured ~1.97m fin-to-fin gap between two adjacent anchors 4m apart:
        // (4.0 - 1.97) / 2.
        private const float AnchorFinInsetPerEnd = 1.015f;

        private GridConfig _gridConfig;
        private GridManager _gridManager;
        private ConveyorData _conveyorData;
        private GoodsData _debugGoodsData;
        private PlacementEventChannel _eventChannel;
        private PlacementManager _placementManager;
        private StorageManager _storageManager;
        private ReceivingManager _receivingManager;
        private ShippingManager _shippingManager;

        private bool _isPlacing;
        private Vector3Int? _dragStartCell;
        private int _flowDirectionIndex;
        private int _dragHeightLevel;
        private List<Vector3Int> _lastPreviewCells;
        private bool _lastRunValid;

        private readonly List<GameObject> _previewInstances = new List<GameObject>();
        private readonly Dictionary<Vector3Int, GameObject> _segmentInstances = new Dictionary<Vector3Int, GameObject>();

        // Cell -> every outgoing flow direction registered there (#17: widened from a single
        // Vector3Int to a list so a cell can split into 2+ branches; every pre-#17 cell still just
        // holds a one-entry list, and every call site below degenerates to the old single-direction
        // behavior exactly in that case).
        private readonly Dictionary<Vector3Int, List<Vector3Int>> _segmentFlowDirections = new Dictionary<Vector3Int, List<Vector3Int>>();

        private readonly List<GoodsAgent> _activeGoods = new List<GoodsAgent>();
        private readonly List<GameObject> _energyConnectors = new List<GameObject>();
        private readonly List<PlacedSpan> _placedSpans = new List<PlacedSpan>();
        private readonly Dictionary<Vector3Int, PlacedSpan> _cellOwnership = new Dictionary<Vector3Int, PlacedSpan>();
        private readonly List<GameObject> _fillTiles = new List<GameObject>();

        // Splitter state (#17): a filter is keyed by a branch's own destination cell (the first cell
        // past the split), not by the split cell itself — a split cell's shared anchor can't uniquely
        // represent "which branch" a click means, but each branch's own first cell can. Absent/null
        // means "Any" (wildcard). A branch destination is always given a real BeltPlatform anchor by
        // IsAnchorCell specifically so it's clickable at all — BeltSystem fill tiles carry no
        // collider (see IsAnchorCell's own doc comment), so resolution goes through the same
        // _segmentInstances/FindCellForHitObject anchor lookup right-click removal already uses, no
        // separate fill-tile tracking needed.
        private readonly Dictionary<Vector3Int, GoodsData> _branchFilters = new Dictionary<Vector3Int, GoodsData>();

        // Persists a GoodsAgent's chosen branch at a split cell across frames while it's mid-transit
        // toward that choice (agent.CurrentCell doesn't change until arrival) — recomputing the
        // choice fresh every frame would let PickFavoredBranch's alternation flip an agent's target
        // mid-flight. Same shape as _claimedTargets' persistence for junction arbitration (#7) —
        // cleared on arrival or when the agent stops being tracked.
        private readonly Dictionary<GoodsAgent, Vector3Int> _agentBranchChoice = new Dictionary<GoodsAgent, Vector3Int>();
        private readonly Dictionary<Vector3Int, Vector3Int> _lastFavoredBranch = new Dictionary<Vector3Int, Vector3Int>();

        // Network-topology state (#7): recomputed wholesale on every placement/removal, never
        // patched incrementally — same "rebuild wholesale, fine at Phase 1 scale" tradeoff
        // RebuildDockConnectors already makes.
        private readonly HashSet<Vector3Int> _mainLineCells = new HashSet<Vector3Int>();
        private readonly Dictionary<Vector3Int, List<Vector3Int>> _junctionFeeders = new Dictionary<Vector3Int, List<Vector3Int>>();
        private readonly Dictionary<Vector3Int, GoodsAgent> _claimedTargets = new Dictionary<Vector3Int, GoodsAgent>();
        private readonly Dictionary<Vector3Int, Vector3Int> _lastFavoredFeeder = new Dictionary<Vector3Int, Vector3Int>();

        private Camera _mainCamera;
        private Mesh _flowArrowMesh;
        private Material _flowArrowMaterial;
        private Mesh _energyConnectorMesh;
        private Material _energyConnectorMaterial;

        /// <summary>
        /// True while conveyor placement mode is active. PlacementManager calls into this manager's
        /// ExitPlacementMode via its own SetConveyorManager reference to enforce mutual exclusion
        /// between the two placement modes.
        /// </summary>
        public bool IsPlacementModeActive => _isPlacing;

        private bool _isGameActive = true;

        /// <summary>
        /// Halts (or resumes) all input handling and goods movement — called by GameManager (#12)
        /// when the run ends (missed-order limit reached). Deactivating force-exits any active drag
        /// so nothing is left mid-placement.
        /// </summary>
        public void SetGameActive(bool active)
        {
            _isGameActive = active;
            if (!active)
            {
                _isPlacing = false;
                CancelDrag();
            }
        }

        /// <summary>
        /// Injects this manager's data references. Called once by Bootstrapper immediately after
        /// creation, since this manager is created in code (not from a prefab) and so has no
        /// Inspector to assign references through directly.
        /// </summary>
        public void Initialize(
            GridConfig gridConfig,
            GridManager gridManager,
            PlacementEventChannel eventChannel,
            ConveyorData conveyorData,
            GoodsData debugGoodsData)
        {
            _gridConfig = gridConfig;
            _gridManager = gridManager;
            _eventChannel = eventChannel;
            _conveyorData = conveyorData;
            _debugGoodsData = debugGoodsData;
        }

        /// <summary>
        /// Wires this manager's reciprocal reference to PlacementManager, so each can force-exit
        /// the other's placement mode to keep the two mutually exclusive. Called once by Bootstrapper
        /// after both managers exist.
        /// </summary>
        public void SetPlacementManager(PlacementManager placementManager)
        {
            _placementManager = placementManager;
        }

        /// <summary>
        /// Wires this manager's reference to StorageManager, used when a GoodsAgent reaches a cell
        /// that isn't a conveyor segment — checked to see if it's a container that can accept the
        /// good. Called once by Bootstrapper after both managers exist.
        /// </summary>
        public void SetStorageManager(StorageManager storageManager)
        {
            _storageManager = storageManager;
        }

        /// <summary>
        /// Wires this manager's reference to ReceivingManager, used to trace the network's main
        /// line back to the Receiving dock's output cell (backflow prevention, junction priority).
        /// Called once by Bootstrapper after both managers exist. Immediately recomputes network
        /// topology so the main line is correct even before any further placement/removal happens.
        /// </summary>
        public void SetReceivingManager(ReceivingManager receivingManager)
        {
            _receivingManager = receivingManager;
            RebuildNetworkTopology();
        }

        /// <summary>
        /// Wires this manager's reference to ShippingManager, used when a GoodsAgent reaches a cell
        /// that isn't a conveyor segment or a container — checked to see if it's a Shipping dock's
        /// input cell that can consume the good toward an active order. Called once by Bootstrapper
        /// after both managers exist.
        /// </summary>
        public void SetShippingManager(ShippingManager shippingManager)
        {
            _shippingManager = shippingManager;
        }

        /// <summary>
        /// Caches the main camera once rather than querying Camera.main every Update.
        /// </summary>
        private void Awake()
        {
            _mainCamera = Camera.main;
        }

        /// <summary>
        /// Polls input each frame: C toggles conveyor placement mode; while active, drives the drag
        /// preview and Q/R flow-direction cycling. Also advances any in-transit GoodsAgent every
        /// frame regardless of placement mode.
        /// </summary>
        private void Update()
        {
            if (!_isGameActive || Keyboard.current == null || Mouse.current == null)
            {
                return;
            }

            if (Keyboard.current.cKey.wasPressedThisFrame)
            {
                ToggleConveyorMode();
            }

            if (_isPlacing)
            {
                HandleDragInput();
            }
            else
            {
                HandleRemoveInput();
                HandleBranchFilterClickInput();
#if UNITY_EDITOR
                HandleDebugSpawnInput();
#endif
            }

            AdvanceGoods();
        }

        /// <summary>
        /// Force-exits conveyor placement mode if active, cancelling any in-progress drag. Called by
        /// PlacementManager when the player enters container placement mode, to keep the two modes
        /// mutually exclusive.
        /// </summary>
        public void ExitPlacementMode()
        {
            if (_isPlacing)
            {
                _isPlacing = false;
                CancelDrag();
            }
        }

        /// <summary>
        /// Enters or exits conveyor placement mode. Entering force-exits PlacementManager's
        /// container placement mode first. No-ops with a warning if required data references aren't
        /// assigned.
        /// </summary>
        private void ToggleConveyorMode()
        {
            if (_gridConfig == null || _gridManager == null || _conveyorData == null
                || _conveyorData.beltPlatformPrefab == null || _conveyorData.beltSystemPrefab == null)
            {
                Debug.LogWarning(
                    "ConveyorManager: cannot enter placement mode — GridConfig, GridManager, or ConveyorData's " +
                    "beltPlatformPrefab/beltSystemPrefab not assigned.");
                return;
            }

            _isPlacing = !_isPlacing;
            if (_isPlacing)
            {
                _dragHeightLevel = 0;
                _placementManager?.ExitPlacementMode();
            }
            else
            {
                CancelDrag();
            }
        }

        /// <summary>
        /// Handles left-click-drag placement input, Q/R flow-direction cycling, and
        /// PageUp/PageDown height-level cycling while conveyor placement mode is active.
        /// </summary>
        private void HandleDragInput()
        {
            if (Keyboard.current.qKey.wasPressedThisFrame)
            {
                CycleFlowDirection(-1);
            }

            if (Keyboard.current.rKey.wasPressedThisFrame)
            {
                CycleFlowDirection(1);
            }

            HandleHeightInput();

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                BeginDrag();
            }
            else if (_dragStartCell.HasValue && Mouse.current.leftButton.isPressed)
            {
                UpdateDragPreview();
            }
            else if (_dragStartCell.HasValue && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                ConfirmDrag();
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _isPlacing = false;
                CancelDrag();
            }
        }

        /// <summary>
        /// Raycasts the cursor against the plane for the currently selected height level (#7:
        /// conveyors are no longer deck-only — PageUp/PageDown selects the level, unlike
        /// PlacementManager's auto-derived container stacking, since a conveyor's whole purpose
        /// here is deliberately routing at a chosen alternate level, e.g. over an obstacle, not
        /// piling) to find the aimed X/Z column, returning it as a 3D cell at that level.
        /// </summary>
        private Vector3Int? RaycastDeckCell()
        {
            if (_mainCamera == null)
            {
                return null;
            }

            Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            float planeHeight = _gridConfig.deckSurfaceHeight + _dragHeightLevel * _gridConfig.cellHeight;
            Plane deckPlane = new Plane(Vector3.up, new Vector3(0f, planeHeight, 0f));
            if (!deckPlane.Raycast(ray, out float enter))
            {
                return null;
            }

            Vector3 hitPoint = ray.GetPoint(enter);
            Vector2Int column = _gridConfig.WorldToCell(hitPoint);
            return new Vector3Int(column.x, _dragHeightLevel, column.y);
        }

        /// <summary>
        /// PageUp/PageDown raises/lowers the height level a new drag will be placed at (clamped to
        /// 0 or above — no going below the deck). If a drag is already in progress, retargets its
        /// start cell to the new level and refreshes the preview so the whole run moves with it.
        /// </summary>
        private void HandleHeightInput()
        {
            int delta = 0;
            if (Keyboard.current.pageUpKey.wasPressedThisFrame)
            {
                delta = 1;
            }
            else if (Keyboard.current.pageDownKey.wasPressedThisFrame)
            {
                delta = -1;
            }

            if (delta == 0)
            {
                return;
            }

            _dragHeightLevel = Mathf.Max(0, _dragHeightLevel + delta);

            if (_dragStartCell.HasValue)
            {
                Vector3Int start = _dragStartCell.Value;
                _dragStartCell = new Vector3Int(start.x, _dragHeightLevel, start.z);
                UpdateDragPreview();
            }
        }

        /// <summary>
        /// Starts a new drag run at the cell under the cursor.
        /// </summary>
        private void BeginDrag()
        {
            Vector3Int? cell = RaycastDeckCell();
            if (!cell.HasValue)
            {
                return;
            }

            _dragStartCell = cell.Value;
            _lastPreviewCells = null;
            UpdateDragPreview();
        }

        /// <summary>
        /// Recomputes the straight-line run from the drag's start cell to the cursor's current cell
        /// (clamped to MaxDragSegments) and rebuilds the ghost preview if it changed since last
        /// frame.
        /// </summary>
        private void UpdateDragPreview()
        {
            Vector3Int? currentCell = RaycastDeckCell();
            if (!currentCell.HasValue || !_dragStartCell.HasValue)
            {
                return;
            }

            List<Vector3Int> cells = ComputeDragRun(_dragStartCell.Value, currentCell.Value);
            if (_lastPreviewCells != null && CellsEqual(cells, _lastPreviewCells))
            {
                return;
            }

            _lastPreviewCells = cells;
            RebuildPreview(cells);
        }

        /// <summary>
        /// Computes the straight cardinal-direction run of cells from start toward end, extending
        /// along whichever axis (X or Z) has the larger delta, clamped to MaxDragSegments cells.
        /// </summary>
        private List<Vector3Int> ComputeDragRun(Vector3Int start, Vector3Int end)
        {
            int deltaX = end.x - start.x;
            int deltaZ = end.z - start.z;

            Vector3Int axisStep = Mathf.Abs(deltaX) >= Mathf.Abs(deltaZ)
                ? new Vector3Int(deltaX >= 0 ? 1 : -1, 0, 0)
                : new Vector3Int(0, 0, deltaZ >= 0 ? 1 : -1);

            int length = Mathf.Clamp(Mathf.Max(Mathf.Abs(deltaX), Mathf.Abs(deltaZ)) + 1, 1, MaxDragSegments);

            List<Vector3Int> cells = new List<Vector3Int>(length);
            for (int i = 0; i < length; i++)
            {
                cells.Add(start + axisStep * i);
            }

            return cells;
        }

        /// <summary>
        /// Destroys the current ghost preview and spawns a fresh one for the given cells, tinted
        /// green if the run is valid (per IsRunValid — the same check ConfirmDrag itself gates on,
        /// so the preview never shows green for a run that would actually be rejected on release)
        /// or red otherwise. Approximates ConfirmDrag's eventual anchor/fill split (#10) by showing
        /// a BeltPlatform ghost at the run's own start and end and BeltSystem ghosts between — real
        /// anchor placement is topology-derived across the whole network post-confirm
        /// (RebuildBeltVisuals) and can differ slightly when this drag will actually extend an
        /// existing run rather than start a fresh one, but that's an acceptable approximation for a
        /// transient, non-final ghost.
        /// </summary>
        private void RebuildPreview(List<Vector3Int> cells)
        {
            DestroyPreviewInstances();

            if (_conveyorData == null || _conveyorData.beltPlatformPrefab == null || _conveyorData.beltSystemPrefab == null)
            {
                return;
            }

            bool allValid = IsRunValid(cells, CardinalDirections[_flowDirectionIndex]);
            Color tint = allValid ? ValidPreviewTint : InvalidPreviewTint;
            Quaternion rotation = FlowRotation();
            int lastIndex = cells.Count - 1;

            for (int i = 0; i <= lastIndex; i++)
            {
                if (i != 0 && i != lastIndex)
                {
                    continue;
                }

                GameObject preview = Instantiate(_conveyorData.beltPlatformPrefab, SegmentWorldPosition(cells[i]), rotation);
                TintAndDisableCollision(preview, tint);
                AttachFlowArrow(preview);
                _previewInstances.Add(preview);
            }

            for (int i = 0; i < lastIndex; i++)
            {
                Vector3 fromPos = SegmentWorldPosition(cells[i]);
                Vector3 toPos = SegmentWorldPosition(cells[i + 1]);
                Vector3 direction = (toPos - fromPos).normalized;
                Quaternion edgeRotation = Quaternion.LookRotation(direction, Vector3.up);
                bool fromIsAnchor = i == 0;
                bool toIsAnchor = i + 1 == lastIndex;
                float centerShift = (CellHalfExtent(fromIsAnchor) - CellHalfExtent(toIsAnchor)) * 0.5f;
                GameObject preview = Instantiate(_conveyorData.beltSystemPrefab, (fromPos + toPos) * 0.5f + direction * centerShift, edgeRotation);
                preview.transform.localScale = new Vector3(1f, 1f, FillTileLengthScale(fromIsAnchor, toIsAnchor));
                TintAndDisableCollision(preview, tint);
                _previewInstances.Add(preview);
            }

            _lastRunValid = allValid;
        }

        /// <summary>
        /// Disables every collider (so ghost previews never block raycasts/placement) and tints
        /// every renderer on the given instance — shared by both the BeltPlatform and BeltSystem
        /// ghost previews in RebuildPreview.
        /// </summary>
        private static void TintAndDisableCollision(GameObject instance, Color tint)
        {
            foreach (Collider previewCollider in instance.GetComponentsInChildren<Collider>())
            {
                previewCollider.enabled = false;
            }

            foreach (Renderer previewRenderer in instance.GetComponentsInChildren<Renderer>())
            {
                previewRenderer.material.color = tint;
            }
        }

        /// <summary>
        /// Cycles the belt's flow direction among the four cardinal directions and refreshes the
        /// preview's rotation to match.
        /// </summary>
        private void CycleFlowDirection(int delta)
        {
            _flowDirectionIndex = (_flowDirectionIndex + delta + CardinalDirections.Length) % CardinalDirections.Length;
            if (_lastPreviewCells != null)
            {
                RebuildPreview(_lastPreviewCells);
            }
        }

        /// <summary>
        /// Returns the Y-axis rotation facing the currently selected flow direction.
        /// </summary>
        private Quaternion FlowRotation()
        {
            Vector3Int flow = CardinalDirections[_flowDirectionIndex];
            return Quaternion.LookRotation(new Vector3(flow.x, 0f, flow.z), Vector3.up);
        }

        /// <summary>
        /// Checks whether the given run (all cells sharing the given flow direction) can be
        /// confirmed: every cell except the first and last must be free. The last cell may either be
        /// free or already hold an existing conveyor segment — a merge junction (#7) — but not a
        /// container or other occupant. The *first* cell may likewise already hold an existing
        /// segment — a branch/split origin (#17) — provided it doesn't already have this exact
        /// direction registered (a "branch" onto a direction the cell already has isn't a real
        /// branch, just a redundant no-op drag). Either exception can apply independently — a run can
        /// both branch off an existing cell at its start and merge onto another at its end in one
        /// drag. Finally, the run's flow, simulated forward through every branch of whatever existing
        /// network it merges/branches into, must never reach the Receiving dock's output cell. Shared
        /// by RebuildPreview and ConfirmDrag so the ghost preview can never show valid for a run that
        /// would actually be rejected on release.
        /// </summary>
        private bool IsRunValid(List<Vector3Int> cells, Vector3Int flowDirection)
        {
            if (cells.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                Vector3Int cell = cells[i];
                if (!_gridManager.IsOccupied(cell))
                {
                    continue;
                }

                bool isFirstCell = i == 0;
                bool isLastCell = i == cells.Count - 1;

                if (isFirstCell && _segmentFlowDirections.TryGetValue(cell, out List<Vector3Int> existingDirections)
                    && !existingDirections.Contains(flowDirection))
                {
                    continue;
                }

                if (isLastCell && _segmentFlowDirections.ContainsKey(cell))
                {
                    continue;
                }

                return false;
            }

            return !TraceReachesReceiving(cells[cells.Count - 1], flowDirection);
        }

        /// <summary>
        /// Simulates the network's flow forward from the given cell — stepping once by the given
        /// flow direction (the candidate run's own direction, since it isn't registered yet), then
        /// breadth-first exploring every branch of whatever existing segments it connects into (#17
        /// widened this from a single-path walk, since a cell can now have more than one outgoing
        /// direction) — returning true if this walk would ever reach the Receiving dock's output cell
        /// through *any* branch (backflow). Tracks visited cells so a network containing an unrelated
        /// loop elsewhere (permitted — only cycles back to Receiving are rejected) can't cause an
        /// infinite walk; degenerates to the exact same single-path result as before #17 whenever
        /// every cell along the way still has just one direction.
        /// </summary>
        private bool TraceReachesReceiving(Vector3Int startCell, Vector3Int flowDirection)
        {
            Vector3Int? outputCell = _receivingManager?.GetPrimaryOutputCell();
            if (!outputCell.HasValue)
            {
                return false;
            }

            HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
            Queue<Vector3Int> frontier = new Queue<Vector3Int>();
            Vector3Int start = startCell + flowDirection;
            visited.Add(start);
            frontier.Enqueue(start);

            while (frontier.Count > 0)
            {
                Vector3Int current = frontier.Dequeue();
                if (current == outputCell.Value)
                {
                    return true;
                }

                if (!_segmentFlowDirections.TryGetValue(current, out List<Vector3Int> directions))
                {
                    continue;
                }

                foreach (Vector3Int direction in directions)
                {
                    Vector3Int next = current + direction;
                    if (visited.Add(next))
                    {
                        frontier.Enqueue(next);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true if a real, placed conveyor segment currently occupies the given cell —
        /// used by ReceivingManager to gate spawning on an actual belt existing, so goods never
        /// appear hovering at a cell with nothing physically there to carry them.
        /// </summary>
        public bool HasSegmentAt(Vector3Int cell)
        {
            return _segmentFlowDirections.ContainsKey(cell);
        }

        /// <summary>
        /// Returns the world position a segment should sit at for the given cell — the cell's
        /// deck-surface position plus ConveyorData's hover height, giving conveyors their "floating"
        /// look rather than sitting flush on the deck.
        /// </summary>
        public Vector3 SegmentWorldPosition(Vector3Int cell)
        {
            return _gridConfig.Cell3DToWorld(cell) + Vector3.up * _conveyorData.hoverHeight;
        }

        /// <summary>
        /// Returns the world position a good riding a segment at the given cell should sit at —
        /// the segment's own position plus ConveyorData's goodsRideHeight, so goods visibly ride on
        /// top of the segment's mesh rather than sharing its exact height (where a base-pivoted
        /// prefab like Pallet1 would sink into/get hidden under the larger segment mesh). Public so
        /// other goods sources (e.g. ReceivingManager) can spawn at the correct height without
        /// duplicating this math.
        /// </summary>
        public Vector3 GoodsRestPosition(Vector3Int cell)
        {
            return SegmentWorldPosition(cell) + Vector3.up * _conveyorData.goodsRideHeight;
        }

        /// <summary>
        /// Attaches a flat arrow indicator as a child of the given segment instance (preview or
        /// confirmed), pointing along the segment's own local forward — since that's exactly what
        /// FlowRotation() aimed the segment at, a child with identity local rotation automatically
        /// points in the segment's flow direction with no separate direction math needed. Exists
        /// because TurretPlatformFlyingBlue's placeholder visual reads as symmetric and doesn't
        /// otherwise communicate which way a segment feeds.
        /// </summary>
        private void AttachFlowArrow(GameObject segmentInstance, Color? tintOverride = null)
        {
            GameObject arrow = new GameObject("FlowArrow");
            arrow.transform.SetParent(segmentInstance.transform, false);
            arrow.transform.localPosition = new Vector3(0f, FlowArrowHoverOffset, 0f);
            arrow.transform.localRotation = Quaternion.identity;

            MeshFilter filter = arrow.AddComponent<MeshFilter>();
            filter.mesh = GetOrCreateFlowArrowMesh();

            MeshRenderer arrowRenderer = arrow.AddComponent<MeshRenderer>();
            arrowRenderer.sharedMaterial = GetOrCreateFlowArrowMaterial();

            // A tint override (#17: a branch's assigned goods filter) needs its own per-instance
            // material clone — .material (not .sharedMaterial) auto-clones on first access, same
            // established pattern as PlacementManager.SetPreviewTint/TintAndDisableCollision above,
            // so tinting one branch's arrow never affects the shared default used everywhere else.
            if (tintOverride.HasValue)
            {
                arrowRenderer.material.color = tintOverride.Value;
            }
        }

        /// <summary>
        /// Deterministically derives a display color for a branch filter (#17) — null (the "Any"
        /// wildcard) always maps to the same default FlowArrowColor every other arrow already uses,
        /// so an unfiltered branch reads as "normal," not specially colored. A real filter maps to a
        /// hue derived from its instance ID — stable for the length of a session (there's no save
        /// system yet to need stability across sessions), distinct enough between different goods
        /// types to tell branches apart at a glance without needing an authored color per GoodsData.
        /// </summary>
        private static Color ColorForBranchFilter(GoodsData filter)
        {
            if (filter == null)
            {
                return FlowArrowColor;
            }

            float hue = Mathf.Abs(filter.GetInstanceID() % 360) / 360f;
            return Color.HSVToRGB(hue, 0.75f, 1f);
        }

        /// <summary>
        /// Lazily builds (once, shared across every segment) a small flat triangle mesh pointing
        /// along local +Z — a simple runtime-generated arrow, since no imported flow-direction asset
        /// exists and this is placeholder-art territory for Phase 1.
        /// </summary>
        private Mesh GetOrCreateFlowArrowMesh()
        {
            if (_flowArrowMesh != null)
            {
                return _flowArrowMesh;
            }

            Vector3 tip = new Vector3(0f, 0f, 1f);
            Vector3 baseLeft = new Vector3(-0.5f, 0f, -0.4f);
            Vector3 baseRight = new Vector3(0.5f, 0f, -0.4f);

            Mesh mesh = new Mesh { name = "ConveyorFlowArrow" };
            mesh.vertices = new[] { tip, baseRight, baseLeft, tip, baseLeft, baseRight };
            mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _flowArrowMesh = mesh;
            return _flowArrowMesh;
        }

        /// <summary>
        /// Lazily builds (once, shared across every segment) the unlit URP material used to tint
        /// the flow arrow a bright, high-contrast color against the platform's blue.
        /// </summary>
        private Material GetOrCreateFlowArrowMaterial()
        {
            if (_flowArrowMaterial != null)
            {
                return _flowArrowMaterial;
            }

            _flowArrowMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
            {
                color = FlowArrowColor
            };
            return _flowArrowMaterial;
        }

        /// <summary>
        /// Builds a map of target cell → every cell whose flow points at it, from the current
        /// _segmentFlowDirections. Shared by RebuildJunctionFeeders (which only keeps entries with
        /// 2+ feeders) and IsAnchorCell (which needs the full per-cell feeder list, including
        /// exactly-one-feeder cells, to detect bends).
        /// </summary>
        private Dictionary<Vector3Int, List<Vector3Int>> BuildFeedersByTarget()
        {
            Dictionary<Vector3Int, List<Vector3Int>> feedersByTarget = new Dictionary<Vector3Int, List<Vector3Int>>();
            foreach (KeyValuePair<Vector3Int, List<Vector3Int>> entry in _segmentFlowDirections)
            {
                foreach (Vector3Int direction in entry.Value)
                {
                    Vector3Int target = entry.Key + direction;
                    if (!feedersByTarget.TryGetValue(target, out List<Vector3Int> feeders))
                    {
                        feeders = new List<Vector3Int>();
                        feedersByTarget[target] = feeders;
                    }

                    feeders.Add(entry.Key);
                }
            }

            return feedersByTarget;
        }

        /// <summary>
        /// Returns true if the given registered cell should show a real BeltPlatform anchor rather
        /// than just being part of a plain fill strip (#10 follow-up) — a genuine start (nothing
        /// feeds it), a genuine end (none of its own outgoing directions lead to another registered
        /// cell — a dead end, or a dock hand-off), a junction (2+ feeders), a split (2+ of its own
        /// outgoing directions, #17), the immediate destination of a split (#17 — see below), or a
        /// bend (its single feeder's flow direction into this cell differs from this cell's own
        /// single outgoing direction, i.e. the run turns here). Every other cell — exactly one
        /// feeder (not itself a split), exactly one outgoing direction, continuing straight — is a
        /// plain interior cell of a straight run, regardless of which drag originally placed it.
        /// This is what makes two separately-confirmed drags that end up adjacent collapse into one
        /// continuous-looking run instead of showing a redundant platform at the seam.
        ///
        /// A split's immediate destination cells always get a real anchor even when nothing else
        /// about them would otherwise qualify (a plain straight continuation) — not just for visual
        /// clarity about where each branch leads, but because it's load-bearing: BeltSystem fill
        /// tiles carry no collider at all (the designer moved every Fin mesh onto BeltPlatform during
        /// #10), so a branch destination that stayed a plain fill cell could never actually be
        /// clicked to assign its filter (#17) — there would be nothing there for a raycast to hit.
        /// </summary>
        private bool IsAnchorCell(Vector3Int cell, Dictionary<Vector3Int, List<Vector3Int>> feedersByTarget)
        {
            List<Vector3Int> flows = _segmentFlowDirections[cell];
            if (flows.Count > 1)
            {
                return true;
            }

            feedersByTarget.TryGetValue(cell, out List<Vector3Int> feeders);
            int feederCount = feeders?.Count ?? 0;

            if (feederCount >= 1)
            {
                foreach (Vector3Int feeder in feeders)
                {
                    if (_segmentFlowDirections[feeder].Count > 1)
                    {
                        return true;
                    }
                }
            }

            Vector3Int flow = flows[0];
            bool hasValidOut = _segmentFlowDirections.ContainsKey(cell + flow);

            if (feederCount != 1 || !hasValidOut)
            {
                return true;
            }

            // The specific direction the sole feeder sends toward this cell — not just "does the
            // feeder have this exact direction somewhere in its list" (#17: a feeder can now have
            // more than one outgoing direction, only one of which necessarily targets this cell).
            Vector3Int feederDirectionIntoThisCell = cell - feeders[0];
            return feederDirectionIntoThisCell != flow;
        }

        /// <summary>
        /// Returns how much a BeltSystem fill tile's Z scale should be to exactly span the real open
        /// gap for an edge whose ends are (or aren't) real BeltPlatform anchors — an anchor's own
        /// fins eat AnchorFinInsetPerEnd meters into the 4m cell-to-cell gap on its side, a plain
        /// fill cell eats nothing. Used by both CreateFillTile (real placement) and RebuildPreview
        /// (ghost, using its own simpler start/end-of-drag approximation of anchor-ness).
        /// </summary>
        /// <summary>
        /// How far a cell's own geometry eats into the gap on a given side, in meters — an anchor's
        /// fins claim AnchorFinInsetPerEnd from its own center; a plain fill cell claims nothing
        /// (a tile may approach all the way to its exact center). Shared by CreateFillTile's length
        /// and (critically) its position calculation — see the comment there for why both matter.
        /// </summary>
        private float CellHalfExtent(bool isAnchor)
        {
            return isAnchor ? AnchorFinInsetPerEnd : 0f;
        }

        private float FillTileLengthScale(bool fromIsAnchor, bool toIsAnchor)
        {
            float availableGap = _gridConfig.cellSize - CellHalfExtent(fromIsAnchor) - CellHalfExtent(toIsAnchor);
            return Mathf.Max(0.05f, availableGap / BeltSystemNativeLength);
        }

        /// <summary>
        /// Instantiates one BeltSystem fill tile bridging two flow-connected cells, scaled along its
        /// own local Z to the real open gap between them (FillTileLengthScale) rather than assumed
        /// to always be a uniform, unscaled 4m — this is what fixed "gaps on longer drags": an
        /// unscaled tile was sized to fit snugly between two anchors' fins, which left a visible
        /// short-fall on every plain interior edge of a longer run, where the full 4m gap is open
        /// with no fins to hide a shortfall against.
        /// **Also positioned asymmetrically, not at the flat midpoint** (post-fix, same-day: the
        /// length fix alone still left a real, measured ~0.5m gap wherever an anchor-adjacent edge
        /// met a plain interior edge — found via direct pixel sampling of a rendered frame, since
        /// bounds-only spot checks on interior-to-interior pairs had missed it). Centering every
        /// tile at the flat midpoint of its two cells only closes gaps when both ends apply the same
        /// inset (anchor-anchor or fill-fill) — an anchor-to-fill edge and its fill-to-fill neighbor
        /// then disagree about where the shared fill cell's own boundary sits, since the flat-midpoint
        /// anchor-fill tile stops CellHalfExtent(anchor) short of the true cell-to-cell midpoint on
        /// its own side too, not just the anchor's side. The correct boundary a tile actually owns on
        /// each end is `cellCenter ± CellHalfExtent(thatCell)`, not the plain midpoint — shifting the
        /// tile's center by half the difference between the two ends' extents reproduces exactly that,
        /// while leaving the already-correct symmetric cases (anchor-anchor, fill-fill) untouched.
        /// </summary>
        private GameObject CreateFillTile(Vector3Int fromCell, Vector3Int toCell, bool fromIsAnchor, bool toIsAnchor)
        {
            Vector3 fromPos = SegmentWorldPosition(fromCell);
            Vector3 toPos = SegmentWorldPosition(toCell);
            Vector3 direction = (toPos - fromPos).normalized;
            Quaternion edgeRotation = Quaternion.LookRotation(direction, Vector3.up);

            float centerShift = (CellHalfExtent(fromIsAnchor) - CellHalfExtent(toIsAnchor)) * 0.5f;
            Vector3 tileCenter = (fromPos + toPos) * 0.5f + direction * centerShift;

            GameObject tile = Instantiate(_conveyorData.beltSystemPrefab, tileCenter, edgeRotation);
            tile.transform.localScale = new Vector3(1f, 1f, FillTileLengthScale(fromIsAnchor, toIsAnchor));

            // Tint this tile's arrow by its destination's assigned filter, but only if that
            // destination is actually a branch of a real split (#17) — an untinted default arrow
            // everywhere else avoids implying a filter is in effect on a plain, unsplit run.
            Color? branchTint = IsSplitBranchDestination(toCell)
                ? ColorForBranchFilter(_branchFilters.TryGetValue(toCell, out GoodsData filter) ? filter : null)
                : (Color?)null;
            AttachFlowArrow(tile, branchTint);

            return tile;
        }

        /// <summary>
        /// Returns true if the given cell is the immediate destination of some other cell's split
        /// (#17) — i.e. some registered cell has 2+ outgoing directions and one of them lands here.
        /// Used to decide whether a cell's arrow should reflect a branch filter and whether clicking
        /// it should cycle one; a plain fill/interior cell (even one fed by an ordinary single-output
        /// cell) is never a branch destination. Scans the whole network on demand — only called from
        /// rare, player-driven events (a rebuild, a click), never per-frame.
        /// </summary>
        private bool IsSplitBranchDestination(Vector3Int cell)
        {
            foreach (KeyValuePair<Vector3Int, List<Vector3Int>> entry in _segmentFlowDirections)
            {
                if (entry.Value.Count <= 1)
                {
                    continue;
                }

                foreach (Vector3Int direction in entry.Value)
                {
                    if (entry.Key + direction == cell)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Destroys every current BeltPlatform anchor and BeltSystem fill-tile instance and rebuilds
        /// both from scratch, purely derived from current network topology (#10 follow-up) —
        /// replacing the earlier design where a drag's own start/end cell always got its own anchor
        /// at confirm time, which put a redundant second platform at every point a player extended
        /// an existing run with a new drag. Deliberately network-wide rather than scoped to whatever
        /// span was just placed/removed, exactly like RebuildDockConnectors below and
        /// RebuildJunctionFeeders/RebuildMainLine (#7) — extending a run across two or more separate
        /// drags needs the join re-evaluated too, and that join doesn't belong to any single span's
        /// own cell list. Anchors and fill tiles aren't individually removable and aren't tracked in
        /// any PlacedSpan — right-click only ever targets a currently-shown anchor and removes the
        /// whole span that owns its cell (see HandleRemoveInput/_cellOwnership); removing a span's
        /// cells and calling this again naturally re-derives anchor status for whatever's left.
        /// </summary>
        private void RebuildBeltVisuals()
        {
            foreach (GameObject instance in _segmentInstances.Values)
            {
                if (instance != null)
                {
                    Destroy(instance);
                }
            }

            _segmentInstances.Clear();

            foreach (GameObject tile in _fillTiles)
            {
                if (tile != null)
                {
                    Destroy(tile);
                }
            }

            _fillTiles.Clear();

            Dictionary<Vector3Int, List<Vector3Int>> feedersByTarget = BuildFeedersByTarget();
            HashSet<Vector3Int> anchorCells = new HashSet<Vector3Int>();

            foreach (Vector3Int cell in _segmentFlowDirections.Keys)
            {
                if (IsAnchorCell(cell, feedersByTarget))
                {
                    anchorCells.Add(cell);
                }
            }

            foreach (Vector3Int cell in anchorCells)
            {
                // A split cell's own shared anchor faces its first-registered direction and stays
                // untinted (#17) — it can't represent every one of its branches' filters at once. A
                // cell forced into anchor status *because* it's a branch destination (see
                // IsAnchorCell) tints by its own assigned filter instead — that's the actual
                // clickable point a player interacts with to set it.
                Vector3Int flow = _segmentFlowDirections[cell][0];
                Quaternion rotation = Quaternion.LookRotation(new Vector3(flow.x, 0f, flow.z), Vector3.up);
                GameObject anchor = Instantiate(_conveyorData.beltPlatformPrefab, SegmentWorldPosition(cell), rotation);

                Color? branchTint = IsSplitBranchDestination(cell)
                    ? ColorForBranchFilter(_branchFilters.TryGetValue(cell, out GoodsData filter) ? filter : null)
                    : (Color?)null;
                AttachFlowArrow(anchor, branchTint);

                _segmentInstances[cell] = anchor;
            }

            foreach (KeyValuePair<Vector3Int, List<Vector3Int>> entry in _segmentFlowDirections)
            {
                foreach (Vector3Int direction in entry.Value)
                {
                    Vector3Int nextCell = entry.Key + direction;
                    if (!_segmentFlowDirections.ContainsKey(nextCell))
                    {
                        continue;
                    }

                    bool fromIsAnchor = anchorCells.Contains(entry.Key);
                    bool toIsAnchor = anchorCells.Contains(nextCell);
                    _fillTiles.Add(CreateFillTile(entry.Key, nextCell, fromIsAnchor, toIsAnchor));
                }
            }
        }

        /// <summary>
        /// Destroys every current dock-bridging energy-connector instance and rebuilds both docks'
        /// (#10): the dock boundary is the one place this project still bridges procedurally, since
        /// ConnectionPoint sits at whatever offset the artist placed it inside each hand-built
        /// compound dock prefab, not a clean grid-cell distance a real BeltSystem tile could just be
        /// dropped into. Rebuilt wholesale on any placement/removal (simple, correct, fine at Phase 1
        /// scale) since a new span can complete a dock connection formed across two separate drags,
        /// not just within the one just confirmed — same reasoning as RebuildBeltVisuals above.
        /// </summary>
        private void RebuildDockConnectors()
        {
            foreach (GameObject connector in _energyConnectors)
            {
                if (connector != null)
                {
                    Destroy(connector);
                }
            }

            _energyConnectors.Clear();

            ConnectLastSegmentToDock(_receivingManager?.GetPrimaryOutputCell(), _receivingManager?.GetPrimaryConnectionPoint());
            ConnectLastSegmentToDock(_shippingManager?.GetPrimaryInputCell(), _shippingManager?.GetPrimaryConnectionPoint());
        }

        /// <summary>
        /// Bridges the visible gap between a dock's own connection-point piece and its hand-off
        /// cell's real belt geometry — the two docks are structurally opposite here, so this
        /// branches on which shape applies. Receiving's output cell IS ConnectionPoint's own cell
        /// (goods spawn directly there, and ReceivingManager.HasSegmentAt gates spawning on a real
        /// segment sitting AT it — see ReceivingManager) — bridged with one direct hop (connector →
        /// that segment, which the two now sharing a cell makes very short). Shipping's input cell
        /// is also ConnectionPoint's own cell, but must deliberately stay real-segment-free (goods
        /// vanish into the dock there instead of continuing to move via AdvanceGoods — see
        /// ShippingManager), so the required real segment sits one cell further out feeding into it
        /// — bridged with two hops (feeder segment → hand-off cell → connector). No-ops if the
        /// relevant real segment doesn't exist yet or the dock hasn't registered.
        /// The dock's own ConnectionPoint sits at whatever height the artist pivoted that piece at
        /// (never tuned to match a belt's hover height), so bridging straight to its raw position
        /// tilts the connector like a ramp — subtle numerically, but visible as a skewed/trapezoidal
        /// quad from a low, oblique gameplay camera angle. Flattened to the belt's own hover height
        /// instead, since this is a decorative light strip, not something that needs to physically
        /// touch the connector mesh's exact pivot.
        /// </summary>
        /// <param name="handOffCell">The dock's registered cell — Receiving's output cell or
        /// Shipping's input cell.</param>
        /// <param name="connectionPoint">The dock's own connector piece to bridge into.</param>
        private void ConnectLastSegmentToDock(Vector3Int? handOffCell, Transform connectionPoint)
        {
            if (!handOffCell.HasValue || connectionPoint == null)
            {
                return;
            }

            Vector3Int cell = handOffCell.Value;

            if (_segmentFlowDirections.ContainsKey(cell))
            {
                Vector3 segmentPos = SegmentWorldPosition(cell);
                Vector3 flattenedConnectorPos = FlattenToHeight(connectionPoint.position, segmentPos.y);
                _energyConnectors.Add(CreateEnergyConnector(flattenedConnectorPos, segmentPos));
                return;
            }

            foreach (KeyValuePair<Vector3Int, List<Vector3Int>> entry in _segmentFlowDirections)
            {
                bool feedsHandOffCell = false;
                foreach (Vector3Int direction in entry.Value)
                {
                    if (entry.Key + direction == cell)
                    {
                        feedsHandOffCell = true;
                        break;
                    }
                }

                if (!feedsHandOffCell)
                {
                    continue;
                }

                Vector3 lastSegmentPos = SegmentWorldPosition(entry.Key);
                Vector3 handOffPos = SegmentWorldPosition(cell);
                Vector3 flattenedConnectorPos = FlattenToHeight(connectionPoint.position, handOffPos.y);
                _energyConnectors.Add(CreateEnergyConnector(lastSegmentPos, handOffPos));
                _energyConnectors.Add(CreateEnergyConnector(handOffPos, flattenedConnectorPos));
                return;
            }
        }

        /// <summary>
        /// Returns the given position with its Y replaced, keeping X/Z unchanged — used to keep a
        /// dock-bridging energy connector level with the belt's own hover height rather than tilting
        /// toward the dock's own connector-piece pivot height.
        /// </summary>
        private static Vector3 FlattenToHeight(Vector3 position, float height)
        {
            return new Vector3(position.x, height, position.z);
        }

        /// <summary>
        /// Creates a flat emissive quad bridging the gap between two connected segments' positions —
        /// the "sci-fi energy conduit" look explaining why goods travel without visible rollers,
        /// static for this first pass (an animated flowing version is a likely future iteration).
        /// The mesh's own length (EnergyConnectorLength) assumes a one-cell gap, which every
        /// segment-to-segment connector actually is — but a dock's ConnectionPoint can sit closer
        /// than that to its hand-off cell (ConnectLastSegmentToDock), so the quad is scaled down
        /// (never up) to the real distance when that's shorter, or it would overshoot both
        /// endpoints and visibly cut into the dock structure before the real gap ends.
        /// </summary>
        private GameObject CreateEnergyConnector(Vector3 from, Vector3 to)
        {
            GameObject connector = new GameObject("EnergyConnector");
            connector.transform.position = (from + to) * 0.5f;
            connector.transform.rotation = Quaternion.LookRotation((to - from).normalized, Vector3.up);

            float actualDistance = Vector3.Distance(from, to);
            float lengthScale = Mathf.Min(1f, actualDistance / EnergyConnectorLength);
            connector.transform.localScale = new Vector3(1f, 1f, lengthScale);

            MeshFilter filter = connector.AddComponent<MeshFilter>();
            filter.mesh = GetOrCreateEnergyConnectorMesh();

            MeshRenderer connectorRenderer = connector.AddComponent<MeshRenderer>();
            connectorRenderer.sharedMaterial = GetOrCreateEnergyConnectorMaterial();

            return connector;
        }

        /// <summary>
        /// Lazily builds (once, shared across every connector) a flat double-sided quad mesh, long
        /// axis along local +Z, sized by EnergyConnectorLength/Width.
        /// </summary>
        private Mesh GetOrCreateEnergyConnectorMesh()
        {
            if (_energyConnectorMesh != null)
            {
                return _energyConnectorMesh;
            }

            float halfWidth = EnergyConnectorWidth * 0.5f;
            float halfLength = EnergyConnectorLength * 0.5f;
            Vector3 a = new Vector3(-halfWidth, 0f, -halfLength);
            Vector3 b = new Vector3(halfWidth, 0f, -halfLength);
            Vector3 c = new Vector3(halfWidth, 0f, halfLength);
            Vector3 d = new Vector3(-halfWidth, 0f, halfLength);

            Mesh mesh = new Mesh { name = "ConveyorEnergyConnector" };
            mesh.vertices = new[] { a, b, c, d, a, d, c, b };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _energyConnectorMesh = mesh;
            return _energyConnectorMesh;
        }

        /// <summary>
        /// Lazily builds (once, shared across every connector) an emissive URP Lit material — this
        /// scene has Bloom enabled, so a genuinely HDR emissive color actually glows rather than just
        /// reading as a flat bright fill.
        /// </summary>
        private Material GetOrCreateEnergyConnectorMaterial()
        {
            if (_energyConnectorMaterial != null)
            {
                return _energyConnectorMaterial;
            }

            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = EnergyConnectorColor };
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", EnergyConnectorColor * EnergyConnectorEmissionIntensity);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            _energyConnectorMaterial = material;
            return _energyConnectorMaterial;
        }

        /// <summary>
        /// Adds an externally-spawned GoodsAgent (e.g. from ReceivingManager) to this manager's
        /// per-frame movement tracking, so it advances along connected conveyor segments exactly
        /// like a debug-spawned one. The caller is responsible for instantiating and initializing
        /// the agent first.
        /// </summary>
        public void RegisterGoodsAgent(GoodsAgent agent)
        {
            _activeGoods.Add(agent);
        }

        /// <summary>
        /// Confirms the current drag run if IsRunValid accepts it, registering every cell with
        /// GridManager's shared occupancy and _segmentFlowDirections as one PlacedSpan (#10) —
        /// except the run's last cell, if it's a merge onto an already-existing conveyor cell (#7):
        /// that cell (and whatever flow direction it already has) is left completely untouched and
        /// excluded from the new span, rather than being replaced. The run's *first* cell (#17) gets
        /// the mirror-image treatment if it's a branch origin: its pre-existing direction(s) are left
        /// completely untouched too, but the new direction this drag adds is appended to that cell's
        /// list and recorded in the span's OwnDirection map — not added to Cells, since Cells means
        /// "cells this span newly registered," which the origin wasn't (it still gets an OwnDirection
        /// entry, so removing this span later correctly takes back only its own new direction).
        /// Deliberately does NOT decide here which cells get a real BeltPlatform anchor versus a
        /// plain fill tile — that's a topology-derived property of the whole network, rebuilt
        /// wholesale afterward (RebuildBeltVisuals) so extending an existing run with a new drag
        /// re-evaluates the seam between them instead of always planting a redundant anchor at the
        /// new drag's own start. The placement event is raised per newly-registered cell only after
        /// that rebuild, so it can report whichever real visual instance (if any) ended up at that
        /// cell. If the run was invalid, it's cancelled without placing anything.
        /// </summary>
        private void ConfirmDrag()
        {
            if (_lastPreviewCells != null && _lastRunValid)
            {
                Vector3Int flow = CardinalDirections[_flowDirectionIndex];
                List<Vector3Int> cells = _lastPreviewCells;

                PlacedSpan span = new PlacedSpan();

                for (int i = 0; i < cells.Count; i++)
                {
                    Vector3Int cell = cells[i];

                    if (_segmentFlowDirections.TryGetValue(cell, out List<Vector3Int> existingDirections))
                    {
                        if (i == 0 && !existingDirections.Contains(flow))
                        {
                            existingDirections.Add(flow);
                            span.OwnDirection[cell] = flow;
                        }

                        continue;
                    }

                    _segmentFlowDirections[cell] = new List<Vector3Int> { flow };
                    _gridManager.Register(cell);
                    span.Cells.Add(cell);
                    span.OwnDirection[cell] = flow;
                    _cellOwnership[cell] = span;
                }

                _placedSpans.Add(span);
                RebuildBeltVisuals();
                RebuildDockConnectors();
                RebuildNetworkTopology();

                foreach (Vector3Int cell in span.Cells)
                {
                    GameObject placedObject = _segmentInstances.TryGetValue(cell, out GameObject instance) ? instance : gameObject;
                    _eventChannel?.RaisePiecePlaced(placedObject, cell);
                }
            }
            else if (_lastPreviewCells != null)
            {
                Debug.LogWarning(
                    "ConveyorManager: drag run overlaps an occupied cell, merges invalidly, or would route back to Receiving — placement cancelled.");
            }

            CancelDrag();
        }

        /// <summary>
        /// Clears the in-progress drag's preview and state without placing anything.
        /// </summary>
        private void CancelDrag()
        {
            DestroyPreviewInstances();
            _dragStartCell = null;
            _lastPreviewCells = null;
        }

        /// <summary>
        /// Destroys all current ghost preview instances.
        /// </summary>
        private void DestroyPreviewInstances()
        {
            foreach (GameObject preview in _previewInstances)
            {
                if (preview != null)
                {
                    Destroy(preview);
                }
            }

            _previewInstances.Clear();
        }

        /// <summary>
        /// Right-click removes the whole placed span (#10) the cursor is aiming at (physics
        /// raycast) — always the anchor-to-anchor unit a drag confirmed, never a single cell within
        /// it. Only ever targets a currently-shown BeltPlatform anchor (FindCellForHitObject only
        /// searches _segmentInstances, which RebuildBeltVisuals only ever populates with anchor
        /// cells); fill tiles are purely decorative and rebuilt network-wide afterward, so there's
        /// nothing span-specific to destroy for them here — clicking bare belt visuals between two
        /// anchors is a deliberate no-op. Only active outside placement mode. Removal goes through
        /// RemoveSpanDirection for every (cell, direction) this span owns (#17) — including a branch
        /// origin cell it doesn't fully own — so removing one branch of a split can never disturb a
        /// direction some other span still has registered at a shared cell.
        /// </summary>
        private void HandleRemoveInput()
        {
            if (!Mouse.current.rightButton.wasPressedThisFrame || _mainCamera == null)
            {
                return;
            }

            Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit))
            {
                return;
            }

            Vector3Int? hitCell = FindCellForHitObject(hit.collider.gameObject);
            if (!hitCell.HasValue || !_cellOwnership.TryGetValue(hitCell.Value, out PlacedSpan span))
            {
                return;
            }

            foreach (KeyValuePair<Vector3Int, Vector3Int> entry in span.OwnDirection)
            {
                RemoveSpanDirection(entry.Key, entry.Value, span);
            }

            _placedSpans.Remove(span);
            RebuildBeltVisuals();
            RebuildDockConnectors();
            RebuildNetworkTopology();
        }

        /// <summary>
        /// Removes exactly one (cell, direction) pair that the given span is responsible for (#17) —
        /// takes just that direction out of the cell's list, only fully unregistering the cell
        /// (GridManager, its branch filter, any GoodsAgent riding it) once no direction remains there
        /// at all. This is what keeps removing one span (a plain run, or one branch of a split) from
        /// ever disturbing a *different* direction a different span still has registered at a cell
        /// they happen to share (a split's shared origin cell, most notably).
        /// </summary>
        private void RemoveSpanDirection(Vector3Int cell, Vector3Int direction, PlacedSpan owningSpan)
        {
            if (_segmentFlowDirections.TryGetValue(cell, out List<Vector3Int> directions))
            {
                directions.Remove(direction);
                if (directions.Count == 0)
                {
                    _segmentFlowDirections.Remove(cell);
                    _gridManager.Unregister(cell);
                    _branchFilters.Remove(cell);
                    DestroyGoodsAtCell(cell);
                }
            }

            if (_cellOwnership.TryGetValue(cell, out PlacedSpan owner) && owner == owningSpan)
            {
                _cellOwnership.Remove(cell);
            }
        }

        /// <summary>
        /// Returns the cell whose BeltPlatform anchor the given hit object belongs to (itself or a
        /// child of it), or null if the hit object isn't a tracked anchor — _segmentInstances only
        /// ever holds anchor cells, since RebuildBeltVisuals only instantiates a tracked instance for
        /// cells that are topologically real anchors (see IsAnchorCell); fill tiles are rebuilt
        /// network-wide and never owned by a specific cell. Used by both right-click removal and the
        /// Editor-only debug spawn.
        /// </summary>
        private Vector3Int? FindCellForHitObject(GameObject hitObject)
        {
            foreach (KeyValuePair<Vector3Int, GameObject> entry in _segmentInstances)
            {
                if (entry.Value == hitObject || hitObject.transform.IsChildOf(entry.Value.transform))
                {
                    return entry.Key;
                }
            }

            return null;
        }

        /// <summary>
        /// Every cell currently holding a registered conveyor segment — read-only snapshot for
        /// WeightManager (#14) to find which conveyor cells sit on a given platform segment.
        /// </summary>
        public IEnumerable<Vector3Int> GetSegmentCells()
        {
            return _segmentFlowDirections.Keys;
        }

        /// <summary>
        /// Removes exactly the given cells from the conveyor network — unlike HandleRemoveInput
        /// (which always removes a whole PlacedSpan together), this operates on an arbitrary subset,
        /// since a platform segment collapse (#14) only destroys the conveyor cells physically on
        /// the failed tile, which may be a partial slice of a longer run that continues onto healthy
        /// tiles elsewhere — reusing the whole-span removal path would destroy the entire run,
        /// including parts nowhere near the failure. Unlike right-click removal, this wipes a given
        /// cell *entirely* (every direction registered there, regardless of which span(s) contributed
        /// them) — correct for "this physical tile is gone," a stronger guarantee than "one span was
        /// removed." Every PlacedSpan is swept afterward to drop any Cells/OwnDirection entries that
        /// referenced a wiped cell (#17: a span's own cells or branch origin can be wiped from
        /// underneath it by a platform collapse elsewhere, not just by its own removal); a span left
        /// with nothing owned is dropped. Rebuilds visuals/dock connectors/topology once afterward if
        /// anything was actually removed — a no-op call (no matching cells) does nothing.
        /// </summary>
        public void RemoveCells(IEnumerable<Vector3Int> cellsToRemove)
        {
            HashSet<Vector3Int> wiped = new HashSet<Vector3Int>(cellsToRemove);
            bool anyRemoved = false;

            foreach (Vector3Int cell in wiped)
            {
                if (!_segmentFlowDirections.ContainsKey(cell))
                {
                    continue;
                }

                _segmentFlowDirections.Remove(cell);
                _branchFilters.Remove(cell);
                _gridManager.Unregister(cell);
                DestroyGoodsAtCell(cell);
                _cellOwnership.Remove(cell);
                anyRemoved = true;
            }

            if (!anyRemoved)
            {
                return;
            }

            foreach (PlacedSpan span in _placedSpans)
            {
                span.Cells.RemoveAll(wiped.Contains);

                List<Vector3Int> ownedCellsToDrop = null;
                foreach (Vector3Int ownedCell in span.OwnDirection.Keys)
                {
                    if (!wiped.Contains(ownedCell))
                    {
                        continue;
                    }

                    ownedCellsToDrop ??= new List<Vector3Int>();
                    ownedCellsToDrop.Add(ownedCell);
                }

                if (ownedCellsToDrop == null)
                {
                    continue;
                }

                foreach (Vector3Int ownedCell in ownedCellsToDrop)
                {
                    span.OwnDirection.Remove(ownedCell);
                }
            }

            _placedSpans.RemoveAll(span => span.OwnDirection.Count == 0);

            RebuildBeltVisuals();
            RebuildDockConnectors();
            RebuildNetworkTopology();
        }

        /// <summary>
        /// Destroys and untracks every GoodsAgent currently sitting at the given cell — called when
        /// the segment supporting them is removed, since a good with no belt beneath it is lost
        /// rather than left floating in place forever (AdvanceGoods only ever moves an agent off a
        /// cell it can find a registered flow direction for).
        /// </summary>
        private void DestroyGoodsAtCell(Vector3Int cell)
        {
            for (int i = _activeGoods.Count - 1; i >= 0; i--)
            {
                if (_activeGoods[i].CurrentCell == cell)
                {
                    GoodsAgent agent = _activeGoods[i];
                    _activeGoods.RemoveAt(i);
                    Destroy(agent.gameObject);
                }
            }
        }

        /// <summary>
        /// Recomputes both pieces of network-topology-derived state (#7) — the main line (cells
        /// reached tracing forward from the Receiving dock's output cell) and the junction feeder
        /// map (cells with 2+ upstream feeders) — called after every placement or removal so
        /// junction arbitration and backflow checks always work against current topology. Also
        /// prunes any goods-target claim for a cell that's no longer a junction, so a stale claim
        /// can't block real contention if that cell becomes a junction again later.
        /// </summary>
        private void RebuildNetworkTopology()
        {
            RebuildMainLine();
            RebuildJunctionFeeders();

            List<Vector3Int> staleClaims = new List<Vector3Int>();
            foreach (Vector3Int claimedCell in _claimedTargets.Keys)
            {
                if (!_junctionFeeders.ContainsKey(claimedCell))
                {
                    staleClaims.Add(claimedCell);
                }
            }

            foreach (Vector3Int staleCell in staleClaims)
            {
                _claimedTargets.Remove(staleCell);
            }
        }

        /// <summary>
        /// Rebuilds the main line: every cell reachable by tracing forward from the Receiving dock's
        /// output cell through connected segments' flow directions. Widened from a single-path walk
        /// to a breadth-first reachability set (#17) — once a cell can have more than one outgoing
        /// direction, "the main line" has to mean "every cell reachable through any branch," not one
        /// arbitrarily-chosen path; this is what PickFavoredFeeder's own main-line-priority check
        /// (_mainLineCells.Contains) still reads. Degenerates to the exact same single-path result as
        /// before #17 whenever every cell along the way still has just one direction. Tracks visited
        /// cells so a network containing a loop can't cause an infinite walk.
        /// </summary>
        private void RebuildMainLine()
        {
            _mainLineCells.Clear();

            Vector3Int? outputCell = _receivingManager?.GetPrimaryOutputCell();
            if (!outputCell.HasValue)
            {
                return;
            }

            Queue<Vector3Int> frontier = new Queue<Vector3Int>();
            _mainLineCells.Add(outputCell.Value);
            frontier.Enqueue(outputCell.Value);

            while (frontier.Count > 0)
            {
                Vector3Int current = frontier.Dequeue();
                if (!_segmentFlowDirections.TryGetValue(current, out List<Vector3Int> directions))
                {
                    continue;
                }

                foreach (Vector3Int direction in directions)
                {
                    Vector3Int next = current + direction;
                    if (_mainLineCells.Add(next))
                    {
                        frontier.Enqueue(next);
                    }
                }
            }
        }

        /// <summary>
        /// Rebuilds the junction feeder map: every cell fed by 2+ other segments' flow directions,
        /// mapped to the list of those feeder cells. A "junction" here is purely this derived
        /// property of the flow-direction graph — no separate placement step creates one.
        /// </summary>
        private void RebuildJunctionFeeders()
        {
            Dictionary<Vector3Int, List<Vector3Int>> feedersByTarget = BuildFeedersByTarget();

            _junctionFeeders.Clear();
            foreach (KeyValuePair<Vector3Int, List<Vector3Int>> entry in feedersByTarget)
            {
                if (entry.Value.Count > 1)
                {
                    _junctionFeeders[entry.Key] = entry.Value;
                }
            }
        }

        /// <summary>
        /// Advances every active GoodsAgent one step: if it's sitting on a conveyor cell whose flow
        /// leads to another conveyor cell that isn't already occupied by a different GoodsAgent (and,
        /// for a junction cell, that this agent currently holds the claim on — see
        /// ResolveJunctionClaims), moves it toward that cell's world position at the configured belt
        /// speed. If the next cell isn't a conveyor segment, attempts to hand the agent off to
        /// whatever's there instead (TryHandOffToDestination — a container or a Shipping dock) —
        /// accepted removes it from tracking, rejected (or nothing there at all) just holds it in
        /// place, tried again next frame. Which next cell a multi-direction (split) cell sends an
        /// agent toward is resolved once via ResolveNextCell and cached per-agent (#17) — the same
        /// persist-until-arrival shape #7's junction claims already established, needed for the same
        /// reason: CurrentCell doesn't change until arrival, so recomputing the choice fresh every
        /// frame could flip an agent's target mid-flight. Iterates backwards since a successful
        /// hand-off removes from _activeGoods mid-loop.
        /// </summary>
        private void AdvanceGoods()
        {
            if (_conveyorData == null || _gridConfig == null)
            {
                return;
            }

            ResolveJunctionClaims();

            for (int i = _activeGoods.Count - 1; i >= 0; i--)
            {
                GoodsAgent agent = _activeGoods[i];
                if (!_segmentFlowDirections.TryGetValue(agent.CurrentCell, out List<Vector3Int> flowDirections))
                {
                    continue;
                }

                Vector3Int? nextCellChoice = ResolveNextCell(agent, flowDirections);
                if (!nextCellChoice.HasValue)
                {
                    continue;
                }

                Vector3Int nextCell = nextCellChoice.Value;
                if (!_segmentFlowDirections.ContainsKey(nextCell))
                {
                    _agentBranchChoice.Remove(agent);
                    TryHandOffToDestination(agent, nextCell, i);
                    continue;
                }

                if (IsCellOccupiedByOtherAgent(nextCell, agent))
                {
                    continue;
                }

                if (_junctionFeeders.ContainsKey(nextCell)
                    && (!_claimedTargets.TryGetValue(nextCell, out GoodsAgent claimant) || claimant != agent))
                {
                    continue;
                }

                Vector3 targetPosition = GoodsRestPosition(nextCell);
                float maxDistanceDelta = _conveyorData.beltSpeed * _gridConfig.cellSize * Time.deltaTime;
                agent.transform.position = Vector3.MoveTowards(agent.transform.position, targetPosition, maxDistanceDelta);

                if (Vector3.Distance(agent.transform.position, targetPosition) < 0.001f)
                {
                    _agentBranchChoice.Remove(agent);
                    agent.SetCurrentCell(nextCell);
                    _claimedTargets.Remove(nextCell);
                }
            }
        }

        /// <summary>
        /// Resolves which of a cell's possibly-several outgoing directions the given agent should
        /// move toward next (#17). A plain single-direction cell (the overwhelming majority, and
        /// every cell that existed before #17) returns that one direction immediately — this fast
        /// path is what keeps pre-#17 behavior byte-identical when no split is involved. For an
        /// actual split, reuses a previously-cached choice for this agent if it's still one of the
        /// cell's live directions (an agent must not switch targets mid-transit — see AdvanceGoods'
        /// own doc comment), otherwise picks fresh via ChooseBranchDirection and caches it.
        /// </summary>
        private Vector3Int? ResolveNextCell(GoodsAgent agent, List<Vector3Int> directions)
        {
            if (directions.Count == 1)
            {
                return agent.CurrentCell + directions[0];
            }

            if (_agentBranchChoice.TryGetValue(agent, out Vector3Int cached) && directions.Contains(cached))
            {
                return agent.CurrentCell + cached;
            }

            Vector3Int? chosen = ChooseBranchDirection(agent.CurrentCell, directions, agent.Data);
            if (!chosen.HasValue)
            {
                return null;
            }

            _agentBranchChoice[agent] = chosen.Value;
            return agent.CurrentCell + chosen.Value;
        }

        /// <summary>
        /// Picks which outgoing direction from a splitting cell matches the given good's type — a
        /// branch whose destination cell has no assigned filter ("Any") matches everything (#17's
        /// wildcard). Returns null if no branch currently accepts this good, in which case the agent
        /// just holds in place at the split, same as any other rejected hand-off elsewhere in this
        /// project. Ties among 2+ matching branches (including 2+ wildcards) are broken by
        /// PickFavoredBranch's fair alternation, the same shape as junction arbitration's own
        /// PickFavoredFeeder but for an outgoing choice instead of an incoming one.
        /// </summary>
        private Vector3Int? ChooseBranchDirection(Vector3Int cell, List<Vector3Int> directions, GoodsData goodsData)
        {
            List<Vector3Int> matching = new List<Vector3Int>();
            foreach (Vector3Int direction in directions)
            {
                _branchFilters.TryGetValue(cell + direction, out GoodsData filter);
                if (filter == null || filter == goodsData)
                {
                    matching.Add(direction);
                }
            }

            if (matching.Count == 0)
            {
                return null;
            }

            return matching.Count == 1 ? matching[0] : PickFavoredBranch(cell, matching);
        }

        /// <summary>
        /// Fair-alternation among 2+ simultaneously-matching branch directions at a split cell —
        /// never repeats the immediately-previous winner if another matching direction is available,
        /// so a tie between (for example) two "Any"-filtered branches doesn't always send every good
        /// down the same one. Simpler than junction arbitration's PickFavoredFeeder (no main-line
        /// concept applies to an outgoing choice, and no persistence is needed here beyond this one
        /// call — ResolveNextCell already handles per-agent persistence across frames separately).
        /// </summary>
        private Vector3Int PickFavoredBranch(Vector3Int originCell, List<Vector3Int> candidates)
        {
            bool hasLast = _lastFavoredBranch.TryGetValue(originCell, out Vector3Int last);
            Vector3Int winner = candidates[0];
            foreach (Vector3Int candidate in candidates)
            {
                if (!hasLast || candidate != last)
                {
                    winner = candidate;
                    break;
                }
            }

            _lastFavoredBranch[originCell] = winner;
            return winner;
        }

        /// <summary>
        /// Left-click cycles a branch's assigned goods filter (#17) when it hits a cell that's
        /// actually a split destination — outside both placement modes, where left-click already
        /// means something else (confirm container placement, start a conveyor drag). Resolves the
        /// clicked cell via FindCellForHitObject (anchors only) — the same lookup right-click removal
        /// uses — since a branch destination is always given a real anchor by IsAnchorCell precisely
        /// so it has something clickable at all (BeltSystem fill tiles carry no collider).
        /// </summary>
        private void HandleBranchFilterClickInput()
        {
            if (!Mouse.current.leftButton.wasPressedThisFrame || _mainCamera == null)
            {
                return;
            }

            if (_placementManager != null && _placementManager.IsPlacementModeActive)
            {
                return;
            }

            Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit))
            {
                return;
            }

            Vector3Int? cell = FindCellForHitObject(hit.collider.gameObject);
            if (!cell.HasValue || !IsSplitBranchDestination(cell.Value))
            {
                return;
            }

            CycleBranchFilter(cell.Value);
            RebuildBeltVisuals();
        }

        /// <summary>
        /// Advances the given branch destination cell's assigned filter to the next choice in the
        /// content-derived list ReceivingManager already builds (#15) — Any (null) first, then each
        /// distinct GoodsData in turn, wrapping back to Any. No-ops if that list is empty (nothing to
        /// cycle to).
        /// </summary>
        private void CycleBranchFilter(Vector3Int destinationCell)
        {
            IReadOnlyList<GoodsData> choices = _receivingManager?.GoodsChoices;
            if (choices == null || choices.Count == 0)
            {
                return;
            }

            _branchFilters.TryGetValue(destinationCell, out GoodsData current);

            int currentIndex = -1;
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i] == current)
                {
                    currentIndex = i;
                    break;
                }
            }

            int nextIndex = currentIndex + 1;
            if (nextIndex >= choices.Count)
            {
                _branchFilters.Remove(destinationCell);
            }
            else
            {
                _branchFilters[destinationCell] = choices[nextIndex];
            }
        }

        /// <summary>
        /// For every known junction cell, clears any claim whose holder has arrived or been
        /// destroyed, then — for junctions left unclaimed and currently free — grants a fresh claim
        /// among this tick's ready feeders (a feeder cell currently holding a settled agent whose
        /// flow points at the junction). A single ready feeder wins outright; genuine contention
        /// (2+ ready feeders at once) is resolved by PickFavoredFeeder. The claim then persists
        /// across frames until its holder arrives, so an in-flight agent is never re-arbitrated
        /// away mid-transit.
        /// </summary>
        private void ResolveJunctionClaims()
        {
            foreach (KeyValuePair<Vector3Int, List<Vector3Int>> junction in _junctionFeeders)
            {
                Vector3Int cell = junction.Key;

                if (_claimedTargets.TryGetValue(cell, out GoodsAgent claimant))
                {
                    if (claimant != null && claimant.CurrentCell != cell)
                    {
                        continue;
                    }

                    _claimedTargets.Remove(cell);
                }

                if (IsCellOccupiedByOtherAgent(cell, null))
                {
                    continue;
                }

                List<Vector3Int> readyFeeders = new List<Vector3Int>();
                foreach (Vector3Int feeder in junction.Value)
                {
                    if (FindAgentAtCell(feeder) != null)
                    {
                        readyFeeders.Add(feeder);
                    }
                }

                if (readyFeeders.Count == 0)
                {
                    continue;
                }

                Vector3Int winner = readyFeeders.Count == 1 ? readyFeeders[0] : PickFavoredFeeder(cell, readyFeeders);
                _claimedTargets[cell] = FindAgentAtCell(winner);
            }
        }

        /// <summary>
        /// Picks which of two or more simultaneously-ready feeders wins a contested junction cell:
        /// the main-line feeder, unless it also won this cell's last contention, in which case a
        /// different ready feeder is favored instead — so a continuously busy main line can't
        /// starve a side line forever. With no main-line feeder ready, alternates among the ready
        /// side feeders the same way.
        /// </summary>
        private Vector3Int PickFavoredFeeder(Vector3Int junctionCell, List<Vector3Int> readyFeeders)
        {
            bool hasLastFavored = _lastFavoredFeeder.TryGetValue(junctionCell, out Vector3Int lastFavored);

            Vector3Int? mainLineFeeder = null;
            foreach (Vector3Int feeder in readyFeeders)
            {
                if (_mainLineCells.Contains(feeder))
                {
                    mainLineFeeder = feeder;
                    break;
                }
            }

            bool mainLineWonLastTime = hasLastFavored && mainLineFeeder.HasValue && lastFavored == mainLineFeeder.Value;

            Vector3Int winner = readyFeeders[0];
            if (mainLineFeeder.HasValue && !mainLineWonLastTime)
            {
                winner = mainLineFeeder.Value;
            }
            else
            {
                foreach (Vector3Int feeder in readyFeeders)
                {
                    if (!hasLastFavored || feeder != lastFavored)
                    {
                        winner = feeder;
                        break;
                    }
                }
            }

            _lastFavoredFeeder[junctionCell] = winner;
            return winner;
        }

        /// <summary>
        /// Returns the active GoodsAgent currently settled at the given cell, or null if none.
        /// Linear scan — fine at Phase 1 scale, same tradeoff IsCellOccupiedByOtherAgent already
        /// makes.
        /// </summary>
        private GoodsAgent FindAgentAtCell(Vector3Int cell)
        {
            foreach (GoodsAgent agent in _activeGoods)
            {
                if (agent.CurrentCell == cell)
                {
                    return agent;
                }
            }

            return null;
        }

        /// <summary>
        /// Attempts to hand the given agent off to whatever destination occupies nextCell — a
        /// container (StorageManager.TryStoreAt) or a Shipping dock's input cell
        /// (ShippingManager.TryFulfillAt), tried in that order (a cell can only ever be one or the
        /// other, never both, so trying both in sequence is safe). Accepted by either removes the
        /// agent from active tracking and destroys its GameObject; rejected by both (wrong type,
        /// full, nothing needs it, or nothing there at all) leaves the agent untouched so it keeps
        /// holding at its current cell.
        /// </summary>
        private void TryHandOffToDestination(GoodsAgent agent, Vector3Int nextCell, int agentIndex)
        {
            bool accepted = (_storageManager != null && _storageManager.TryStoreAt(nextCell, agent.Data))
                || (_shippingManager != null && _shippingManager.TryFulfillAt(nextCell, agent.Data));

            if (!accepted)
            {
                return;
            }

            _activeGoods.RemoveAt(agentIndex);
            Destroy(agent.gameObject);
        }

        /// <summary>
        /// Returns true if any GoodsAgent other than the given one currently occupies the given
        /// cell. Linear scan over _activeGoods — fine at Phase 1 scale, same tradeoff GridManager's
        /// NextFreeHeightLevel already makes.
        /// </summary>
        private bool IsCellOccupiedByOtherAgent(Vector3Int cell, GoodsAgent excluding)
        {
            foreach (GoodsAgent other in _activeGoods)
            {
                if (other != excluding && other.CurrentCell == cell)
                {
                    return true;
                }
            }

            return false;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only debug stand-in for the not-yet-built ReceivingManager: pressing G while
        /// aiming at a placed conveyor segment spawns one GoodsAgent there, for testing belt
        /// movement before real scheduled spawning exists.
        /// </summary>
        private void HandleDebugSpawnInput()
        {
            if (!Keyboard.current.gKey.wasPressedThisFrame || _mainCamera == null
                || _debugGoodsData == null || _debugGoodsData.prefab == null)
            {
                return;
            }

            Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit))
            {
                return;
            }

            Vector3Int? hitCell = FindCellForHitObject(hit.collider.gameObject);
            if (hitCell.HasValue)
            {
                SpawnDebugGoods(hitCell.Value);
            }
        }

        /// <summary>
        /// Instantiates a GoodsAgent for the debug GoodsData at the given cell.
        /// </summary>
        private void SpawnDebugGoods(Vector3Int cell)
        {
            GameObject instance = Instantiate(_debugGoodsData.prefab, GoodsRestPosition(cell), Quaternion.identity);
            GoodsAgent agent = instance.AddComponent<GoodsAgent>();
            agent.Initialize(_debugGoodsData, cell);
            RegisterGoodsAgent(agent);
        }
#endif

        /// <summary>
        /// Compares two cell lists for equality, used to avoid rebuilding the preview when the drag
        /// hasn't actually changed cells since last frame.
        /// </summary>
        private static bool CellsEqual(List<Vector3Int> a, List<Vector3Int> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
