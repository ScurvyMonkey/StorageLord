using System.Collections.Generic;
using StorageLord.Conveyors;
using StorageLord.Goods;
using StorageLord.Grid;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace StorageLord.Docks
{
    /// <summary>
    /// Spawns goods automatically at every registered ReceivingDock, each on its own timer, and
    /// hands them to ConveyorManager so they move along whatever belt is connected — the platform's
    /// only goods source. The fixed starting dock is Editor-placed and found at startup; extra
    /// Receiving buildings the player buys (#51) are registered/unregistered at runtime by
    /// ReceivingBuildingPlacer.
    ///
    /// Each dock has its own goods selection (#15, per-dock since #51) — left-clicking a dock's
    /// own geometry cycles that dock's selection through every distinct GoodsData referenced by
    /// the Company's order content (ShippingScheduleData/WaveEscalationData), a content-derived
    /// list that grows automatically as more orders get authored.
    ///
    /// Created and wired by Bootstrapper — not placed directly in a scene, since it has no
    /// serialized Inspector fields to wire (its data references are injected via Initialize()).
    /// </summary>
    public class ReceivingManager : MonoBehaviour
    {
        /// <summary>Per-dock runtime state (#51): its goods selection, spawn timer and rate, and last spawned good.</summary>
        private class DockState
        {
            public int GoodsIndex;
            public float SpawnTimer;
            public float ItemsPerMinuteOverride;
            public GoodsAgent LastSpawned;
        }

        private GridConfig _gridConfig;
        private GridManager _gridManager;
        private ConveyorManager _conveyorManager;
        private ReceivingData _receivingData;
        private Camera _mainCamera;

        private readonly List<ReceivingDock> _docks = new List<ReceivingDock>();
        private readonly Dictionary<ReceivingDock, DockState> _dockStates = new Dictionary<ReceivingDock, DockState>();
        private readonly HashSet<Vector3Int> _outputCells = new HashSet<Vector3Int>();
        private readonly List<GoodsData> _goodsChoices = new List<GoodsData>();
        private int _defaultGoodsIndex;
        private ReceivingDock _selectedDock;

        private bool _isGameActive = true;

        /// <summary>
        /// The goods type the selected dock (the one the player last clicked, or the starting dock)
        /// currently spawns — its entry in the content-derived choice list (#15) if that list isn't
        /// empty, otherwise ReceivingData's own fixed goodsData as a direct fallback.
        /// </summary>
        public GoodsData CurrentGoods => GoodsFor(_selectedDock);

        /// <summary>1-based position of the selected dock among all registered docks (#51), or 0 if none exist.</summary>
        public int SelectedDockNumber => _selectedDock != null ? _docks.IndexOf(_selectedDock) + 1 : 0;

        /// <summary>How many Receiving docks are registered, the fixed one included (#51).</summary>
        public int DockCount => _docks.Count;

        /// <summary>How many player-placed Receiving buildings currently exist (#51) — drives the next building's price.</summary>
        public int PlacedBuildingCount
        {
            get
            {
                int count = 0;
                foreach (ReceivingDock dock in _docks)
                {
                    if (dock.IsPlayerPlaced)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// The full content-derived goods choice list (#15) — every distinct GoodsData referenced by
        /// ShippingScheduleData/WaveEscalationData, exposed read-only so ConveyorManager can reuse
        /// the exact same list for cycling a conveyor split branch's accepted-goods filter (#17)
        /// rather than re-deriving it independently.
        /// </summary>
        public IReadOnlyList<GoodsData> GoodsChoices => _goodsChoices;

        /// <summary>Every registered dock's output cell (#51) — ConveyorManager seeds its main line from these.</summary>
        public IReadOnlyCollection<Vector3Int> OutputCells => _outputCells;

        /// <summary>How many distinct Receiving output cells exist (#51).</summary>
        public int OutputCellCount => _outputCells.Count;

        /// <summary>True if <paramref name="cell"/> is any registered dock's output cell (#51) — ConveyorManager's backflow check.</summary>
        public bool IsOutputCell(Vector3Int cell)
        {
            return _outputCells.Contains(cell);
        }

        /// <summary>
        /// Halts (or resumes) automatic spawning — called by GameManager (#12) when the run ends
        /// (missed-order limit reached).
        /// </summary>
        public void SetGameActive(bool active)
        {
            _isGameActive = active;
        }

        /// <summary>
        /// Injects this manager's data references and registers every Editor-placed ReceivingDock in
        /// the scene. Deliberately does NOT register a dock's own ConnectionPoint cell with
        /// GridManager — it used to be reserved, which meant a player who naturally tried placing a
        /// belt flush against the dock (right on that cell) got rejected with a generic, unhelpful
        /// warning (fixed by direct user report). The real required position (see OutputCellFor) is
        /// one cell further out and was never actually blocked. Called once by Bootstrapper
        /// immediately after creation — deliberately not done in Awake(), since Bootstrapper creates
        /// this manager via AddComponent(), which fires Awake() synchronously before Initialize() has
        /// set any of these references (see CLAUDE.md's Camera.main precedent for the same pitfall).
        /// </summary>
        public void Initialize(
            GridConfig gridConfig,
            GridManager gridManager,
            ConveyorManager conveyorManager,
            ReceivingData receivingData,
            ShippingScheduleData shippingScheduleData,
            WaveEscalationData waveEscalationData)
        {
            _gridConfig = gridConfig;
            _gridManager = gridManager;
            _conveyorManager = conveyorManager;
            _receivingData = receivingData;

            BuildGoodsChoices(shippingScheduleData, waveEscalationData);
            foreach (ReceivingDock dock in FindObjectsByType<ReceivingDock>(FindObjectsSortMode.None))
            {
                RegisterDock(dock, 0f);
            }
        }

        /// <summary>
        /// Registers a Receiving dock so it starts spawning (#51) — its selection starts on the
        /// default goods type, and it becomes the selected dock so the HUD shows it. A non-positive
        /// <paramref name="itemsPerMinuteOverride"/> uses ReceivingData's rate. The fixed dock is
        /// registered from Initialize; player-placed buildings by ReceivingBuildingPlacer, which
        /// also tells ConveyorManager to recompute topology afterwards.
        /// </summary>
        public void RegisterDock(ReceivingDock dock, float itemsPerMinuteOverride)
        {
            if (dock == null || _dockStates.ContainsKey(dock))
            {
                return;
            }

            _docks.Add(dock);
            _dockStates[dock] = new DockState { GoodsIndex = _defaultGoodsIndex, ItemsPerMinuteOverride = itemsPerMinuteOverride };
            RebuildOutputCells();
            if (_selectedDock == null || dock.IsPlayerPlaced)
            {
                _selectedDock = dock;
            }
        }

        /// <summary>
        /// Stops a Receiving dock spawning and forgets its state (#51) — called when a player-placed
        /// building is removed or lost to a platform collapse. Goods it already spawned keep moving.
        /// </summary>
        public void UnregisterDock(ReceivingDock dock)
        {
            if (dock == null || !_dockStates.Remove(dock))
            {
                return;
            }

            _docks.Remove(dock);
            RebuildOutputCells();
            if (_selectedDock == dock)
            {
                _selectedDock = _docks.Count > 0 ? _docks[0] : null;
            }
        }

        /// <summary>
        /// Caches the main camera once rather than querying Camera.main every Update — same
        /// pattern PlacementManager/ConveyorManager already use for their own click raycasts.
        /// </summary>
        private void Awake()
        {
            _mainCamera = Camera.main;
        }

        /// <summary>
        /// Builds the player-selectable goods list from every distinct GoodsData referenced by the
        /// Company's authored order schedule and the escalating-wave goods pool (#15) — a
        /// content-derived list that grows automatically as more OrderData/WaveEscalationData
        /// content is authored, with no separate unlock list to maintain. Each dock's selection
        /// starts on ReceivingData.goodsData if it appears in the derived list, otherwise the list's
        /// first entry. GoodsFor falls back to goodsData directly if the derived list ends up empty.
        /// </summary>
        private void BuildGoodsChoices(ShippingScheduleData schedule, WaveEscalationData waveData)
        {
            HashSet<GoodsData> seen = new HashSet<GoodsData>();

            if (schedule != null && schedule.scheduledOrders != null)
            {
                foreach (OrderData order in schedule.scheduledOrders)
                {
                    if (order != null && order.requiredGoods != null && seen.Add(order.requiredGoods))
                    {
                        _goodsChoices.Add(order.requiredGoods);
                    }
                }
            }

            if (waveData != null && waveData.goodsPool != null)
            {
                foreach (GoodsData goods in waveData.goodsPool)
                {
                    if (goods != null && seen.Add(goods))
                    {
                        _goodsChoices.Add(goods);
                    }
                }
            }

            if (_receivingData != null && _receivingData.goodsData != null)
            {
                _defaultGoodsIndex = Mathf.Max(0, _goodsChoices.IndexOf(_receivingData.goodsData));
            }
        }

        /// <summary>
        /// Advances each dock's own spawn timer; once one crosses that dock's interval, attempts a
        /// spawn there with that dock's own goods selection. Goods-selection input is handled
        /// regardless of spawn readiness.
        /// </summary>
        private void Update()
        {
            if (!_isGameActive)
            {
                return;
            }

            HandleGoodsSelectionInput();

            if (_receivingData == null || _conveyorManager == null || _gridConfig == null)
            {
                return;
            }

            for (int i = 0; i < _docks.Count; i++)
            {
                ReceivingDock dock = _docks[i];
                DockState state = _dockStates[dock];
                float itemsPerMinute = state.ItemsPerMinuteOverride > 0f ? state.ItemsPerMinuteOverride : _receivingData.itemsPerMinute;
                float interval = 60f / itemsPerMinute;

                state.SpawnTimer += Time.deltaTime;
                if (state.SpawnTimer < interval)
                {
                    continue;
                }

                state.SpawnTimer -= interval;
                GoodsData goods = GoodsFor(dock);
                if (goods != null && goods.prefab != null)
                {
                    TrySpawnAt(dock, state, goods);
                }
            }
        }

        /// <summary>
        /// Left-click on a registered dock's own geometry cycles that dock's goods selection forward
        /// (wrapping) and makes it the selected dock for the HUD — but only outside every placement
        /// mode (#15, via GridManager's registry since #51), since a left-click there already means
        /// "confirm the ghost preview" or "start a conveyor drag." No-ops if there's nothing to cycle
        /// through, or if the click landed on a UI element (#36's arch condition).
        /// </summary>
        private void HandleGoodsSelectionInput()
        {
            if (_mainCamera == null || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame
                || _goodsChoices.Count == 0)
            {
                return;
            }

            if (_gridManager != null && _gridManager.IsAnyPlacementModeActive)
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

            ReceivingDock dock = hit.collider.GetComponentInParent<ReceivingDock>();
            if (dock == null || !_dockStates.TryGetValue(dock, out DockState state))
            {
                return;
            }

            state.GoodsIndex = (state.GoodsIndex + 1) % _goodsChoices.Count;
            _selectedDock = dock;
        }

        /// <summary>Returns the goods type <paramref name="dock"/> currently spawns, or ReceivingData's goodsData if there's no choice list or no dock.</summary>
        private GoodsData GoodsFor(ReceivingDock dock)
        {
            if (_goodsChoices.Count == 0 || dock == null || !_dockStates.TryGetValue(dock, out DockState state))
            {
                return _goodsChoices.Count > 0 ? _goodsChoices[_defaultGoodsIndex] : _receivingData?.goodsData;
            }

            return _goodsChoices[state.GoodsIndex];
        }

        /// <summary>
        /// Spawns one good onto the given dock's output cell — but only once a real conveyor
        /// segment is actually placed there (otherwise the good would hover in open space with
        /// nothing to carry it, since GoodsRestPosition is a pure coordinate computation that
        /// doesn't care whether a segment GameObject exists). Also skipped if the good this dock
        /// spawned last time is still sitting there unmoved, rather than stacking a second good on
        /// top of it.
        /// </summary>
        private void TrySpawnAt(ReceivingDock dock, DockState state, GoodsData goodsData)
        {
            Vector3Int outputCell = OutputCellFor(dock);

            if (!_conveyorManager.HasSegmentAt(outputCell))
            {
                return;
            }

            if (state.LastSpawned != null && state.LastSpawned.CurrentCell == outputCell)
            {
                return;
            }

            GameObject instance = Instantiate(
                goodsData.prefab, _conveyorManager.GoodsRestPosition(outputCell), Quaternion.identity);
            GoodsAgent agent = instance.AddComponent<GoodsAgent>();
            agent.Initialize(goodsData, outputCell);
            _conveyorManager.RegisterGoodsAgent(agent);
            state.LastSpawned = agent;
        }

        /// <summary>Recomputes the cached set of every registered dock's output cell (#51). Docks never move, so this only runs on register/unregister.</summary>
        private void RebuildOutputCells()
        {
            _outputCells.Clear();
            foreach (ReceivingDock dock in _docks)
            {
                _outputCells.Add(OutputCellFor(dock));
            }
        }

        /// <summary>
        /// Returns the cell one step in front of the given dock's ConnectionPoint, per its facing —
        /// goods spawn here, and a real belt segment must be placed at this exact cell (gated by
        /// HasSegmentAt) for spawning to actually happen. For the fixed dock this is one cell out
        /// from its connector (a same-day earlier fix that moved it onto the connector's own cell
        /// broke every belt built the natural way, one cell out, and was reverted); for a
        /// player-placed building (#51), whose ConnectionPoint is its own root, it's the adjacent
        /// cell on its output side.
        /// </summary>
        public Vector3Int OutputCellFor(ReceivingDock dock)
        {
            return _gridConfig.WorldToCell3D(dock.ConnectionPoint.position) + dock.GetOutputDirection();
        }

        /// <summary>
        /// Returns the output cell of the first registered ReceivingDock (the fixed starting dock),
        /// or null if none exist yet. Used only for the fixed dock's energy-connector visual —
        /// backflow and main-line tracing use every output (IsOutputCell/OutputCells, #51).
        /// </summary>
        public Vector3Int? GetPrimaryOutputCell()
        {
            return _docks.Count > 0 ? OutputCellFor(_docks[0]) : (Vector3Int?)null;
        }

        /// <summary>
        /// Returns the first registered ReceivingDock's connection-point Transform, or null if none
        /// exist yet — the exact world point ConveyorManager's energy-connector visual should
        /// originate from.
        /// </summary>
        public Transform GetPrimaryConnectionPoint()
        {
            return _docks.Count > 0 ? _docks[0].ConnectionPoint : null;
        }
    }
}
