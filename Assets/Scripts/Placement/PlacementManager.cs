using System.Collections.Generic;
using StorageLord.Grid;
using StorageLord.Storage;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StorageLord.Placement
{
    /// <summary>
    /// Runtime placement system: lets the player place a ContainerData piece on the shared 3D
    /// grid. A piece always requires solid support directly beneath it — it snaps onto the deck
    /// (height level 0) or directly atop whatever's already placed in that X/Z column, so stacking
    /// happens automatically rather than via a manually chosen height. This is the player-facing
    /// counterpart to the Editor-time Platform Grid Placer — both share GridConfig's grid math,
    /// but this one runs in Play Mode / a build, driven by the new Input System.
    ///
    /// Created and wired by Bootstrapper — not placed directly in a scene, since it has no
    /// serialized Inspector fields to wire (its data references are injected via Initialize()).
    /// </summary>
    public class PlacementManager : MonoBehaviour
    {
        private GridConfig _gridConfig;
        private PlacementEventChannel _eventChannel;
        private ContainerData _containerData;

        private bool _isPlacing;
        private float _currentYRotation;
        private GameObject _previewInstance;

        private readonly HashSet<Vector3Int> _occupiedCells = new HashSet<Vector3Int>();
        private readonly Dictionary<Vector3Int, GameObject> _placedPieces = new Dictionary<Vector3Int, GameObject>();

        private static readonly Color ValidPreviewTint = new Color(0.4f, 1f, 0.4f, 0.6f);

        private Camera _mainCamera;

        /// <summary>
        /// Injects this manager's data references. Called once by Bootstrapper immediately after
        /// creation, since this manager is created in code (not from a prefab) and so has no
        /// Inspector to assign references through directly.
        /// </summary>
        public void Initialize(GridConfig gridConfig, PlacementEventChannel eventChannel, ContainerData containerData)
        {
            _gridConfig = gridConfig;
            _eventChannel = eventChannel;
            _containerData = containerData;
        }

        /// <summary>
        /// Caches the main camera once rather than querying Camera.main every Update.
        /// </summary>
        private void Awake()
        {
            _mainCamera = Camera.main;
        }

        /// <summary>
        /// Polls input each frame: Tab toggles placement mode; while active, drives rotation
        /// adjustment, the ghost preview, and confirm/cancel/remove.
        /// </summary>
        private void Update()
        {
            if (Keyboard.current == null || Mouse.current == null)
            {
                return;
            }

            if (Keyboard.current.tabKey.wasPressedThisFrame)
            {
                TogglePlacing();
            }

            if (_isPlacing)
            {
                HandleRotationInput();
                UpdatePreview();
                HandleConfirmCancelInput();
            }
            else
            {
                HandleRemoveInput();
            }
        }

        /// <summary>
        /// Enters or exits placement mode, spawning/destroying the ghost preview as appropriate.
        /// No-ops with a warning if required data references aren't assigned.
        /// </summary>
        private void TogglePlacing()
        {
            if (_gridConfig == null || _containerData == null || _containerData.prefab == null)
            {
                Debug.LogWarning(
                    "PlacementManager: cannot enter placement mode — GridConfig or ContainerData/prefab not assigned.");
                return;
            }

            _isPlacing = !_isPlacing;
            if (_isPlacing)
            {
                _currentYRotation = 0f;
                RebuildPreview();
            }
            else
            {
                DestroyPreview();
            }
        }

        /// <summary>
        /// Rotates the preview by one rotation-snap increment around Y when R is pressed.
        /// </summary>
        private void HandleRotationInput()
        {
            if (Keyboard.current.rKey.wasPressedThisFrame)
            {
                _currentYRotation = _gridConfig.SnapYRotation(_currentYRotation + _gridConfig.rotationSnapDegrees);
            }
        }

        /// <summary>
        /// Raycasts the cursor against the deck plane (Y=0) to find the aimed X/Z column, then
        /// derives the placement height from that column's occupancy — the highest occupied level
        /// plus one, or the deck (level 0) if the column is empty — and moves the ghost preview
        /// there. Always a valid placement: it's derived from occupancy, so it can never land on an
        /// already-occupied cell.
        /// </summary>
        private void UpdatePreview()
        {
            if (_previewInstance == null || _mainCamera == null)
            {
                return;
            }

            Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            Plane deckPlane = new Plane(Vector3.up, Vector3.zero);

            if (!deckPlane.Raycast(ray, out float enter))
            {
                return;
            }

            Vector3 hitPoint = ray.GetPoint(enter);
            Vector2Int column = _gridConfig.WorldToCell(hitPoint);
            Vector3Int cell = new Vector3Int(column.x, NextFreeHeightLevel(column), column.y);

            _previewInstance.transform.SetPositionAndRotation(
                _gridConfig.Cell3DToWorld(cell), Quaternion.Euler(0f, _currentYRotation, 0f));
        }

        /// <summary>
        /// Returns the height level a piece placed in the given X/Z column would land on: one above
        /// the highest occupied level already in that column, or 0 (the deck) if it's empty.
        /// </summary>
        private int NextFreeHeightLevel(Vector2Int column)
        {
            int nextLevel = 0;
            foreach (Vector3Int occupied in _occupiedCells)
            {
                if (occupied.x == column.x && occupied.z == column.y)
                {
                    nextLevel = Mathf.Max(nextLevel, occupied.y + 1);
                }
            }

            return nextLevel;
        }

        /// <summary>
        /// Confirms placement on left-click (if the target cell is free) or exits placement mode
        /// on Escape.
        /// </summary>
        private void HandleConfirmCancelInput()
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                TryConfirmPlacement();
            }
            else if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _isPlacing = false;
                DestroyPreview();
            }
        }

        /// <summary>
        /// Instantiates the container prefab at the preview's current cell, registers it as
        /// occupied, and raises the placement event. The target cell is always free by
        /// construction — UpdatePreview only ever targets the deck or a column's next free level.
        /// </summary>
        private void TryConfirmPlacement()
        {
            if (_previewInstance == null)
            {
                return;
            }

            Vector3Int cell = _gridConfig.WorldToCell3D(_previewInstance.transform.position);
            GameObject instance = Instantiate(
                _containerData.prefab, _previewInstance.transform.position, _previewInstance.transform.rotation);
            _occupiedCells.Add(cell);
            _placedPieces[cell] = instance;
            _eventChannel?.RaisePiecePlaced(instance, cell);
        }

        /// <summary>
        /// Right-click removes the placed piece under the cursor (physics raycast against placed
        /// instances) along with every piece stacked directly above it in the same X/Z column, so a
        /// removal can never leave an unsupported piece behind. Only active outside placement mode.
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
            foreach (KeyValuePair<Vector3Int, GameObject> entry in _placedPieces)
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

            for (Vector3Int cell = cellToRemove.Value; _placedPieces.ContainsKey(cell); cell += Vector3Int.up)
            {
                Destroy(_placedPieces[cell]);
                _placedPieces.Remove(cell);
                _occupiedCells.Remove(cell);
            }
        }

        /// <summary>
        /// Destroys any existing preview and spawns a fresh one from the current ContainerData,
        /// with its colliders disabled so it never blocks placement/removal raycasts.
        /// </summary>
        private void RebuildPreview()
        {
            DestroyPreview();
            if (_containerData == null || _containerData.prefab == null)
            {
                return;
            }

            _previewInstance = Instantiate(_containerData.prefab);
            foreach (Collider pieceCollider in _previewInstance.GetComponentsInChildren<Collider>())
            {
                pieceCollider.enabled = false;
            }

            SetPreviewTint(ValidPreviewTint);
        }

        /// <summary>
        /// Destroys the ghost preview instance, if one exists.
        /// </summary>
        private void DestroyPreview()
        {
            if (_previewInstance != null)
            {
                Destroy(_previewInstance);
                _previewInstance = null;
            }
        }

        /// <summary>
        /// Tints all renderers on the preview instance so it reads clearly as a ghost preview.
        /// Uses Renderer.material (not sharedMaterial) so only the preview's own material
        /// instance is modified, never the source prefab/asset. Set once when the preview is
        /// created — every computed placement is valid by construction, so the tint never changes.
        /// </summary>
        private void SetPreviewTint(Color tint)
        {
            foreach (Renderer pieceRenderer in _previewInstance.GetComponentsInChildren<Renderer>())
            {
                pieceRenderer.material.color = tint;
            }
        }
    }
}
