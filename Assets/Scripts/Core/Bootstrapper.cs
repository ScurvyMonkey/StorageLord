using StorageLord.CameraSystem;
using StorageLord.Conveyors;
using StorageLord.Docks;
using StorageLord.Goods;
using StorageLord.Grid;
using StorageLord.Placement;
using StorageLord.Storage;
using UnityEngine;

namespace StorageLord.Core
{
    /// <summary>
    /// Creates and DontDestroyOnLoad's manager singletons at startup, wiring each one's data
    /// references. This is the only place a manager singleton should come into existence — see
    /// CLAUDE.md's manager hierarchy. Deliberately scoped to only the managers real issues have
    /// needed so far (PlacementManager, CameraManager, GridManager, ConveyorManager,
    /// ReceivingManager); other proposed managers (GameManager, UIManager, etc.) get added here only
    /// once their own issue actually needs them, not preemptively.
    /// </summary>
    public class Bootstrapper : MonoBehaviour
    {
        [Header("Grid")]
        [SerializeField] private GridConfig gridConfig;

        [Header("Placement")]
        [SerializeField] private PlacementEventChannel placementEventChannel;
        [SerializeField] private ContainerData defaultContainerData;

        [Header("Conveyors")]
        [SerializeField] private ConveyorData defaultConveyorData;
        [Tooltip("Goods spawned by ConveyorManager's temporary debug spawn action (Editor-only, G " +
                 "key) — a legacy stand-in from before ReceivingManager existed; kept for quick " +
                 "manual testing, but ReceivingManager is the real goods source now.")]
        [SerializeField] private GoodsData debugGoodsData;

        [Header("Receiving")]
        [SerializeField] private ReceivingData receivingData;

        [Header("Camera")]
        [SerializeField] private CameraConfig cameraConfig;
        [Tooltip("The scene's CameraRig transform — the pan target CameraManager drives. Its child " +
                 "Camera (tagged MainCamera) is the one PlacementManager also relies on; CameraManager " +
                 "repositions it but never destroys/recreates it.")]
        [SerializeField] private Transform cameraRig;

        /// <summary>
        /// Creates the manager singletons, wires the two placers' mutual-exclusion references to
        /// each other, and marks this object to persist across scene loads.
        /// </summary>
        private void Awake()
        {
            GridManager gridManager = CreateGridManager();
            PlacementManager placementManager = CreatePlacementManager(gridManager);
            CreateCameraManager();
            ConveyorManager conveyorManager = CreateConveyorManager(gridManager);

            if (placementManager != null && conveyorManager != null)
            {
                placementManager.SetConveyorManager(conveyorManager);
                conveyorManager.SetPlacementManager(placementManager);
            }

            CreateReceivingManager(gridManager, conveyorManager);

            StorageManager storageManager = CreateStorageManager();
            conveyorManager?.SetStorageManager(storageManager);

            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Creates the GridManager singleton — the shared occupancy registry PlacementManager and
        /// ConveyorManager both validate against, so two independent placers can never confirm into
        /// the same cell — unless one already exists.
        /// </summary>
        private GridManager CreateGridManager()
        {
            GridManager existing = FindFirstObjectByType<GridManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject managerObject = new GameObject("GridManager");
            GridManager manager = managerObject.AddComponent<GridManager>();
            DontDestroyOnLoad(managerObject);
            return manager;
        }

        /// <summary>
        /// Creates the PlacementManager singleton and injects its data references, unless one
        /// already exists (e.g. persisted from a previous scene load).
        /// </summary>
        private PlacementManager CreatePlacementManager(GridManager gridManager)
        {
            PlacementManager existing = FindFirstObjectByType<PlacementManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject managerObject = new GameObject("PlacementManager");
            PlacementManager manager = managerObject.AddComponent<PlacementManager>();
            manager.Initialize(gridConfig, gridManager, placementEventChannel, defaultContainerData);
            DontDestroyOnLoad(managerObject);
            return manager;
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

        /// <summary>
        /// Creates the ConveyorManager singleton and injects its data references, unless one already
        /// exists.
        /// </summary>
        private ConveyorManager CreateConveyorManager(GridManager gridManager)
        {
            ConveyorManager existing = FindFirstObjectByType<ConveyorManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject managerObject = new GameObject("ConveyorManager");
            ConveyorManager manager = managerObject.AddComponent<ConveyorManager>();
            manager.Initialize(gridConfig, gridManager, placementEventChannel, defaultConveyorData, debugGoodsData);
            DontDestroyOnLoad(managerObject);
            return manager;
        }

        /// <summary>
        /// Creates the ReceivingManager singleton and injects its data references, unless one
        /// already exists. Created after ConveyorManager so it can be handed a live reference —
        /// ReceivingManager spawns goods and hands them straight to ConveyorManager's movement
        /// tracking.
        /// </summary>
        private void CreateReceivingManager(GridManager gridManager, ConveyorManager conveyorManager)
        {
            if (FindFirstObjectByType<ReceivingManager>() != null)
            {
                return;
            }

            GameObject managerObject = new GameObject("ReceivingManager");
            ReceivingManager manager = managerObject.AddComponent<ReceivingManager>();
            manager.Initialize(gridConfig, gridManager, conveyorManager, receivingData);
            DontDestroyOnLoad(managerObject);
        }

        /// <summary>
        /// Creates the StorageManager singleton and injects its data references, unless one already
        /// exists. Created after ConveyorManager so its reference can be wired into it afterward —
        /// ConveyorManager queries StorageManager when a GoodsAgent reaches a non-conveyor cell.
        /// </summary>
        private StorageManager CreateStorageManager()
        {
            StorageManager existing = FindFirstObjectByType<StorageManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject managerObject = new GameObject("StorageManager");
            StorageManager manager = managerObject.AddComponent<StorageManager>();
            manager.Initialize(placementEventChannel);
            DontDestroyOnLoad(managerObject);
            return manager;
        }
    }
}
