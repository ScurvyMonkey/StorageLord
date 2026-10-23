using System.Collections.Generic;
using StorageLord.Audio;
using StorageLord.Conveyors;
using StorageLord.Core;
using StorageLord.Docks;
using StorageLord.Grid;
using StorageLord.Storage;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace StorageLord.Placement
{
    /// <summary>
    /// Third runtime placer (#51): lets the player buy and place extra compact Receiving buildings,
    /// each spawning its own goods selection onto the belt at its output side — so scaling
    /// throughput is a player decision rather than a tuning constant. B toggles the mode (the
    /// Upgrade Store's Receiving card enters it too); R rotates the output side; left-click buys
    /// and places at the ghost's cell; right-click on a placed building outside every mode removes
    /// it (no refund). Each building occupies exactly one deck cell, needs floor under it (#46),
    /// and its occupancy goes through GridManager like every other placer's. Registered with
    /// GridManager's placement-mode registry, so it's mutually exclusive with container and conveyor
    /// placement. Created and wired by Bootstrapper.
    /// </summary>
    public class ReceivingBuildingPlacer : MonoBehaviour, IPlacementModeOwner
    {
        private static readonly Color ValidPreviewTint = new Color(0.4f, 1f, 0.4f, 0.6f);
        private static readonly Color InvalidPreviewTint = new Color(1f, 0.3f, 0.3f, 0.6f);
        private static readonly Color AffordableLabelColor = new Color(0.55f, 1f, 0.55f);
        private static readonly Color UnaffordableLabelColor = new Color(1f, 0.45f, 0.45f);
        private const float PriceLabelHeight = 7.5f;

        private GridConfig _gridConfig;
        private GridManager _gridManager;
        private PlacementEventChannel _eventChannel;
        private ReceivingBuildingData _data;
        private ReceivingManager _receivingManager;
        private ConveyorManager _conveyorManager;
        private WeightManager _weightManager;
        private ScoreManager _scoreManager;
        private SoundManager _soundManager;
        private Camera _mainCamera;

        private readonly Dictionary<Vector3Int, GameObject> _placedBuildings = new Dictionary<Vector3Int, GameObject>();

        private bool _isPlacing;
        private bool _isGameActive = true;
        private float _currentYRotation;
        private GameObject _previewInstance;
        private TextMeshPro _priceLabel;
        private bool _isPreviewValid = true;
        private int _shownCost = -1;
        private bool _shownAffordable;

        /// <summary>True while Receiving-building placement mode is active.</summary>
        public bool IsPlacementModeActive => _isPlacing;

        /// <summary>Price of the next building, from ReceivingBuildingData and how many extra buildings already exist (-1 if unconfigured).</summary>
        public int NextCost => _data != null && _receivingManager != null ? _data.CostForNext(_receivingManager.PlacedBuildingCount) : -1;

        /// <summary>
        /// Injects this placer's references and registers it with GridManager's placement-mode
        /// registry. Called once by Bootstrapper after every referenced manager exists.
        /// </summary>
        public void Initialize(
            GridConfig gridConfig,
            GridManager gridManager,
            PlacementEventChannel eventChannel,
            ReceivingBuildingData data,
            ReceivingManager receivingManager,
            ConveyorManager conveyorManager,
            WeightManager weightManager,
            ScoreManager scoreManager,
            SoundManager soundManager)
        {
            _gridConfig = gridConfig;
            _gridManager = gridManager;
            _eventChannel = eventChannel;
            _data = data;
            _receivingManager = receivingManager;
            _conveyorManager = conveyorManager;
            _weightManager = weightManager;
            _scoreManager = scoreManager;
            _soundManager = soundManager;
            _gridManager?.RegisterPlacer(this);
        }

        /// <summary>
        /// Halts (or resumes) all placement/removal input — called by GameManager when the run
        /// ends. Deactivating force-exits placement mode.
        /// </summary>
        public void SetGameActive(bool active)
        {
            _isGameActive = active;
            if (!active)
            {
                ExitPlacementMode();
            }
        }

        /// <summary>
        /// Enters placement mode (exiting every other one), unless the run is over or data is
        /// missing. Called by the B key and by the Upgrade Store's Receiving-building card.
        /// </summary>
        public void EnterPlacementMode()
        {
            if (_isPlacing || !_isGameActive)
            {
                return;
            }

            if (_gridConfig == null || _gridManager == null || _receivingManager == null || _data == null || _data.prefab == null)
            {
                Debug.LogWarning("ReceivingBuildingPlacer: cannot enter placement mode — ReceivingBuildingData/prefab or a manager reference is missing.");
                return;
            }

            _gridManager.ExitOtherPlacementModes(this);
            _isPlacing = true;
            _currentYRotation = 0f;
            BuildPreview();
        }

        /// <summary>
        /// Force-exits placement mode if active, destroying the ghost and its price label. Called
        /// through GridManager.ExitOtherPlacementModes when another mode is entered.
        /// </summary>
        public void ExitPlacementMode()
        {
            if (!_isPlacing)
            {
                return;
            }

            _isPlacing = false;
            DestroyPreview();
        }

        /// <summary>Cells of every placed building (#51) — WeightManager checks these against a collapsing platform tile.</summary>
        public List<Vector3Int> GetBuildingCells()
        {
            return new List<Vector3Int>(_placedBuildings.Keys);
        }

        /// <summary>
        /// Removes the building at <paramref name="cell"/>, if one is there: unregisters it from
        /// ReceivingManager and GridManager, destroys it, and recomputes conveyor topology. Used by
        /// right-click removal and by WeightManager's platform collapse. No refund.
        /// </summary>
        public void RemoveBuildingAt(Vector3Int cell)
        {
            if (!_placedBuildings.TryGetValue(cell, out GameObject building))
            {
                return;
            }

            _placedBuildings.Remove(cell);
            _receivingManager?.UnregisterDock(building.GetComponent<ReceivingDock>());
            _gridManager.Unregister(cell);
            Destroy(building);
            _conveyorManager?.NotifyReceivingOutputsChanged();
        }

        /// <summary>Caches the main camera once rather than querying Camera.main every Update.</summary>
        private void Awake()
        {
            _mainCamera = Camera.main;
        }

        /// <summary>
        /// Polls input each frame: B toggles placement mode; while active, drives rotation, the
        /// ghost and its price label, and confirm/cancel; otherwise handles right-click removal.
        /// </summary>
        private void Update()
        {
            if (!_isGameActive || Keyboard.current == null || Mouse.current == null)
            {
                return;
            }

            if (Keyboard.current.bKey.wasPressedThisFrame)
            {
                if (_isPlacing)
                {
                    ExitPlacementMode();
                }
                else
                {
                    EnterPlacementMode();
                }
            }

            if (_isPlacing)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame)
                {
                    _currentYRotation = _gridConfig.SnapYRotation(_currentYRotation + _gridConfig.rotationSnapDegrees);
                }

                UpdatePreview();

                if (Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    ExitPlacementMode();
                }
                else if (Mouse.current.leftButton.wasPressedThisFrame
                    && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
                {
                    TryConfirmPlacement();
                }
            }
            else
            {
                HandleRemoveInput();
            }
        }

        /// <summary>
        /// Moves the ghost to the aimed deck cell with the current rotation, re-tints it when its
        /// validity flips, and keeps the price label above it, facing the camera, coloured by
        /// affordability.
        /// </summary>
        private void UpdatePreview()
        {
            if (_previewInstance == null || _mainCamera == null)
            {
                return;
            }

            Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            Plane deckPlane = new Plane(Vector3.up, new Vector3(0f, _gridConfig.deckSurfaceHeight, 0f));
            if (!deckPlane.Raycast(ray, out float enter))
            {
                return;
            }

            Vector2Int column = _gridConfig.WorldToCell(ray.GetPoint(enter));
            Vector3Int cell = new Vector3Int(column.x, 0, column.y);
            Quaternion rotation = Quaternion.Euler(0f, _currentYRotation, 0f);
            _previewInstance.transform.SetPositionAndRotation(_gridConfig.Cell3DToWorld(cell), rotation);

            int cost = NextCost;
            bool affordable = _scoreManager == null || _scoreManager.Money >= cost;
            bool isValid = affordable && IsCellPlaceable(cell, OutputDirection(rotation));
            if (isValid != _isPreviewValid)
            {
                _isPreviewValid = isValid;
                SetPreviewTint(isValid ? ValidPreviewTint : InvalidPreviewTint);
            }

            if (_priceLabel != null)
            {
                _priceLabel.transform.SetPositionAndRotation(
                    _previewInstance.transform.position + Vector3.up * PriceLabelHeight, _mainCamera.transform.rotation);
                if (cost != _shownCost || affordable != _shownAffordable)
                {
                    _shownCost = cost;
                    _shownAffordable = affordable;
                    _priceLabel.text = affordable ? $"${cost}" : $"${cost} — can't afford";
                    _priceLabel.color = affordable ? AffordableLabelColor : UnaffordableLabelColor;
                }
            }
        }

        /// <summary>
        /// True if a building may sit at <paramref name="cell"/> with its output facing
        /// <paramref name="outputDirection"/>: the cell is free and has floor (#46), it isn't
        /// another Receiving dock's output cell (that would block the dock's belt), and no existing
        /// belt already flows into its own output cell — such a belt would become a backflow route
        /// into Receiving (#51 arch condition 4). Affordability is checked separately.
        /// </summary>
        private bool IsCellPlaceable(Vector3Int cell, Vector3Int outputDirection)
        {
            if (_gridManager.IsOccupied(cell) || _receivingManager.IsOutputCell(cell))
            {
                return false;
            }

            if (_weightManager != null && !_weightManager.HasFloorAt(cell))
            {
                return false;
            }

            return _conveyorManager == null || !_conveyorManager.HasFeederInto(cell + outputDirection);
        }

        /// <summary>
        /// Buys and places a building at the ghost's cell if it's valid and affordable — money is
        /// spent only here, on a confirmed placement. Registers the cell with GridManager, the dock
        /// with ReceivingManager, recomputes conveyor topology (its output is now main line and
        /// backflow-protected), and raises the placement event (which plays the confirm cue).
        /// Otherwise plays the reject cue.
        /// </summary>
        private void TryConfirmPlacement()
        {
            if (_previewInstance == null)
            {
                return;
            }

            Vector3Int cell = _gridConfig.WorldToCell3D(_previewInstance.transform.position);
            Quaternion rotation = _previewInstance.transform.rotation;
            int cost = NextCost;
            if (!IsCellPlaceable(cell, OutputDirection(rotation)) || _scoreManager == null || !_scoreManager.TrySpend(cost))
            {
                _soundManager?.PlayPlacementReject();
                return;
            }

            GameObject instance = Instantiate(_data.prefab, _previewInstance.transform.position, rotation);
            ReceivingDock dock = instance.GetComponent<ReceivingDock>();
            if (dock == null)
            {
                dock = instance.AddComponent<ReceivingDock>();
            }

            dock.MarkPlayerPlaced();
            _gridManager.Register(cell);
            _placedBuildings[cell] = instance;
            _receivingManager.RegisterDock(dock, _data.itemsPerMinute);
            _conveyorManager?.NotifyReceivingOutputsChanged();
            _eventChannel?.RaisePiecePlaced(instance, cell);
        }

        /// <summary>
        /// Right-click on a placed building, outside every placement mode, removes it (no refund),
        /// lowering the next building's price. The hit point must lie inside the building's own
        /// cell (#44's guard), and the fixed starting dock is never removable — it isn't in this
        /// placer's registry. Skips clicks on UI.
        /// </summary>
        private void HandleRemoveInput()
        {
            if (!Mouse.current.rightButton.wasPressedThisFrame || _mainCamera == null || _placedBuildings.Count == 0)
            {
                return;
            }

            if (_gridManager.IsAnyPlacementModeActive)
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
            if (dock == null || !dock.IsPlayerPlaced)
            {
                return;
            }

            Vector3Int cell = _gridConfig.WorldToCell3D(dock.transform.position);
            if (_placedBuildings.TryGetValue(cell, out GameObject building) && building == dock.gameObject && IsHitInsideCell(hit.point, cell))
            {
                RemoveBuildingAt(cell);
            }
        }

        /// <summary>True if <paramref name="hitPoint"/> lies within <paramref name="cell"/>'s X/Z footprint, with a few cm of tolerance (#44).</summary>
        private bool IsHitInsideCell(Vector3 hitPoint, Vector3Int cell)
        {
            const float Tolerance = 0.05f;
            Vector3 center = _gridConfig.Cell3DToWorld(cell);
            float halfExtent = _gridConfig.cellSize * 0.5f + Tolerance;
            return Mathf.Abs(hitPoint.x - center.x) <= halfExtent && Mathf.Abs(hitPoint.z - center.z) <= halfExtent;
        }

        /// <summary>The cardinal output direction for a building at <paramref name="rotation"/> — the same rounding ReceivingDock.GetOutputDirection applies to the placed instance.</summary>
        private static Vector3Int OutputDirection(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            return new Vector3Int(Mathf.RoundToInt(forward.x), 0, Mathf.RoundToInt(forward.z));
        }

        /// <summary>Spawns the ghost (colliders off, tinted, its ReceivingDock stripped so nothing can mistake it for a dock) and its world-space price label.</summary>
        private void BuildPreview()
        {
            DestroyPreview();
            _previewInstance = Instantiate(_data.prefab);
            ReceivingDock previewDock = _previewInstance.GetComponent<ReceivingDock>();
            if (previewDock != null)
            {
                Destroy(previewDock);
            }

            foreach (Collider pieceCollider in _previewInstance.GetComponentsInChildren<Collider>())
            {
                pieceCollider.enabled = false;
            }

            _isPreviewValid = true;
            SetPreviewTint(ValidPreviewTint);

            GameObject labelObject = new GameObject("ReceivingBuildingPriceLabel");
            _priceLabel = labelObject.AddComponent<TextMeshPro>();
            _priceLabel.alignment = TextAlignmentOptions.Center;
            _priceLabel.enableWordWrapping = false;
            _priceLabel.fontSize = 12f;
            _priceLabel.rectTransform.sizeDelta = new Vector2(20f, 3f);
            _shownCost = -1;
        }

        /// <summary>Destroys the ghost and its price label, if they exist.</summary>
        private void DestroyPreview()
        {
            if (_previewInstance != null)
            {
                Destroy(_previewInstance);
                _previewInstance = null;
            }

            if (_priceLabel != null)
            {
                Destroy(_priceLabel.gameObject);
                _priceLabel = null;
            }
        }

        /// <summary>Tints every renderer on the ghost via Renderer.material, so only the ghost's own material instances change, never the prefab's.</summary>
        private void SetPreviewTint(Color tint)
        {
            foreach (Renderer pieceRenderer in _previewInstance.GetComponentsInChildren<Renderer>())
            {
                pieceRenderer.material.color = tint;
            }
        }
    }
}
