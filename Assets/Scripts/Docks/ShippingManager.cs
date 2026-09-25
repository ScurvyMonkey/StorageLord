using System.Collections.Generic;
using System.Linq;
using StorageLord.Conveyors;
using StorageLord.Goods;
using StorageLord.Grid;
using StorageLord.Placement;
using StorageLord.Storage;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StorageLord.Docks
{
    /// <summary>
    /// Runs the Company's order queue: activates each OrderData in ShippingScheduleData once its
    /// own startDelaySeconds elapses (multiple orders can be active at once, each on its own
    /// deadline), and fulfills them two ways — goods arriving directly at a ShippingDock's input
    /// cell via conveyor (TryFulfillAt, called by ConveyorManager), and automatic dispatch from
    /// StorageManager's containers each frame (#22 — a container with a matching, wanted good and
    /// a real belt against one of its sides pushes a unit onto that belt, which must then travel
    /// to and arrive at a dock exactly like any other delivery; storage can no longer credit an
    /// order on its own). When multiple active orders want the same goods type, Priority always
    /// wins over Random (#25 — "the Company always takes priority"), and within a tier whichever
    /// is closest to finishing (highest Delivered/requiredQuantity ratio, #24) is credited first —
    /// see OrdersByPriority; ties fall back to oldest-activated-first for free, since _activeOrders
    /// is always appended to in activation order and both OrderBy/OrderByDescending are stable
    /// sorts. Once the authored schedule is exhausted, WaveEscalationData (#13) takes over for
    /// Priority orders, RandomJobPoolData (#25) drives a separate rotating pool of optional,
    /// player-picked Random-tier offers, and SpecialEventData (#26) drives a single rare
    /// Random-shaped-but-Special-tier offer sized off the platform's current total weight capacity
    /// — all three generated-order paths reuse this exact activation/fulfillment/event path, so
    /// nothing downstream can tell a generated order from an authored one.
    ///
    /// Created and wired by Bootstrapper — not placed directly in a scene, since it has no
    /// serialized Inspector fields to wire (its data references are injected via Initialize()).
    /// </summary>
    public class ShippingManager : MonoBehaviour
    {
        /// <summary>Preview stats for the pending Special-tier offer (#26), before it's accepted
        /// and split into real bundle orders — deliberately not an OrderData itself (a Special
        /// offer isn't one-good-shaped until accepted), mirroring WeightManager's own
        /// SegmentWeightStatus snapshot-struct pattern.</summary>
        public readonly struct SpecialOfferPreview
        {
            public readonly float TotalWeightTargetKg;
            public readonly int TotalMoneyReward;
            public readonly float DeadlineSeconds;

            public SpecialOfferPreview(float totalWeightTargetKg, int totalMoneyReward, float deadlineSeconds)
            {
                TotalWeightTargetKg = totalWeightTargetKg;
                TotalMoneyReward = totalMoneyReward;
                DeadlineSeconds = deadlineSeconds;
            }
        }

        private GridConfig _gridConfig;
        private GridManager _gridManager;
        private StorageManager _storageManager;
        private ConveyorManager _conveyorManager;
        private PlacementManager _placementManager;
        private WeightManager _weightManager;
        private ShippingScheduleData _schedule;
        private OrderEventChannel _eventChannel;
        private WaveEscalationData _waveData;
        private RandomJobPoolData _jobPoolData;
        private SpecialEventData _specialEventData;
        private Camera _mainCamera;

        private bool[] _activatedFlags;
        private float _elapsedSeconds;
        private int _waveNumber;
        private float _waveSpawnTimer;
        private int _highlightedOfferIndex;
        private float _specialSpawnCooldown;

        private readonly List<ActiveOrder> _activeOrders = new List<ActiveOrder>();
        private readonly HashSet<Vector3Int> _dockInputCells = new HashSet<Vector3Int>();
        private readonly List<ShippingDock> _docks = new List<ShippingDock>();
        private readonly HashSet<OrderData> _generatedOrders = new HashSet<OrderData>();
        private readonly List<(OrderData Order, float RemainingLifetime)> _jobOffers = new List<(OrderData, float)>();
        private readonly List<OrderData> _activeSpecialBundle = new List<OrderData>();

        /// <summary>
        /// The pending Special-tier offer (#26), if one currently exists — null while a bundle is
        /// active or the spawn cooldown hasn't elapsed. Deliberately tracked separately from
        /// JobOffers/RandomJobPoolData's pool (not appended to that list): RefreshJobOfferPool's own
        /// churn/expiry logic is written specifically for the Random pool's fixed poolSize and
        /// offerLifetimeSeconds, and mixing a structurally different, independently-timed single
        /// offer into it would make that logic implicitly tier-aware in a way it isn't today.
        /// HandleJobOfferInput instead treats this as one virtual extra slot appended past the
        /// Random pool's own indices, so the player experiences one unified cycle-and-accept
        /// interaction even though the two backends stay honestly separate.
        /// </summary>
        public SpecialOfferPreview? PendingSpecialOffer { get; private set; }

        /// <summary>
        /// Every currently active order, oldest-activated first, read-only for observers like
        /// ShippingHUD.
        /// </summary>
        public IReadOnlyList<ActiveOrder> ActiveOrders => _activeOrders;

        /// <summary>
        /// The current rotating pool of pickable Random-tier job offers (#25), each with its own
        /// remaining time before it expires unpicked and gets replaced — read-only for a future HUD.
        /// </summary>
        public IReadOnlyList<(OrderData Order, float RemainingLifetime)> JobOffers => _jobOffers;

        /// <summary>Which JobOffers index is currently highlighted for accept (#25).</summary>
        public int HighlightedOfferIndex => _highlightedOfferIndex;

        private bool _isGameActive = true;

        /// <summary>
        /// Halts (or resumes) order activation/deadline ticking/automatic dispatch — called by
        /// GameManager (#12) when the run ends (missed-order limit reached).
        /// </summary>
        public void SetGameActive(bool active)
        {
            _isGameActive = active;
        }

        /// <summary>
        /// Wires this manager's reference to ConveyorManager (#22), needed so automatic dispatch
        /// can spawn a GoodsAgent onto the belt cell StorageManager.TryDispatch finds — the same
        /// RegisterGoodsAgent entry point ReceivingManager already uses. Called once by Bootstrapper
        /// after both managers exist.
        /// </summary>
        public void SetConveyorManager(ConveyorManager conveyorManager)
        {
            _conveyorManager = conveyorManager;
        }

        /// <summary>
        /// Wires this manager's reference to PlacementManager (#25), used by HandleJobOfferInput to
        /// avoid processing a dock click while container placement mode is active — the same
        /// left/right-click-already-means-something-else guard ReceivingManager's own dock click
        /// (#15) already established. Called once by Bootstrapper after both managers exist.
        /// </summary>
        public void SetPlacementManager(PlacementManager placementManager)
        {
            _placementManager = placementManager;
        }

        /// <summary>
        /// Wires this manager's reference to WeightManager (#26), needed so a Special-tier offer's
        /// weight target can be sized against WeightManager.GetTotalCapacityKg() — the platform's
        /// *current* total capacity, not a frozen snapshot. Called once by Bootstrapper after both
        /// managers exist (WeightManager is already created earlier in Bootstrapper.Awake(), so no
        /// reordering is needed).
        /// </summary>
        public void SetWeightManager(WeightManager weightManager)
        {
            _weightManager = weightManager;
        }

        /// <summary>
        /// Injects this manager's data references, finds every ShippingDock in the scene, and
        /// registers each one's own cell with GridManager as its input cell — the same cell its
        /// ConnectionPoint occupies, not a separate cell further out. That cell must stay reserved
        /// (never hold a real conveyor segment): TryFulfillAt only ever fires when AdvanceGoods
        /// finds a non-segment cell ahead of an agent, so a real segment placed there would silently
        /// swallow the hand-off forever — registering it makes ConveyorManager.IsRunValid reject
        /// such a placement outright, the same way it already rejects running a belt through a
        /// placed container. Collapsing what used to be two separate reserved cells (the dock's own
        /// cell, then a further input cell derived from GetInputDirection) into this one cell halves
        /// the unplaceable buffer in front of the dock — the same fix applied to Receiving's output
        /// cell (see ReceivingManager) after a direct user report that the old two-cell gap felt
        /// broken (a player naturally tries to place flush against the dock and gets rejected).
        /// Shipping can't go all the way to zero cells like Receiving did, since its mechanic
        /// requires this one cell to stay real-segment-free — but one reserved cell, bridged by the
        /// energy connector, is the minimum this mechanic allows. Called once by Bootstrapper
        /// immediately after creation — deliberately not done in Awake(), since Bootstrapper creates
        /// this manager via AddComponent(), which fires Awake() synchronously before Initialize()
        /// has set any of these references (see CLAUDE.md's Camera.main precedent for the same
        /// pitfall).
        /// </summary>
        public void Initialize(
            GridConfig gridConfig,
            GridManager gridManager,
            StorageManager storageManager,
            ShippingScheduleData schedule,
            OrderEventChannel eventChannel,
            WaveEscalationData waveData,
            RandomJobPoolData jobPoolData,
            SpecialEventData specialEventData)
        {
            _gridConfig = gridConfig;
            _gridManager = gridManager;
            _storageManager = storageManager;
            _schedule = schedule;
            _eventChannel = eventChannel;
            _waveData = waveData;
            _jobPoolData = jobPoolData;
            _specialEventData = specialEventData;
            _specialSpawnCooldown = specialEventData != null ? specialEventData.spawnIntervalSeconds : 0f;

            _activatedFlags = new bool[schedule != null && schedule.scheduledOrders != null ? schedule.scheduledOrders.Length : 0];

            if (_gridConfig == null || _gridManager == null)
            {
                return;
            }

            foreach (ShippingDock dock in FindObjectsByType<ShippingDock>(FindObjectsSortMode.None))
            {
                Vector3Int dockCell = _gridConfig.WorldToCell3D(dock.ConnectionPoint.position);
                _gridManager.Register(dockCell);
                _dockInputCells.Add(dockCell);
                _docks.Add(dock);
            }
        }

        /// <summary>
        /// Caches the main camera once rather than querying Camera.main every Update — same
        /// pattern ReceivingManager uses for its own dock-click raycast (#15).
        /// </summary>
        private void Awake()
        {
            _mainCamera = Camera.main;
        }

        /// <summary>
        /// Returns the input cell of the first registered ShippingDock — the same cell its
        /// ConnectionPoint occupies — or null if none exist yet. Used by ConveyorManager to know
        /// which cell a real conveyor run needs to feed into for its energy-connector visual to
        /// bridge into the dock — assumes a single dock, matching this project's current
        /// single-ShippingDock scope.
        /// </summary>
        public Vector3Int? GetPrimaryInputCell()
        {
            return _docks.Count > 0 ? _gridConfig.WorldToCell3D(_docks[0].ConnectionPoint.position) : (Vector3Int?)null;
        }

        /// <summary>
        /// Returns the first registered ShippingDock's connection-point Transform, or null if none
        /// exist yet — the exact world point ConveyorManager's energy-connector visual should
        /// terminate at.
        /// </summary>
        public Transform GetPrimaryConnectionPoint()
        {
            return _docks.Count > 0 ? _docks[0].ConnectionPoint : null;
        }

        /// <summary>
        /// Advances the schedule clock (activating any order whose delay has elapsed), generates a
        /// wave order once the authored schedule is exhausted (#13), refreshes the Random-tier job
        /// offer pool and handles its dock click input (#25), ticks every active order's deadline
        /// (expiring any that hit zero), and attempts automatic dispatch from storage for every
        /// active order's remaining need. The job offer pool refreshes/handles input even if the
        /// authored schedule is empty — Random events don't depend on Priority's schedule existing.
        /// </summary>
        private void Update()
        {
            if (!_isGameActive)
            {
                return;
            }

            RefreshJobOfferPool(Time.deltaTime);
            RefreshSpecialEventOffer(Time.deltaTime);
            HandleJobOfferInput();

            if (_schedule == null || _schedule.scheduledOrders == null)
            {
                return;
            }

            _elapsedSeconds += Time.deltaTime;
            ActivateDueOrders();
            GenerateWaveOrders(Time.deltaTime);
            TickDeadlines(Time.deltaTime);
            TryAutomaticDispatch();
        }

        /// <summary>
        /// Activates every scheduled order whose startDelaySeconds has elapsed and hasn't already
        /// been activated.
        /// </summary>
        private void ActivateDueOrders()
        {
            for (int i = 0; i < _schedule.scheduledOrders.Length; i++)
            {
                OrderData data = _schedule.scheduledOrders[i];
                if (_activatedFlags[i] || data == null || _elapsedSeconds < data.startDelaySeconds)
                {
                    continue;
                }

                _activatedFlags[i] = true;
                _activeOrders.Add(new ActiveOrder(data));
                _eventChannel?.RaiseOrderActivated(data);
            }
        }

        /// <summary>
        /// Returns true once every scheduled order has activated (or the schedule was empty to
        /// begin with) — the signal that wave generation should start filling in for the Company.
        /// </summary>
        private bool IsScheduleExhausted()
        {
            foreach (bool activated in _activatedFlags)
            {
                if (!activated)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Once the authored schedule is exhausted, spawns a new generated order on a timer that
        /// shrinks every wave (#13) — no-ops if WaveEscalationData isn't assigned or has no goods to
        /// draw from, so a project without wave data configured behaves exactly as before.
        /// </summary>
        private void GenerateWaveOrders(float deltaTime)
        {
            if (_waveData == null || _waveData.goodsPool == null || _waveData.goodsPool.Length == 0 || !IsScheduleExhausted())
            {
                return;
            }

            _waveSpawnTimer += deltaTime;
            float interval = Mathf.Max(_waveData.minSpawnIntervalSeconds,
                _waveData.baseSpawnIntervalSeconds - _waveData.spawnIntervalReductionPerWave * _waveNumber);

            if (_waveSpawnTimer < interval)
            {
                return;
            }

            _waveSpawnTimer -= interval;
            SpawnWaveOrder();
            _waveNumber++;
        }

        /// <summary>
        /// Creates one runtime OrderData for a random goods type from WaveEscalationData's pool,
        /// with this wave's quantity/deadline, and activates it through the exact same path an
        /// authored order uses. The instance isn't a project asset — see DestroyIfGenerated for
        /// cleanup once this order leaves _activeOrders.
        /// </summary>
        private void SpawnWaveOrder()
        {
            GoodsData goods = _waveData.goodsPool[Random.Range(0, _waveData.goodsPool.Length)];

            OrderData data = ScriptableObject.CreateInstance<OrderData>();
            data.displayName = $"Company Order (wave {_waveNumber + 1})";
            data.tier = OrderTier.Priority;
            data.requiredGoods = goods;
            data.requiredQuantity = _waveData.baseQuantity + _waveData.quantityGrowthPerWave * _waveNumber;
            data.deadlineSeconds = Mathf.Max(_waveData.minDeadlineSeconds,
                _waveData.baseDeadlineSeconds - _waveData.deadlineReductionPerWave * _waveNumber);

            _generatedOrders.Add(data);
            _activeOrders.Add(new ActiveOrder(data));
            _eventChannel?.RaiseOrderActivated(data);
        }

        /// <summary>
        /// Destroys and untracks the given OrderData if it was runtime-generated by SpawnWaveOrder
        /// (a no-op for authored, asset-backed orders) — called at every point an order leaves
        /// _activeOrders, since wave generation runs indefinitely and would otherwise leak one
        /// ScriptableObject instance per generated order for the rest of the run.
        /// </summary>
        private void DestroyIfGenerated(OrderData data)
        {
            if (_generatedOrders.Remove(data))
            {
                Destroy(data);
            }
        }

        /// <summary>
        /// Keeps the Random-tier job offer pool (#25) at RandomJobPoolData.poolSize, generating a
        /// fresh offer for any empty slot and replacing any offer whose RemainingLifetime just hit
        /// zero (destroying the expired one via DestroyIfGenerated first, since every offer is a
        /// runtime-created OrderData exactly like a wave order — see GenerateJobOffer). Slots are
        /// replaced in place by index rather than removed/reinserted, so HighlightedOfferIndex never
        /// needs reindexing as offers churn. No-ops if RandomJobPoolData isn't assigned or has no
        /// goods to draw from, matching WaveEscalationData's own unassigned-safe convention.
        /// </summary>
        private void RefreshJobOfferPool(float deltaTime)
        {
            if (_jobPoolData == null || _jobPoolData.goodsPool == null || _jobPoolData.goodsPool.Length == 0)
            {
                return;
            }

            while (_jobOffers.Count < _jobPoolData.poolSize)
            {
                _jobOffers.Add((GenerateJobOffer(), _jobPoolData.offerLifetimeSeconds));
            }

            for (int i = 0; i < _jobOffers.Count; i++)
            {
                float remaining = _jobOffers[i].RemainingLifetime - deltaTime;
                if (remaining <= 0f)
                {
                    DestroyIfGenerated(_jobOffers[i].Order);
                    _jobOffers[i] = (GenerateJobOffer(), _jobPoolData.offerLifetimeSeconds);
                }
                else
                {
                    _jobOffers[i] = (_jobOffers[i].Order, remaining);
                }
            }
        }

        /// <summary>
        /// Keeps the rare Special-tier offer (#26) available: while neither a pending offer nor an
        /// active bundle exists, counts down _specialSpawnCooldown and generates a fresh preview
        /// once it elapses, sized against WeightManager's *current* total capacity. A pending offer
        /// persists indefinitely once generated (no separate expiry) until the player accepts it —
        /// deliberately simpler than the Random pool's churn, matching a rare/precious offer rather
        /// than a constantly-rotating one. No-ops if SpecialEventData/WeightManager isn't assigned,
        /// or while a bundle from a previous acceptance is still active (see HandleOrderLeftActive,
        /// which starts the cooldown only once every bundle member has left _activeOrders).
        /// </summary>
        private void RefreshSpecialEventOffer(float deltaTime)
        {
            if (_specialEventData == null || _specialEventData.goodsPool == null
                || _specialEventData.goodsPool.Length == 0 || _weightManager == null
                || _activeSpecialBundle.Count > 0 || PendingSpecialOffer.HasValue)
            {
                return;
            }

            _specialSpawnCooldown -= deltaTime;
            if (_specialSpawnCooldown > 0f)
            {
                return;
            }

            float totalWeightTargetKg = _weightManager.GetTotalCapacityKg() * _specialEventData.weightFractionTarget;
            int totalMoneyReward = Random.Range(_specialEventData.moneyRewardMin, _specialEventData.moneyRewardMax + 1);
            PendingSpecialOffer = new SpecialOfferPreview(totalWeightTargetKg, totalMoneyReward, _specialEventData.deadlineSeconds);
        }

        /// <summary>
        /// Creates one runtime Random-tier OrderData offer (#25) for a random goods type from
        /// RandomJobPoolData's pool, with a random quantity/deadline/moneyReward within its tunable
        /// ranges. Tracked in _generatedOrders immediately, same as a wave order, so
        /// DestroyIfGenerated cleans it up correctly whether it's ultimately accepted (and later
        /// leaves _activeOrders via fulfillment/expiry) or simply expires unpicked in the pool.
        /// </summary>
        private OrderData GenerateJobOffer()
        {
            GoodsData goods = _jobPoolData.goodsPool[Random.Range(0, _jobPoolData.goodsPool.Length)];

            OrderData data = ScriptableObject.CreateInstance<OrderData>();
            data.displayName = $"Odd Job: {goods.displayName}";
            data.tier = OrderTier.Random;
            data.requiredGoods = goods;
            data.requiredQuantity = Random.Range(_jobPoolData.quantityMin, _jobPoolData.quantityMax + 1);
            data.deadlineSeconds = Random.Range(_jobPoolData.deadlineSecondsMin, _jobPoolData.deadlineSecondsMax);
            data.moneyReward = Random.Range(_jobPoolData.moneyRewardMin, _jobPoolData.moneyRewardMax + 1);

            _generatedOrders.Add(data);
            return data;
        }

        /// <summary>
        /// Left-click cycles which offer is highlighted; right-click accepts the highlighted one —
        /// both only when the raycast hits a registered ShippingDock's own geometry, and only
        /// outside both placement modes (#25, mirroring ReceivingManager's #15 dock-click gating
        /// exactly), since either click already means something else there. The cyclable range
        /// covers the Random pool's own slots plus one virtual extra slot for the pending Special
        /// offer when one exists (#26) — index == JobOffers.Count means "the Special slot" — so the
        /// player experiences one unified interaction even though the two offer kinds are tracked
        /// completely separately underneath (see PendingSpecialOffer's own doc comment for why).
        /// </summary>
        private void HandleJobOfferInput()
        {
            int totalSlots = _jobOffers.Count + (PendingSpecialOffer.HasValue ? 1 : 0);
            if (_mainCamera == null || Mouse.current == null || totalSlots == 0)
            {
                return;
            }

            bool leftClicked = Mouse.current.leftButton.wasPressedThisFrame;
            bool rightClicked = Mouse.current.rightButton.wasPressedThisFrame;
            if (!leftClicked && !rightClicked)
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

            ShippingDock dock = hit.collider.GetComponentInParent<ShippingDock>();
            if (dock == null || !_docks.Contains(dock))
            {
                return;
            }

            if (_highlightedOfferIndex >= totalSlots)
            {
                _highlightedOfferIndex = 0;
            }

            if (leftClicked)
            {
                _highlightedOfferIndex = (_highlightedOfferIndex + 1) % totalSlots;
            }
            else if (_highlightedOfferIndex < _jobOffers.Count)
            {
                TryAcceptHighlightedRandomOffer();
            }
            else
            {
                TryAcceptSpecialOffer();
            }
        }

        /// <summary>
        /// Accepts the currently-highlighted Random-tier job offer (#25) — a no-op if a Random-tier
        /// order is already active, since at most one can be active at a time (accepting is a real
        /// commitment, not a second stream of endless work). Otherwise the chosen offer is promoted
        /// straight into _activeOrders through the same activation path every other order uses, and
        /// its now-empty pool slot is immediately refilled with a freshly generated offer.
        /// </summary>
        private void TryAcceptHighlightedRandomOffer()
        {
            if (_activeOrders.Any(order => order.Data.tier == OrderTier.Random))
            {
                return;
            }

            OrderData chosen = _jobOffers[_highlightedOfferIndex].Order;
            _jobOffers[_highlightedOfferIndex] = (GenerateJobOffer(), _jobPoolData.offerLifetimeSeconds);

            _activeOrders.Add(new ActiveOrder(chosen));
            _eventChannel?.RaiseOrderActivated(chosen);
        }

        /// <summary>
        /// Accepts the pending Special-tier offer (#26) — splits its total weight target and money
        /// reward evenly across SpecialEventData.bundleSize ordinary OrderData instances (a random
        /// goods type per order, quantity derived from that type's own weightKg so the *combined*
        /// bundle hits the target), activates all of them together through the same path every
        /// other order uses, and clears the pending preview. A no-op if there's no pending offer.
        /// </summary>
        private void TryAcceptSpecialOffer()
        {
            if (!PendingSpecialOffer.HasValue)
            {
                return;
            }

            SpecialOfferPreview offer = PendingSpecialOffer.Value;
            float perOrderTargetKg = offer.TotalWeightTargetKg / _specialEventData.bundleSize;
            int perOrderReward = offer.TotalMoneyReward / _specialEventData.bundleSize;

            for (int i = 0; i < _specialEventData.bundleSize; i++)
            {
                GoodsData goods = _specialEventData.goodsPool[Random.Range(0, _specialEventData.goodsPool.Length)];

                OrderData data = ScriptableObject.CreateInstance<OrderData>();
                data.displayName = $"Special Project ({i + 1}/{_specialEventData.bundleSize})";
                data.tier = OrderTier.Special;
                data.requiredGoods = goods;
                data.requiredQuantity = Mathf.Max(1, Mathf.CeilToInt(perOrderTargetKg / goods.weightKg));
                data.deadlineSeconds = offer.DeadlineSeconds;
                data.moneyReward = perOrderReward;

                _generatedOrders.Add(data);
                _activeSpecialBundle.Add(data);
                _activeOrders.Add(new ActiveOrder(data));
                _eventChannel?.RaiseOrderActivated(data);
            }

            PendingSpecialOffer = null;
        }

        /// <summary>
        /// Called wherever an order leaves _activeOrders (fulfilled or expired, #26) — if it was a
        /// Special-tier bundle member, untracks it, and once the *whole* bundle has left (every
        /// member either fulfilled or expired), starts the spawn cooldown for the next Special
        /// offer. A no-op for any order that was never part of a Special bundle.
        /// </summary>
        private void HandleOrderLeftActiveOrders(OrderData data)
        {
            if (_activeSpecialBundle.Remove(data) && _activeSpecialBundle.Count == 0)
            {
                _specialSpawnCooldown = _specialEventData != null ? _specialEventData.spawnIntervalSeconds : 0f;
            }
        }

        /// <summary>
        /// Advances every active order's deadline countdown, removing and reporting any that just
        /// expired unfulfilled.
        /// </summary>
        private void TickDeadlines(float deltaTime)
        {
            for (int i = _activeOrders.Count - 1; i >= 0; i--)
            {
                ActiveOrder order = _activeOrders[i];
                order.Tick(deltaTime);

                if (order.IsExpired)
                {
                    _activeOrders.RemoveAt(i);
                    _eventChannel?.RaiseOrderMissed(order.Data);
                    HandleOrderLeftActiveOrders(order.Data);
                    DestroyIfGenerated(order.Data);
                }
            }
        }

        /// <summary>
        /// For each active order, most-complete-first (#24 — see OrdersByPriority), dispatches
        /// matching goods from storage onto a belt (#22 — StorageManager.TryDispatch, which only
        /// withdraws once it's found a real, currently-open conveyor segment to spawn onto) one
        /// unit at a time until either the order's remaining need (still-undelivered *and*
        /// already-in-transit units together, Delivered + Reserved) is fully claimed or storage has
        /// no more dispatchable unit of that type, re-checking after every unit so dispatch never
        /// outpaces demand. Unlike direct delivery, a dispatched unit does not credit the order
        /// here — it only does once it physically arrives at Shipping via TryFulfillAt, same as any
        /// other delivery. Orders are only ever removed from _activeOrders once actually IsFulfilled
        /// (Delivered reaching requiredQuantity), which now only ever happens through TryFulfillAt.
        /// </summary>
        private void TryAutomaticDispatch()
        {
            if (_storageManager == null || _conveyorManager == null)
            {
                return;
            }

            foreach ((ActiveOrder order, int _) in OrdersByPriority())
            {
                while (order.Delivered + order.Reserved < order.Data.requiredQuantity
                    && _storageManager.TryDispatch(order.Data.requiredGoods, out Vector3Int spawnCell))
                {
                    order.Reserve();
                    SpawnDispatchedAgent(order.Data.requiredGoods, spawnCell);
                }
            }
        }

        // Reusable buffer for OrdersByPriority (#28) — this method runs unconditionally every
        // frame via TryAutomaticDispatch, so a fresh LINQ query (OrderBy/ThenByDescending, each
        // allocating its own enumerator/comparer) every call was the one allocation in this
        // project found to never stop for the life of a run. Cleared and repopulated each call
        // rather than replaced, so its backing array is reused once warmed up. Carries each
        // order's original _activeOrders index alongside it (see CompareByPriority) since
        // List&lt;T&gt;.Sort, unlike the LINQ chain it replaces, is not a stable sort.
        private readonly List<(ActiveOrder Order, int OriginalIndex)> _orderedOrdersBuffer = new List<(ActiveOrder, int)>();

        /// <summary>
        /// Returns every active order ordered by fulfillment priority — tier first (#25: Priority
        /// always beats Random for the same goods type, "the Company always takes priority", per
        /// OrderTier's declaration order Priority=0 &lt; Random=1 &lt; Special=2), then within a tier
        /// whichever is closest to finishing (#24 — highest Delivered/requiredQuantity ratio), so
        /// same-tier orders competing for the same type "finish in sequence" rather than several
        /// inching forward together. Ties on both keys (most commonly several freshly-activated
        /// same-tier orders all at 0%) fall back to activation order — since #28 replaced the
        /// original stable LINQ sort with List&lt;T&gt;.Sort (not stable), each entry's original
        /// _activeOrders index is carried alongside it and used as CompareByPriority's explicit
        /// final tiebreak, reproducing the same behavior the LINQ chain's stability used to give
        /// for free. Returns entries paired with their (now-unneeded-by-callers) original index
        /// rather than sorting _activeOrders in place, since that list's own order is also read by
        /// ShippingHUD for on-screen display and nothing has asked for that to change. The returned
        /// list is a reused buffer, not a snapshot — callers must finish iterating it before this is
        /// called again (true today: both call sites fully consume it within one synchronous
        /// foreach, and the two call sites never nest, since they run from different managers'
        /// separate Update() calls).
        /// </summary>
        private List<(ActiveOrder Order, int OriginalIndex)> OrdersByPriority()
        {
            _orderedOrdersBuffer.Clear();
            for (int i = 0; i < _activeOrders.Count; i++)
            {
                _orderedOrdersBuffer.Add((_activeOrders[i], i));
            }

            _orderedOrdersBuffer.Sort(CompareByPriority);
            return _orderedOrdersBuffer;
        }

        /// <summary>
        /// Comparison backing OrdersByPriority's sort (#28) — tier ascending, then percent-complete
        /// descending, then original activation-order index ascending as an explicit tiebreak (see
        /// OrdersByPriority's own doc comment for why this replaces LINQ's free stability).
        /// </summary>
        private static int CompareByPriority((ActiveOrder Order, int OriginalIndex) a, (ActiveOrder Order, int OriginalIndex) b)
        {
            int tierCompare = a.Order.Data.tier.CompareTo(b.Order.Data.tier);
            if (tierCompare != 0)
            {
                return tierCompare;
            }

            float percentA = a.Order.Delivered / (float)a.Order.Data.requiredQuantity;
            float percentB = b.Order.Delivered / (float)b.Order.Data.requiredQuantity;
            int percentCompare = percentB.CompareTo(percentA);
            if (percentCompare != 0)
            {
                return percentCompare;
            }

            return a.OriginalIndex.CompareTo(b.OriginalIndex);
        }

        /// <summary>
        /// Instantiates a GoodsAgent at the given belt cell for a unit StorageManager just
        /// withdrew (#22) — mirrors ReceivingManager's own spawn pattern exactly (same prefab
        /// instantiation, same GoodsRestPosition height, same RegisterGoodsAgent entry point), so a
        /// storage-dispatched good moves and gets credited identically to any other delivery.
        /// </summary>
        private void SpawnDispatchedAgent(GoodsData goodsData, Vector3Int cell)
        {
            GameObject instance = Instantiate(goodsData.prefab, _conveyorManager.GoodsRestPosition(cell), Quaternion.identity);
            GoodsAgent agent = instance.AddComponent<GoodsAgent>();
            agent.Initialize(goodsData, cell);
            _conveyorManager.RegisterGoodsAgent(agent);
        }

        /// <summary>
        /// Attempts to fulfill one unit of the given goods type with a good that physically arrived
        /// at a registered Shipping dock's input cell — the active order that still needs this type
        /// and is closest to finishing is credited first (#24 — see OrdersByPriority). Called by
        /// ConveyorManager when a GoodsAgent reaches such a cell.
        /// </summary>
        /// <returns>True if some active order accepted and consumed the unit; false if the cell
        /// isn't a dock input cell, or no active order currently needs this type — the caller
        /// should leave the agent in place either way.</returns>
        public bool TryFulfillAt(Vector3Int cell, GoodsData goodsData)
        {
            if (!_dockInputCells.Contains(cell))
            {
                return false;
            }

            foreach ((ActiveOrder order, int _) in OrdersByPriority())
            {
                if (order.Data.requiredGoods != goodsData || order.IsFulfilled)
                {
                    continue;
                }

                order.Deliver();

                if (order.IsFulfilled)
                {
                    _activeOrders.Remove(order);
                    _eventChannel?.RaiseOrderFulfilled(order.Data);
                    HandleOrderLeftActiveOrders(order.Data);
                    DestroyIfGenerated(order.Data);
                }

                return true;
            }

            return false;
        }
    }
}
