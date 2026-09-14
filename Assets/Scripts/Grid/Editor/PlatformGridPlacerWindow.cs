using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StorageLord.Grid.Editor
{
    /// <summary>
    /// Editor tool for hand-placing SpacePlatformKit prefabs onto the shared grid while building
    /// out a level's platform geometry. This is a level-authoring tool used in the Editor — it is
    /// not a runtime/player-facing system (that's the future PlacementManager for containers and
    /// conveyors, which will reuse the same GridConfig math this tool uses).
    /// </summary>
    public class PlatformGridPlacerWindow : EditorWindow
    {
        private GridConfig _gridConfig;
        private GameObject _prefabToPlace;
        private Transform _platformRoot;

        private bool _isPlacing;
        private float _currentYRotation;
        private GameObject _previewInstance;

        private readonly HashSet<Vector2Int> _occupiedCells = new HashSet<Vector2Int>();

        [MenuItem("Window/Storage Lord/Platform Grid Placer")]
        private static void Open()
        {
            GetWindow<PlatformGridPlacerWindow>("Platform Grid Placer");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            StopPlacing();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Platform Grid Placer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Places a prefab snapped to the shared grid, in the Scene view.\n" +
                "Left-click: place   •   R: rotate 90°   •   Esc: stop placing\n\n" +
                "Occupancy here is one cell per placed piece (the piece's origin), not real " +
                "footprint checking — larger pieces can still visually overlap a neighbor. " +
                "Footprint-aware occupancy is a follow-up pass once per-piece footprints are known.",
                MessageType.Info);

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            _gridConfig = (GridConfig)EditorGUILayout.ObjectField(
                "Grid Config", _gridConfig, typeof(GridConfig), false);
            _prefabToPlace = (GameObject)EditorGUILayout.ObjectField(
                "Prefab To Place", _prefabToPlace, typeof(GameObject), false);
            _platformRoot = (Transform)EditorGUILayout.ObjectField(
                "Platform Root (optional)", _platformRoot, typeof(Transform), true);
            bool changed = EditorGUI.EndChangeCheck();

            if (changed && _isPlacing)
            {
                RebuildPreview();
                RebuildOccupiedCellsFromRoot();
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_gridConfig == null || _prefabToPlace == null))
            {
                if (!_isPlacing)
                {
                    if (GUILayout.Button("Start Placing", GUILayout.Height(28)))
                    {
                        StartPlacing();
                    }
                }
                else
                {
                    if (GUILayout.Button("Stop Placing", GUILayout.Height(28)))
                    {
                        StopPlacing();
                    }
                }
            }

            if (_isPlacing)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Current rotation:", _currentYRotation + "°");
                EditorGUILayout.LabelField("Cells occupied under root:", _occupiedCells.Count.ToString());
            }
        }

        /// <summary>
        /// Enters placement mode: ensures a platform root exists, seeds occupied cells from
        /// whatever is already parented under it, and spawns the ghost preview.
        /// </summary>
        private void StartPlacing()
        {
            if (_gridConfig == null || _prefabToPlace == null)
            {
                return;
            }

            EnsurePlatformRoot();
            RebuildOccupiedCellsFromRoot();
            _isPlacing = true;
            _currentYRotation = 0f;
            RebuildPreview();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Exits placement mode and cleans up the ghost preview instance.
        /// </summary>
        private void StopPlacing()
        {
            _isPlacing = false;
            if (_previewInstance != null)
            {
                DestroyImmediate(_previewInstance);
                _previewInstance = null;
            }
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Finds or creates the "Platform" GameObject that placed pieces are parented under,
        /// if the user hasn't assigned a root themselves.
        /// </summary>
        private void EnsurePlatformRoot()
        {
            if (_platformRoot != null)
            {
                return;
            }

            GameObject existing = GameObject.Find("Platform");
            if (existing == null)
            {
                existing = new GameObject("Platform");
                Undo.RegisterCreatedObjectUndo(existing, "Create Platform Root");
            }
            _platformRoot = existing.transform;
        }

        /// <summary>
        /// Reconstructs the occupied-cell set from whatever pieces are already parented under
        /// the platform root, so occupancy is correct even across separate placing sessions.
        /// </summary>
        private void RebuildOccupiedCellsFromRoot()
        {
            _occupiedCells.Clear();
            if (_platformRoot == null || _gridConfig == null)
            {
                return;
            }

            foreach (Transform child in _platformRoot)
            {
                _occupiedCells.Add(_gridConfig.WorldToCell(child.position));
            }
        }

        /// <summary>
        /// Destroys and recreates the ghost preview instance from the currently selected prefab.
        /// </summary>
        private void RebuildPreview()
        {
            if (_previewInstance != null)
            {
                DestroyImmediate(_previewInstance);
                _previewInstance = null;
            }

            if (_prefabToPlace == null)
            {
                return;
            }

            _previewInstance = (GameObject)PrefabUtility.InstantiatePrefab(_prefabToPlace);
            _previewInstance.hideFlags = HideFlags.HideAndDontSave;
            SetPreviewCollidersEnabled(false);
        }

        /// <summary>
        /// Enables or disables all colliders on the preview instance so it never blocks picking
        /// or physics while it's just following the cursor.
        /// </summary>
        private void SetPreviewCollidersEnabled(bool isEnabled)
        {
            if (_previewInstance == null)
            {
                return;
            }

            foreach (Collider pieceCollider in _previewInstance.GetComponentsInChildren<Collider>())
            {
                pieceCollider.enabled = isEnabled;
            }
        }

        /// <summary>
        /// Drives the placement interaction each Scene view frame: raycasts the cursor against
        /// the Y=0 ground plane, snaps to the grid, moves the preview, and handles place/rotate/
        /// cancel input while placement mode is active.
        /// </summary>
        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_isPlacing || _gridConfig == null || _prefabToPlace == null)
            {
                return;
            }

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

            if (groundPlane.Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                Vector3 snappedPosition = _gridConfig.SnapPosition(hitPoint);
                Vector2Int cell = _gridConfig.WorldToCell(snappedPosition);
                bool occupied = _occupiedCells.Contains(cell);

                if (_previewInstance != null)
                {
                    _previewInstance.transform.SetPositionAndRotation(
                        snappedPosition, Quaternion.Euler(0f, _currentYRotation, 0f));
                }

                Handles.color = occupied ? Color.red : Color.green;
                Handles.DrawWireCube(
                    snappedPosition, new Vector3(_gridConfig.cellSize, 0.05f, _gridConfig.cellSize));

                Handles.BeginGUI();
                Vector2 guiPoint = HandleUtility.WorldToGUIPoint(snappedPosition);
                GUI.color = occupied ? Color.red : Color.green;
                GUI.Label(
                    new Rect(guiPoint.x + 12, guiPoint.y, 220, 20),
                    occupied ? $"Cell {cell.x}, {cell.y} — occupied" : $"Cell {cell.x}, {cell.y}");
                GUI.color = Color.white;
                Handles.EndGUI();

                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.R)
                {
                    _currentYRotation = _gridConfig.SnapYRotation(_currentYRotation + _gridConfig.rotationSnapDegrees);
                    e.Use();
                    Repaint();
                }
                else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
                {
                    StopPlacing();
                    Repaint();
                    e.Use();
                }
                else if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && !occupied)
                {
                    PlacePiece(snappedPosition, _currentYRotation, cell);
                    e.Use();
                    Repaint();
                }
            }

            HandleUtility.AddDefaultControl(controlId);
            sceneView.Repaint();
        }

        /// <summary>
        /// Instantiates the selected prefab as a real prefab instance (keeping its prefab link)
        /// at the given snapped position/rotation, parents it under the platform root, and marks
        /// its cell occupied.
        /// </summary>
        private void PlacePiece(Vector3 position, float yRotation, Vector2Int cell)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(_prefabToPlace, _platformRoot);
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yRotation, 0f));
            Undo.RegisterCreatedObjectUndo(instance, "Place Platform Piece");
            _occupiedCells.Add(cell);
        }
    }
}
