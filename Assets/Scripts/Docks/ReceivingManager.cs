using System.Collections.Generic;
using StorageLord.Conveyors;
using StorageLord.Goods;
using StorageLord.Grid;
using StorageLord.Placement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StorageLord.Docks
{
    /// <summary>
    /// Spawns goods automatically at every ReceivingDock in the scene, on a fixed timer, and hands
    /// them to ConveyorManager so they move along whatever belt is connected — the platform's first
    /// real goods source, replacing the need for ConveyorManager's temporary debug spawn action.
    /// Docks are fixed/Editor-placed (not runtime-placed), but still register their own cell with
    /// GridManager at startup so PlacementManager/ConveyorManager can't be confirmed on top of one.
    ///
    /// Also lets the player choose which goods type is currently spawned (#15) — left-clicking a
    /// dock's own geometry cycles through every distinct GoodsData referenced by the Company's
    /// order content (ShippingScheduleData/WaveEscalationData), a content-derived list that grows
    /// automatically as more orders get authored, with no separate unlock/progression system.
    ///
    /// Created and wired by Bootstrapper — not placed directly in a scene, since it has no
    /// serialized Inspector fields to wire (its data references are injected via Initialize()).
    /// </summary>
    public class ReceivingManager : MonoBehaviour
    {
        private GridConfig _gridConfig;
        private GridManager _gridManager;
        private ConveyorManager _conveyorManager;
        private PlacementManager _placementManager;
        private ReceivingData _receivingData;
        private Camera _mainCamera;

        private readonly List<ReceivingDock> _docks = new List<ReceivingDock>();
        private readonly Dictionary<ReceivingDock, GoodsAgent> _lastSpawnedByDock = new Dictionary<ReceivingDock, GoodsAgent>();
        private readonly List<GoodsData> _goodsChoices = new List<GoodsData>();
        private int _selectedIndex;

        private float _spawnTimer;
        private bool _isGameActive = true;

        /// <summary>
        /// The goods type ReceivingManager currently spawns — the player-selected entry in the
        /// content-derived choice list (#15) if that list isn't empty, otherwise ReceivingData's
        /// own fixed goodsData as a direct fallback.
        /// </summary>
        public GoodsData CurrentGoods => _goodsChoices.Count > 0 ? _goodsChoices[_selectedIndex] : _receivingData?.goodsData;

        /// <summary>
        /// The full content-derived goods choice list (#15) — every distinct GoodsData referenced by
        /// ShippingScheduleData/WaveEscalationData, exposed read-only so ConveyorManager can reuse
        /// the exact same list for cycling a conveyor split branch's accepted-goods filter (#17)
        /// rather than re-deriving it independently.
        /// </summary>
        public IReadOnlyList<GoodsData> GoodsChoices => _goodsChoices;

        /// <summary>
        /// Halts (or resumes) automatic spawning — called by GameManager (#12) when the run ends
        /// (missed-order limit reached).
        /// </summary>
        public void SetGameActive(bool active)
        {
            _isGameActive = active;
        }

        /// <summary>
        /// Injects this manager's data references and finds every ReceivingDock in the scene.
        /// Deliberately does NOT register the dock's own ConnectionPoint cell with GridManager — it
        /// used to be reserved, which meant a player who naturally tried placing a belt flush
        /// against the dock (right on that cell) got rejected with a generic, unhelpful warning
        /// (fixed by direct user report). The real required position (see OutputCell) is one cell
        /// further out and was never actually blocked — leaving ConnectionPoint's own cell
        /// unreserved just means a player is also free to place there too if they want (harmless,
        /// redundant with the decorative connector piece already there), without being forced to or
        /// rejected for trying. Called once by Bootstrapper immediately after creation — deliberately
        /// not done in Awake(), since Bootstrapper creates this manager via AddComponent(), which
        /// fires Awake() synchronously before Initialize() has set any of these references (see
        /// CLAUDE.md's Camera.main precedent for the same pitfall).
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

            _docks.AddRange(FindObjectsByType<ReceivingDock>(FindObjectsSortMode.None));
            BuildGoodsChoices(shippingScheduleData, waveEscalationData);
        }

        /// <summary>
        /// Wires this manager's reference to PlacementManager, used by HandleGoodsSelectionInput
        /// to avoid processing a dock click while container placement mode is active (#15) — a
        /// left-click during PlacementManager's own placement mode already means "confirm the
        /// ghost preview," not "cycle Receiving's goods selection." Called once by Bootstrapper
        /// after both managers exist.
        /// </summary>
        public void SetPlacementManager(PlacementManager placementManager)
        {
            _placementManager = placementManager;
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
        /// content is authored, with no separate unlock list to maintain. Starts the selection on
        /// ReceivingData.goodsData if it appears in the derived list, otherwise the list's first
        /// entry (covering both "goodsData unset" and "goodsData set but not referenced by any
        /// order" — the simplest reading of the spec's "default to goodsData, or the first derived
        /// choice if unset" acceptance criterion). CurrentGoods falls back to goodsData directly if
        /// the derived list ends up empty.
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
                int defaultIndex = _goodsChoices.IndexOf(_receivingData.goodsData);
                _selectedIndex = Mathf.Max(0, defaultIndex);
            }
        }

        /// <summary>
        /// Advances the shared spawn timer; once it crosses the configured interval, attempts a
        /// spawn at every dock and resets the timer. Goods-selection input is handled regardless of
        /// spawn readiness, since it doesn't depend on the spawn timer.
        /// </summary>
        private void Update()
        {
            if (!_isGameActive)
            {
                return;
            }

            HandleGoodsSelectionInput();

            GoodsData currentGoods = CurrentGoods;
            if (_receivingData == null || currentGoods == null || currentGoods.prefab == null
                || _conveyorManager == null || _gridConfig == null)
            {
                return;
            }

            _spawnTimer += Time.deltaTime;
            float interval = 60f / _receivingData.itemsPerMinute;
            if (_spawnTimer < interval)
            {
                return;
            }

            _spawnTimer -= interval;
            foreach (ReceivingDock dock in _docks)
            {
                TrySpawnAt(dock, currentGoods);
            }
        }

        /// <summary>
        /// Left-click cycles the current goods selection forward (wrapping) when it hits a
        /// registered dock's own geometry — but only outside both placement modes (#15), since a
        /// left-click there already means "confirm the ghost preview" or "start a conveyor drag,"
        /// not "cycle goods." No-ops if there's nothing to cycle through.
        /// </summary>
        private void HandleGoodsSelectionInput()
        {
            if (_mainCamera == null || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame
                || _goodsChoices.Count == 0)
            {
                return;
            }

            if ((_placementManager != null && _placementManager.IsPlacementModeActive)
                || (_conveyorManager != null && _conveyorManager.IsPlacementModeActive))
            {
                return;
            }

            Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit))
            {
                return;
            }

            ReceivingDock dock = hit.collider.GetComponentInParent<ReceivingDock>();
            if (dock == null || !_docks.Contains(dock))
            {
                return;
            }

            _selectedIndex = (_selectedIndex + 1) % _goodsChoices.Count;
        }

        /// <summary>
        /// Spawns one good onto the given dock's output cell — but only once a real conveyor
        /// segment is actually placed there (otherwise the good would hover in open space with
        /// nothing to carry it, since GoodsRestPosition is a pure coordinate computation that
        /// doesn't care whether a segment GameObject exists). Also skipped if the good this dock
        /// spawned last time is still sitting there unmoved, rather than stacking a second good on
        /// top of it.
        /// </summary>
        private void TrySpawnAt(ReceivingDock dock, GoodsData goodsData)
        {
            Vector3Int outputCell = OutputCell(dock);

            if (!_conveyorManager.HasSegmentAt(outputCell))
            {
                return;
            }

            if (_lastSpawnedByDock.TryGetValue(dock, out GoodsAgent lastAgent)
                && lastAgent != null && lastAgent.CurrentCell == outputCell)
            {
                return;
            }

            GameObject instance = Instantiate(
                goodsData.prefab, _conveyorManager.GoodsRestPosition(outputCell), Quaternion.identity);
            GoodsAgent agent = instance.AddComponent<GoodsAgent>();
            agent.Initialize(goodsData, outputCell);
            _conveyorManager.RegisterGoodsAgent(agent);
            _lastSpawnedByDock[dock] = agent;
        }

        /// <summary>
        /// Returns the cell one step in front of the given dock's ConnectionPoint, per its facing —
        /// goods spawn here, and a real belt segment must be placed at this exact cell (gated by
        /// HasSegmentAt) for spawning to actually happen. A same-day earlier fix tried moving this
        /// to ConnectionPoint's own cell directly (reasoning: Receiving's mechanic already requires
        /// a real segment wherever goods spawn, so the buffer could collapse to zero) — but that
        /// silently broke spawning for any belt built the "natural" way, one cell out from the dock,
        /// since that's the position the connector's own cell being unreserved (see Initialize) was
        /// only ever meant to *permit*, not replace. Reverted: the required position stays one cell
        /// out (matching Shipping's own collapsed-to-one-cell design, and every belt already built
        /// against the old design before today), while ConnectionPoint's own cell stays unreserved
        /// so a player is free to place there too if they want, without being forced to.
        /// </summary>
        private Vector3Int OutputCell(ReceivingDock dock)
        {
            return _gridConfig.WorldToCell3D(dock.ConnectionPoint.position) + dock.GetOutputDirection();
        }

        /// <summary>
        /// Returns the output cell of the first registered ReceivingDock, or null if none exist yet.
        /// Used by ConveyorManager (#7) to trace the conveyor network's main line back to Receiving
        /// for backflow prevention and junction priority — assumes a single dock/single main line,
        /// matching this project's current single-ReceivingDock scope.
        /// </summary>
        public Vector3Int? GetPrimaryOutputCell()
        {
            return _docks.Count > 0 ? OutputCell(_docks[0]) : (Vector3Int?)null;
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
