using System.Collections.Generic;
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

        private bool _isPlacing;
        private Vector3Int? _dragStartCell;
        private int _flowDirectionIndex;
        private List<Vector3Int> _lastPreviewCells;
        private bool _lastRunValid;

        private readonly List<GameObject> _previewInstances = new List<GameObject>();
        private readonly Dictionary<Vector3Int, GameObject> _segmentInstances = new Dictionary<Vector3Int, GameObject>();
        private readonly Dictionary<Vector3Int, Vector3Int> _segmentFlowDirections = new Dictionary<Vector3Int, Vector3Int>();
        private readonly List<GoodsAgent> _activeGoods = new List<GoodsAgent>();
        private readonly List<GameObject> _energyConnectors = new List<GameObject>();

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
                _placementManager?.ExitPlacementMode();
            }
            else
            {
                CancelDrag();
            }
        }

        /// <summary>
        /// Handles left-click-drag placement input and Q/R flow-direction cycling while conveyor
        /// placement mode is active.
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
        /// Raycasts the cursor against the deck plane (at GridConfig.deckSurfaceHeight, the
        /// platform's actual walkable surface) to find the aimed X/Z column, returning it as a
        /// deck-level (height 0) 3D cell. Conveyors are deck-level-only for Phase 1.
        /// </summary>
        private Vector3Int? RaycastDeckCell()
        {
            if (_mainCamera == null)
            {
                return null;
            }

            Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            Plane deckPlane = new Plane(Vector3.up, new Vector3(0f, _gridConfig.deckSurfaceHeight, 0f));
            if (!deckPlane.Raycast(ray, out float enter))
            {
                return null;
            }

            Vector3 hitPoint = ray.GetPoint(enter);
            Vector2Int column = _gridConfig.WorldToCell(hitPoint);
            return new Vector3Int(column.x, 0, column.y);
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
        /// green if every cell is free or red if any cell is already occupied — the run can only be
        /// confirmed when entirely valid.
        /// </summary>
        private void RebuildPreview(List<Vector3Int> cells)
        {
            DestroyPreviewInstances();

            if (_conveyorData == null || _conveyorData.prefab == null)
            {
                return;
            }

            bool allValid = true;
            foreach (Vector3Int cell in cells)
            {
                if (_gridManager.IsOccupied(cell))
                {
                    allValid = false;
                    break;
                }
            }

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
        /// Confirms the current drag run if every cell in it is free, instantiating a segment at
        /// each cell, registering it with GridManager's shared occupancy, and raising the placement
        /// event. If any cell was occupied, the drag is cancelled without placing anything.
        /// </summary>
        private void ConfirmDrag()
        {
            if (_lastPreviewCells != null && _lastRunValid)
            {
                Vector3Int flow = CardinalDirections[_flowDirectionIndex];
                Quaternion rotation = FlowRotation();

                foreach (Vector3Int cell in _lastPreviewCells)
                {
                    GameObject instance = Instantiate(_conveyorData.prefab, SegmentWorldPosition(cell), rotation);
                    AttachFlowArrow(instance);
                    _segmentInstances[cell] = instance;
                    _segmentFlowDirections[cell] = flow;
                    _gridManager.Register(cell);
                    _eventChannel?.RaisePiecePlaced(instance, cell);
                }

                RebuildEnergyConnectors();
            }
            else if (_lastPreviewCells != null)
            {
                Debug.LogWarning("ConveyorManager: drag run overlaps an occupied cell — placement cancelled.");
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
        /// cascades.
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
            RebuildEnergyConnectors();
        }

        /// <summary>
        /// Advances every active GoodsAgent one step: if it's sitting on a conveyor cell whose flow
        /// leads to another conveyor cell that isn't already occupied by a different GoodsAgent,
        /// moves it toward that cell's world position at the configured belt speed. If the next cell
        /// isn't a conveyor segment, attempts to hand the agent off to a container there instead
        /// (StorageManager.TryStoreAt) — accepted removes it from tracking, rejected (or no
        /// container there at all) just holds it in place, tried again next frame. Iterates
        /// backwards since a successful hand-off removes from _activeGoods mid-loop.
        /// </summary>
        private void AdvanceGoods()
        {
            if (_conveyorData == null || _gridConfig == null)
            {
                return;
            }

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
                    TryHandOffToContainer(agent, nextCell, i);
                    continue;
                }

                if (IsCellOccupiedByOtherAgent(nextCell, agent))
                {
                    continue;
                }

                Vector3 targetPosition = GoodsRestPosition(nextCell);
                float maxDistanceDelta = _conveyorData.beltSpeed * _gridConfig.cellSize * Time.deltaTime;
                agent.transform.position = Vector3.MoveTowards(agent.transform.position, targetPosition, maxDistanceDelta);

                if (Vector3.Distance(agent.transform.position, targetPosition) < 0.001f)
                {
                    agent.SetCurrentCell(nextCell);
                }
            }
        }

        /// <summary>
        /// Attempts to hand the given agent off to a container at nextCell via StorageManager. If
        /// accepted, removes the agent from active tracking and destroys its GameObject (absorbed
        /// into storage); if rejected (no container there, wrong type, or already full), leaves the
        /// agent untouched so it keeps holding at its current cell.
        /// </summary>
        private void TryHandOffToContainer(GoodsAgent agent, Vector3Int nextCell, int agentIndex)
        {
            if (_storageManager == null || !_storageManager.TryStoreAt(nextCell, agent.Data))
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
