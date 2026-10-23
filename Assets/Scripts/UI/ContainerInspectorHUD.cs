using StorageLord.Conveyors;
using StorageLord.Placement;
using StorageLord.Storage;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace StorageLord.UI
{
    /// <summary>
    /// Read-only popup showing a clicked container's locked goods type, current count, and capacity
    /// (#36) — the project's first genuinely interactive UI element to react to a world-space click
    /// rather than just display passive state. Left-clicking a placed container, outside both
    /// placement modes (currently unclaimed input — containers had no outside-both-modes click
    /// handler before this issue), opens the popup at UIManager.CenterPopupAnchor, reusing
    /// GameOverHUD's exact instantiate-and-strip-then-add-fresh-labels pattern against
    /// UIThemeData.popupPrefab. Clicking elsewhere (in world space) closes it; right-clicking the
    /// same container still removes it via PlacementManager's existing, unchanged behavior — this
    /// HUD detects that via a per-frame fake-null check on the tracked reference (the same pattern
    /// #7's junction-claim logic and #36's own arch review both established for "was this destroyed
    /// by some other path"), not by intercepting the right-click itself. Read-only by design: no
    /// StorageManager/ContainerInstance state is ever written here.
    /// </summary>
    public class ContainerInspectorHUD : MonoBehaviour
    {
        private PlacementManager _placementManager;
        private ConveyorManager _conveyorManager;
        private UIManager _uiManager;
        private StorageManager _storageManager;
        private Camera _mainCamera;

        private GameObject _popupInstance;
        private RectTransform _popupContentRoot;
        private TextMeshProUGUI _popupLabel;
        private ContainerInstance _inspectedContainer;
        private Vector3Int? _inspectedCell;

        private DisplayCacheKey _lastShown;

        private struct DisplayCacheKey
        {
            public string GoodsName;
            public int Count;
            public int Capacity;
            public int ColumnStored;
            public int ColumnCapacity;
        }

        /// <summary>
        /// Caches manager/camera references — the same self-service pattern every other HUD/manager
        /// already uses.
        /// </summary>
        private void Awake()
        {
            _placementManager = FindFirstObjectByType<PlacementManager>();
            _conveyorManager = FindFirstObjectByType<ConveyorManager>();
            _uiManager = FindFirstObjectByType<UIManager>();
            _storageManager = FindFirstObjectByType<StorageManager>();
            _mainCamera = Camera.main;
        }

        /// <summary>
        /// Closes the popup if its tracked container has been destroyed by some other path (right-
        /// click removal, cascade removal, a platform weight-overload collapse) since the last
        /// frame, then handles this frame's click input.
        /// </summary>
        private void Update()
        {
            if (_popupInstance != null && _inspectedContainer == null)
            {
                ClosePopup();
            }

            if (_popupInstance != null)
            {
                RefreshPopupText();
            }

            HandleClickInput();
        }

        /// <summary>
        /// Left-click opens the popup for whichever container it hits (replacing any container
        /// already shown), or closes an open popup if it hits empty space/a non-container object —
        /// outside both placement modes, where left-click already means something else. Skips
        /// entirely when the click lands on a UI element (#36's own arch condition) — a click aimed
        /// at the popup itself or the order dropdown must never also fire a stray world raycast that
        /// could open/close the wrong container.
        /// </summary>
        private void HandleClickInput()
        {
            if (_mainCamera == null || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if ((_placementManager != null && _placementManager.IsPlacementModeActive)
                || (_conveyorManager != null && _conveyorManager.IsPlacementModeActive))
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            ContainerInstance hitContainer = null;
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                hitContainer = hit.collider.GetComponentInParent<ContainerInstance>();
            }

            if (hitContainer == null)
            {
                if (_popupInstance != null)
                {
                    ClosePopup();
                }

                return;
            }

            if (hitContainer == _inspectedContainer)
            {
                return;
            }

            OpenPopup(hitContainer);
        }

        /// <summary>
        /// Instantiates the popup prefab (stripped of the pack's own demo content) at the standalone
        /// center anchor, adds a fresh text label into it, and shows the given container's state.
        /// Closes any previously-open popup first — only one container can be inspected at a time.
        /// </summary>
        private void OpenPopup(ContainerInstance container)
        {
            ClosePopup();

            if (_uiManager == null || _uiManager.CenterPopupAnchor == null)
            {
                return;
            }

            _inspectedContainer = container;
            _inspectedCell = _storageManager != null && _storageManager.TryGetCell(container, out Vector3Int cell) ? cell : (Vector3Int?)null;
            HudRegion panel = _uiManager.CreatePopupPanel("ContainerInspectorPopup");
            _popupInstance = panel.gameObject;
            _popupContentRoot = panel.ContentRoot;

            _popupLabel = HudTextFactory.CreateLabel(_popupContentRoot, string.Empty);
            _popupLabel.alignment = TextAlignmentOptions.Center;
            _lastShown = default;
            RefreshPopupText();
        }

        /// <summary>
        /// Rebuilds the popup's text only if the displayed values have actually changed since the
        /// last call — goods keep arriving/leaving in real time while the popup is open.
        /// </summary>
        private void RefreshPopupText()
        {
            if (_popupLabel == null || _inspectedContainer == null)
            {
                return;
            }

            string goodsName = _inspectedContainer.LockedType != null ? _inspectedContainer.LockedType.displayName : "Empty — accepts any type";
            (int columnStored, int columnCapacity) = _inspectedCell.HasValue && _storageManager != null
                ? _storageManager.GetColumnTotals(_inspectedCell.Value)
                : (0, 0);
            DisplayCacheKey current = new DisplayCacheKey
            {
                GoodsName = goodsName,
                Count = _inspectedContainer.CurrentCount,
                Capacity = _inspectedContainer.Capacity,
                ColumnStored = columnStored,
                ColumnCapacity = columnCapacity
            };

            if (current.GoodsName == _lastShown.GoodsName && current.Count == _lastShown.Count && current.Capacity == _lastShown.Capacity
                && current.ColumnStored == _lastShown.ColumnStored && current.ColumnCapacity == _lastShown.ColumnCapacity)
            {
                return;
            }

            _lastShown = current;
            string text = $"{goodsName}\n{current.Count}/{current.Capacity}";
            if (current.ColumnCapacity > current.Capacity)
            {
                text += $"\nColumn: {current.ColumnStored}/{current.ColumnCapacity}";
            }

            _popupLabel.text = text;

            // The label's line count can change (e.g. the #47 Column line appears or not), and a
            // nested ContentSizeFitter doesn't reliably propagate that to the panel on its own (#33).
            LayoutRebuilder.ForceRebuildLayoutImmediate(_popupContentRoot);
        }

        /// <summary>Destroys the popup instance (if any) and clears all tracked state.</summary>
        private void ClosePopup()
        {
            if (_popupInstance != null)
            {
                Destroy(_popupInstance);
            }

            _popupInstance = null;
            _popupContentRoot = null;
            _inspectedCell = null;
            _popupLabel = null;
            _inspectedContainer = null;
        }
    }
}
