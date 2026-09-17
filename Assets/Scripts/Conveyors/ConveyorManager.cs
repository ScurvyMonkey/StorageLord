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
    /// Runtime placement and movement system for conveyor belt segments. Players toggle conveyor
    /// placement mode (C), then left-click-drag from a start cell to lay a straight run of up to 10
    /// segments, cycling flow direction with Q/R while dragging; releasing confirms the run. Also
    /// drives per-frame movement of any GoodsAgent sitting on a placed segment. Separate from
    /// PlacementManager (drag-based input differs enough from single-click container placement to
    /// warrant its own class), but shares GridManager's occupancy registry with it so the two
    /// placers can never confirm into the same cell, and enforces mutual exclusion with
    /// PlacementManager's own placement mode so a single R press is never ambiguous between
    /// "rotate the container ghost" and "cycle belt flow direction."
    ///
    /// Created and wired by Bootstrapper — not placed directly in a scene, since it has no
    /// serialized Inspector fields to wire (its data references are injected via Initialize()).
    /// </summary>
    public class ConveyorManager : MonoBehaviour
    {
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
        private readonly Dictionary<Vector3Int, Vector3Int> _segmentFlowDirections = new Dictionary<Vector3Int, Vector3Int>();
        private readonly List<GoodsAgent> _activeGoods = new List<GoodsAgent>();
        private readonly List<GameObject> _energyConnectors = new List<GameObject>();

        // Network-topology state (#7): recomputed wholesale on every placement/removal, never
        // patched incrementally — same "rebuild wholesale, fine at Phase 1 scale" tradeoff
        // RebuildEnergyConnectors already makes.
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
            if (Keyboard.current == null || Mouse.current == null)
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
            if (_gridConfig == null || _gridManager == null || _conveyorData == null || _conveyorData.prefab == null)
            {
                Debug.LogWarning(
                    "ConveyorManager: cannot enter placement mode — GridConfig, GridManager, or ConveyorData/prefab not assigned.");
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
        /// or red otherwise.
        /// </summary>
        private void RebuildPreview(List<Vector3Int> cells)
        {
            DestroyPreviewInstances();

            if (_conveyorData == null || _conveyorData.prefab == null)
            {
                return;
            }

            bool allValid = IsRunValid(cells, CardinalDirections[_flowDirectionIndex]);
            Color tint = allValid ? ValidPreviewTint : InvalidPreviewTint;
            Quaternion rotation = FlowRotation();

            foreach (Vector3Int cell in cells)
            {
                GameObject preview = Instantiate(_conveyorData.prefab, SegmentWorldPosition(cell), rotation);
                foreach (Collider previewCollider in preview.GetComponentsInChildren<Collider>())
                {
                    previewCollider.enabled = false;
                }

                foreach (Renderer previewRenderer in preview.GetComponentsInChildren<Renderer>())
                {
                    previewRenderer.material.color = tint;
                }

                AttachFlowArrow(preview);
                _previewInstances.Add(preview);
            }

            _lastRunValid = allValid;
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
        /// confirmed (#7): every cell except the last must be free; the last cell may either be
        /// free or already hold an existing conveyor segment — a merge junction — but not a
        /// container or other occupant; and the run's flow, simulated forward through whatever
        /// existing network it merges into, must never reach the Receiving dock's output cell.
        /// Shared by RebuildPreview and ConfirmDrag so the ghost preview can never show valid for a
        /// run that would actually be rejected on release.
        /// </summary>
        private bool IsRunValid(List<Vector3Int> cells, Vector3Int flowDirection)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                Vector3Int cell = cells[i];
                if (!_gridManager.IsOccupied(cell))
                {
                    continue;
                }

                bool isLastCell = i == cells.Count - 1;
                if (!isLastCell || !_segmentInstances.ContainsKey(cell))
                {
                    return false;
                }
            }

            return !TraceReachesReceiving(cells[cells.Count - 1], flowDirection);
        }

        /// <summary>
        /// Simulates the network's flow forward from the given cell — stepping once by the given
        /// flow direction (the candidate run's own direction, since it isn't registered yet), then
        /// continuing through whatever existing segments it connects into — returning true if this
        /// walk would ever reach the Receiving dock's output cell (backflow). Tracks visited cells
        /// so a network containing an unrelated loop elsewhere (permitted — only cycles back to
        /// Receiving are rejected) can't cause an infinite walk.
        /// </summary>
        private bool TraceReachesReceiving(Vector3Int startCell, Vector3Int flowDirection)
        {
            Vector3Int? outputCell = _receivingManager?.GetPrimaryOutputCell();
            if (!outputCell.HasValue)
            {
                return false;
            }

            HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
            Vector3Int current = startCell + flowDirection;

            while (visited.Add(current))
            {
                if (current == outputCell.Value)
                {
                    return true;
                }

                if (!_segmentFlowDirections.TryGetValue(current, out Vector3Int nextDirection))
                {
                    return false;
                }

                current += nextDirection;
            }

            return false;
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
        private void AttachFlowArrow(GameObject segmentInstance)
        {
            GameObject arrow = new GameObject("FlowArrow");
            arrow.transform.SetParent(segmentInstance.transform, false);
            arrow.transform.localPosition = new Vector3(0f, FlowArrowHoverOffset, 0f);
            arrow.transform.localRotation = Quaternion.identity;

            MeshFilter filter = arrow.AddComponent<MeshFilter>();
            filter.mesh = GetOrCreateFlowArrowMesh();

            MeshRenderer arrowRenderer = arrow.AddComponent<MeshRenderer>();
            arrowRenderer.sharedMaterial = GetOrCreateFlowArrowMaterial();
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
        /// Destroys every current energy-connector instance and rebuilds one for each pair of
        /// segments that are actually connected — cell and cell+flowDirection both registered
        /// segments, the same condition AdvanceGoods already uses to decide a good can move between
        /// them. Rebuilt wholesale on any placement/removal rather than updated incrementally
        /// (simple, correct, fine at Phase 1 scale) since a new segment can complete a connection
        /// formed across two separate drags, not just within the one just confirmed.
        /// </summary>
        private void RebuildEnergyConnectors()
        {
            foreach (GameObject connector in _energyConnectors)
            {
                if (connector != null)
                {
                    Destroy(connector);
                }
            }

            _energyConnectors.Clear();

            foreach (KeyValuePair<Vector3Int, Vector3Int> entry in _segmentFlowDirections)
            {
                Vector3Int cell = entry.Key;
                Vector3Int nextCell = cell + entry.Value;
                if (_segmentFlowDirections.ContainsKey(nextCell))
                {
                    _energyConnectors.Add(CreateEnergyConnector(SegmentWorldPosition(cell), SegmentWorldPosition(nextCell)));
                }
            }
        }

        /// <summary>
        /// Creates a flat emissive quad bridging the gap between two connected segments' positions —
        /// the "sci-fi energy conduit" look explaining why goods travel without visible rollers,
        /// static for this first pass (an animated flowing version is a likely future iteration).
        /// </summary>
        private GameObject CreateEnergyConnector(Vector3 from, Vector3 to)
        {
            GameObject connector = new GameObject("EnergyConnector");
            connector.transform.position = (from + to) * 0.5f;
            connector.transform.rotation = Quaternion.LookRotation((to - from).normalized, Vector3.up);

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
        /// Confirms the current drag run if IsRunValid accepts it, instantiating a segment at each
        /// cell, registering it with GridManager's shared occupancy, and raising the placement
        /// event — except the run's last cell, if it's a merge onto an already-existing conveyor
        /// segment (#7): that segment stays as-is, keeping its own flow direction, rather than
        /// being replaced (which would leak the old instance and duplicate the cell's dictionary
        /// entry). If the run was invalid, it's cancelled without placing anything.
        /// </summary>
        private void ConfirmDrag()
        {
            if (_lastPreviewCells != null && _lastRunValid)
            {
                Vector3Int flow = CardinalDirections[_flowDirectionIndex];
                Quaternion rotation = FlowRotation();

                foreach (Vector3Int cell in _lastPreviewCells)
                {
                    if (_segmentInstances.ContainsKey(cell))
                    {
                        continue;
                    }

                    GameObject instance = Instantiate(_conveyorData.prefab, SegmentWorldPosition(cell), rotation);
                    AttachFlowArrow(instance);
                    _segmentInstances[cell] = instance;
                    _segmentFlowDirections[cell] = flow;
                    _gridManager.Register(cell);
                    _eventChannel?.RaisePiecePlaced(instance, cell);
                }

                RebuildEnergyConnectors();
                RebuildNetworkTopology();
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
        /// Right-click removes the single placed conveyor segment under the cursor (physics
        /// raycast). Only active outside placement mode. Segments aren't stacked, so removal never
        /// cascades. Any GoodsAgent currently riding the removed cell is lost along with it — a
        /// belt pulled out from under a good has nothing left to hold it up, so it's destroyed
        /// rather than left frozen in place with no supporting segment.
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

            Vector3Int? cellToRemove = null;
            foreach (KeyValuePair<Vector3Int, GameObject> entry in _segmentInstances)
            {
                if (entry.Value == hit.collider.gameObject || hit.collider.transform.IsChildOf(entry.Value.transform))
                {
                    cellToRemove = entry.Key;
                    break;
                }
            }

            if (!cellToRemove.HasValue)
            {
                return;
            }

            Destroy(_segmentInstances[cellToRemove.Value]);
            _segmentInstances.Remove(cellToRemove.Value);
            _segmentFlowDirections.Remove(cellToRemove.Value);
            _gridManager.Unregister(cellToRemove.Value);
            DestroyGoodsAtCell(cellToRemove.Value);
            RebuildEnergyConnectors();
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
        /// Rebuilds the main line: the set of cells reached by tracing forward from the Receiving
        /// dock's output cell through connected segments' flow directions. Tracks visited cells so
        /// a network containing a loop can't cause an infinite walk.
        /// </summary>
        private void RebuildMainLine()
        {
            _mainLineCells.Clear();

            Vector3Int? outputCell = _receivingManager?.GetPrimaryOutputCell();
            if (!outputCell.HasValue)
            {
                return;
            }

            Vector3Int current = outputCell.Value;
            while (_mainLineCells.Add(current) && _segmentFlowDirections.TryGetValue(current, out Vector3Int direction))
            {
                current += direction;
            }
        }

        /// <summary>
        /// Rebuilds the junction feeder map: every cell fed by 2+ other segments' flow directions,
        /// mapped to the list of those feeder cells. A "junction" here is purely this derived
        /// property of the flow-direction graph — no separate placement step creates one.
        /// </summary>
        private void RebuildJunctionFeeders()
        {
            Dictionary<Vector3Int, List<Vector3Int>> feedersByTarget = new Dictionary<Vector3Int, List<Vector3Int>>();
            foreach (KeyValuePair<Vector3Int, Vector3Int> entry in _segmentFlowDirections)
            {
                Vector3Int target = entry.Key + entry.Value;
                if (!feedersByTarget.TryGetValue(target, out List<Vector3Int> feeders))
                {
                    feeders = new List<Vector3Int>();
                    feedersByTarget[target] = feeders;
                }

                feeders.Add(entry.Key);
            }

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
        /// place, tried again next frame. Iterates backwards since a successful hand-off removes
        /// from _activeGoods mid-loop.
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
                if (!_segmentFlowDirections.TryGetValue(agent.CurrentCell, out Vector3Int flowDirection))
                {
                    continue;
                }

                Vector3Int nextCell = agent.CurrentCell + flowDirection;
                if (!_segmentFlowDirections.ContainsKey(nextCell))
                {
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
                    agent.SetCurrentCell(nextCell);
                    _claimedTargets.Remove(nextCell);
                }
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

            foreach (KeyValuePair<Vector3Int, GameObject> entry in _segmentInstances)
            {
                if (entry.Value == hit.collider.gameObject || hit.collider.transform.IsChildOf(entry.Value.transform))
                {
                    SpawnDebugGoods(entry.Key);
                    return;
                }
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
