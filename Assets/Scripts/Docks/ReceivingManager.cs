using System.Collections.Generic;
using StorageLord.Conveyors;
using StorageLord.Goods;
using StorageLord.Grid;
using UnityEngine;

namespace StorageLord.Docks
{
    /// <summary>
    /// Spawns goods automatically at every ReceivingDock in the scene, on a fixed timer, and hands
    /// them to ConveyorManager so they move along whatever belt is connected — the platform's first
    /// real goods source, replacing the need for ConveyorManager's temporary debug spawn action.
    /// Docks are fixed/Editor-placed (not runtime-placed), but still register their own cell with
    /// GridManager at startup so PlacementManager/ConveyorManager can't be confirmed on top of one.
    ///
    /// Created and wired by Bootstrapper — not placed directly in a scene, since it has no
    /// serialized Inspector fields to wire (its data references are injected via Initialize()).
    /// </summary>
    public class ReceivingManager : MonoBehaviour
    {
        private GridConfig _gridConfig;
        private GridManager _gridManager;
        private ConveyorManager _conveyorManager;
        private ReceivingData _receivingData;

        private readonly List<ReceivingDock> _docks = new List<ReceivingDock>();
        private readonly Dictionary<ReceivingDock, GoodsAgent> _lastSpawnedByDock = new Dictionary<ReceivingDock, GoodsAgent>();

        private float _spawnTimer;

        /// <summary>
        /// Injects this manager's data references, finds every ReceivingDock in the scene, and
        /// registers each one's own cell with GridManager so runtime placement can't confirm a
        /// piece on top of a fixed dock. Called once by Bootstrapper immediately after creation —
        /// deliberately not done in Awake(), since Bootstrapper creates this manager via
        /// AddComponent(), which fires Awake() synchronously before Initialize() has set any of
        /// these references (see CLAUDE.md's Camera.main precedent for the same pitfall).
        /// </summary>
        public void Initialize(GridConfig gridConfig, GridManager gridManager, ConveyorManager conveyorManager, ReceivingData receivingData)
        {
            _gridConfig = gridConfig;
            _gridManager = gridManager;
            _conveyorManager = conveyorManager;
            _receivingData = receivingData;

            _docks.AddRange(FindObjectsByType<ReceivingDock>(FindObjectsSortMode.None));

            if (_gridConfig == null || _gridManager == null)
            {
                return;
            }

            foreach (ReceivingDock dock in _docks)
            {
                _gridManager.Register(_gridConfig.WorldToCell3D(dock.ConnectionPoint.position));
            }
        }

        /// <summary>
        /// Advances the shared spawn timer; once it crosses the configured interval, attempts a
        /// spawn at every dock and resets the timer.
        /// </summary>
        private void Update()
        {
            if (_receivingData == null || _receivingData.goodsData == null || _receivingData.goodsData.prefab == null
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
                TrySpawnAt(dock);
            }
        }

        /// <summary>
        /// Spawns one good onto the given dock's output cell — but only once a real conveyor
        /// segment is actually placed there (otherwise the good would hover in open space with
        /// nothing to carry it, since GoodsRestPosition is a pure coordinate computation that
        /// doesn't care whether a segment GameObject exists). Also skipped if the good this dock
        /// spawned last time is still sitting there unmoved, rather than stacking a second good on
        /// top of it.
        /// </summary>
        private void TrySpawnAt(ReceivingDock dock)
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
                _receivingData.goodsData.prefab, _conveyorManager.GoodsRestPosition(outputCell), Quaternion.identity);
            GoodsAgent agent = instance.AddComponent<GoodsAgent>();
            agent.Initialize(_receivingData.goodsData, outputCell);
            _conveyorManager.RegisterGoodsAgent(agent);
            _lastSpawnedByDock[dock] = agent;
        }

        /// <summary>
        /// Returns the 3D cell directly in front of the given dock's connection point, per its
        /// facing.
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
