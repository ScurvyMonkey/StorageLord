using System.Collections.Generic;
using StorageLord.Goods;
using StorageLord.Grid;
using StorageLord.Storage;
using UnityEngine;

namespace StorageLord.Docks
{
    /// <summary>
    /// Runs the Company's order queue: activates each OrderData in ShippingScheduleData once its
    /// own startDelaySeconds elapses (multiple orders can be active at once, each on its own
    /// deadline), and fulfills them two ways — goods arriving directly at a ShippingDock's input
    /// cell via conveyor (TryFulfillAt, called by ConveyorManager), and automatic withdrawal from
    /// StorageManager's containers each frame. When multiple active orders want the same goods
    /// type, the oldest-activated one is credited first, since _activeOrders is always appended to
    /// in activation order and every lookup scans it front-to-back.
    ///
    /// Created and wired by Bootstrapper — not placed directly in a scene, since it has no
    /// serialized Inspector fields to wire (its data references are injected via Initialize()).
    /// </summary>
    public class ShippingManager : MonoBehaviour
    {
        private GridConfig _gridConfig;
        private GridManager _gridManager;
        private StorageManager _storageManager;
        private ShippingScheduleData _schedule;
        private OrderEventChannel _eventChannel;

        private bool[] _activatedFlags;
        private float _elapsedSeconds;

        private readonly List<ActiveOrder> _activeOrders = new List<ActiveOrder>();
        private readonly HashSet<Vector3Int> _dockInputCells = new HashSet<Vector3Int>();
        private readonly List<ShippingDock> _docks = new List<ShippingDock>();

        /// <summary>
        /// Every currently active order, oldest-activated first, read-only for observers like
        /// ShippingHUD.
        /// </summary>
        public IReadOnlyList<ActiveOrder> ActiveOrders => _activeOrders;

        /// <summary>
        /// Injects this manager's data references, finds every ShippingDock in the scene,
        /// registers each one's own cell with GridManager, and records each one's input cell.
        /// The input cell is also registered with GridManager (unlike Receiving's output cell,
        /// which deliberately must hold a real conveyor segment) — TryFulfillAt only ever fires
        /// when AdvanceGoods finds a non-segment cell ahead of an agent, so a real segment placed
        /// directly on the input cell would silently swallow that hand-off forever; registering it
        /// makes ConveyorManager.IsRunValid reject such a placement outright, the same way it
        /// already rejects running a belt through a placed container. Called once by Bootstrapper
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
            OrderEventChannel eventChannel)
        {
            _gridConfig = gridConfig;
            _gridManager = gridManager;
            _storageManager = storageManager;
            _schedule = schedule;
            _eventChannel = eventChannel;

            _activatedFlags = new bool[schedule != null && schedule.scheduledOrders != null ? schedule.scheduledOrders.Length : 0];

            if (_gridConfig == null || _gridManager == null)
            {
                return;
            }

            foreach (ShippingDock dock in FindObjectsByType<ShippingDock>(FindObjectsSortMode.None))
            {
                Vector3Int dockCell = _gridConfig.WorldToCell3D(dock.ConnectionPoint.position);
                _gridManager.Register(dockCell);

                Vector3Int inputCell = dockCell + dock.GetInputDirection();
                _gridManager.Register(inputCell);
                _dockInputCells.Add(inputCell);
                _docks.Add(dock);
            }
        }

        /// <summary>
        /// Returns the input cell of the first registered ShippingDock, or null if none exist yet.
        /// Used by ConveyorManager to know which cell a real conveyor run needs to feed into for
        /// its energy-connector visual to bridge into the dock — assumes a single dock, matching
        /// this project's current single-ShippingDock scope.
        /// </summary>
        public Vector3Int? GetPrimaryInputCell()
        {
            return _docks.Count > 0 ? _gridConfig.WorldToCell3D(_docks[0].ConnectionPoint.position) + _docks[0].GetInputDirection() : (Vector3Int?)null;
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
        /// Advances the schedule clock (activating any order whose delay has elapsed), ticks every
        /// active order's deadline (expiring any that hit zero), and attempts automatic withdrawal
        /// from storage for every active order's remaining need.
        /// </summary>
        private void Update()
        {
            if (_schedule == null || _schedule.scheduledOrders == null)
            {
                return;
            }

            _elapsedSeconds += Time.deltaTime;
            ActivateDueOrders();
            TickDeadlines(Time.deltaTime);
            TryAutomaticWithdrawals();
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
                }
            }
        }

        /// <summary>
        /// For each active order (oldest first), withdraws matching goods from storage one unit at
        /// a time until either the order is satisfied or storage has no more of that type,
        /// re-checking remaining need after every unit so withdrawal never outpaces demand.
        /// </summary>
        private void TryAutomaticWithdrawals()
        {
            if (_storageManager == null)
            {
                return;
            }

            foreach (ActiveOrder order in _activeOrders)
            {
                while (!order.IsFulfilled && _storageManager.TryWithdraw(order.Data.requiredGoods))
                {
                    order.Deliver();
                }
            }

            for (int i = _activeOrders.Count - 1; i >= 0; i--)
            {
                if (_activeOrders[i].IsFulfilled)
                {
                    ActiveOrder fulfilled = _activeOrders[i];
                    _activeOrders.RemoveAt(i);
                    _eventChannel?.RaiseOrderFulfilled(fulfilled.Data);
                }
            }
        }

        /// <summary>
        /// Attempts to fulfill one unit of the given goods type with a good that physically arrived
        /// at a registered Shipping dock's input cell — the oldest active order that still needs
        /// this type is credited first. Called by ConveyorManager when a GoodsAgent reaches such a
        /// cell.
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

            foreach (ActiveOrder order in _activeOrders)
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
                }

                return true;
            }

            return false;
        }
    }
}
