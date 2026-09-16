using StorageLord.CameraSystem;
using StorageLord.Grid;
using StorageLord.Placement;
using StorageLord.Storage;
using UnityEngine;

namespace StorageLord.Core
{
    /// <summary>
    /// Creates and DontDestroyOnLoad's manager singletons at startup, wiring each one's data
    /// references. This is the only place a manager singleton should come into existence — see
    /// CLAUDE.md's manager hierarchy. Deliberately scoped to only PlacementManager and CameraManager
    /// for now; other proposed managers (GameManager, UIManager, etc.) get added here only once
    /// their own issue actually needs them, not preemptively.
    /// </summary>
    public class Bootstrapper : MonoBehaviour
    {
        [Header("Placement")]
        [SerializeField] private GridConfig gridConfig;
        [SerializeField] private PlacementEventChannel placementEventChannel;
        [SerializeField] private ContainerData defaultContainerData;

        [Header("Camera")]
        [SerializeField] private CameraConfig cameraConfig;
        [Tooltip("The scene's CameraRig transform — the pan target CameraManager drives. Its child " +
                 "Camera (tagged MainCamera) is the one PlacementManager also relies on; CameraManager " +
                 "repositions it but never destroys/recreates it.")]
        [SerializeField] private Transform cameraRig;

        /// <summary>
        /// Creates the manager singletons and marks this object to persist across scene loads.
        /// </summary>
        private void Awake()
        {
            CreatePlacementManager();
            CreateCameraManager();
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Creates the PlacementManager singleton and injects its data references, unless one
        /// already exists (e.g. persisted from a previous scene load).
        /// </summary>
        private void CreatePlacementManager()
        {
            if (FindFirstObjectByType<PlacementManager>() != null)
            {
                return;
            }

            GameObject managerObject = new GameObject("PlacementManager");
            PlacementManager manager = managerObject.AddComponent<PlacementManager>();
            manager.Initialize(gridConfig, placementEventChannel, defaultContainerData);
            DontDestroyOnLoad(managerObject);
        }

        /// <summary>
        /// Creates the CameraManager singleton and injects its data/scene references, unless one
        /// already exists.
        /// </summary>
        private void CreateCameraManager()
        {
            if (FindFirstObjectByType<CameraManager>() != null)
            {
                return;
            }

            if (cameraRig == null)
            {
                Debug.LogWarning("Bootstrapper: cannot create CameraManager — cameraRig not assigned.");
                return;
            }

            GameObject managerObject = new GameObject("CameraManager");
            CameraManager manager = managerObject.AddComponent<CameraManager>();
            manager.Initialize(cameraConfig, cameraRig);
            DontDestroyOnLoad(managerObject);
        }
    }
}
