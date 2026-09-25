using StorageLord.Audio;
using StorageLord.CameraSystem;
using StorageLord.Conveyors;
using StorageLord.Docks;
using StorageLord.Goods;
using StorageLord.Grid;
using StorageLord.Placement;
using StorageLord.Storage;
using StorageLord.UI;
using UnityEngine;

namespace StorageLord.Core
{
    /// <summary>
    /// Creates and DontDestroyOnLoad's manager singletons at startup, wiring each one's data
    /// references. This is the only place a manager singleton should come into existence — see
    /// CLAUDE.md's manager hierarchy. Each manager gets added here only once its own issue actually
    /// needs it, not preemptively. UIManager (#30) is created first, before every other manager —
    /// every HUD self-serves it via FindFirstObjectByType, and HUD creation is interleaved
    /// throughout this method rather than batched, so it must exist before the earliest HUD call.
    /// </summary>
    public class Bootstrapper : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private UIThemeData uiThemeData;

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

        [Header("Shipping")]
        [SerializeField] private ShippingScheduleData shippingScheduleData;
        [SerializeField] private OrderEventChannel orderEventChannel;
        [SerializeField] private WaveEscalationData waveEscalationData;
        [SerializeField] private RandomJobPoolData randomJobPoolData;
        [SerializeField] private SpecialEventData specialEventData;

        [Header("Game Rules")]
        [SerializeField] private GameRulesData gameRulesData;

        [Header("Weight")]
        [SerializeField] private PlatformSegmentData platformSegmentData;

        [Header("Upgrades")]
        [SerializeField] private UpgradeData upgradeData;

        [Header("Audio")]
        [SerializeField] private SoundLibraryData soundLibraryData;
        [SerializeField] private AmbienceData ambienceData;

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
            CreateUIManager();

            GridManager gridManager = CreateGridManager();
            PlacementManager placementManager = CreatePlacementManager(gridManager);
            CreateCameraManager();
            ConveyorManager conveyorManager = CreateConveyorManager(gridManager);

            if (placementManager != null && conveyorManager != null)
            {
                placementManager.SetConveyorManager(conveyorManager);
                conveyorManager.SetPlacementManager(placementManager);
            }

            ReceivingManager receivingManager = CreateReceivingManager(gridManager, conveyorManager);
            conveyorManager?.SetReceivingManager(receivingManager);
            receivingManager?.SetPlacementManager(placementManager);
            CreateReceivingHUD();

            StorageManager storageManager = CreateStorageManager();
            conveyorManager?.SetStorageManager(storageManager);
            placementManager?.SetStorageManager(storageManager);
            storageManager?.SetConveyorManager(conveyorManager);

            WeightManager weightManager = CreateWeightManager(gridManager, storageManager, conveyorManager);
            storageManager?.SetWeightManager(weightManager);
            weightManager?.SetPlacementManager(placementManager);
            CreateWeightHUD();

            ShippingManager shippingManager = CreateShippingManager(gridManager, storageManager);
            conveyorManager?.SetShippingManager(shippingManager);
            shippingManager?.SetConveyorManager(conveyorManager);
            shippingManager?.SetPlacementManager(placementManager);
            shippingManager?.SetWeightManager(weightManager);

            CreateShippingHUD();

            ScoreManager scoreManager = CreateScoreManager();
            CreateScoreHUD();
            CreateJobOfferHUD();

            UpgradeManager upgradeManager = CreateUpgradeManager(scoreManager);
            conveyorManager?.SetUpgradeManager(upgradeManager);
            weightManager?.SetUpgradeManager(upgradeManager);
            CreateUpgradeHUD();

            SoundManager soundManager = CreateSoundManager();
            conveyorManager?.SetSoundManager(soundManager);
            CreateAmbienceManager(soundManager);

            CreateGameManager(placementManager, conveyorManager, receivingManager, shippingManager, weightManager);
            CreateGameOverHUD();

            CreateOrderGuideHUD();
            CreateControlsHUD();

            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Creates the UIManager singleton, unless one already exists — deliberately the very first
        /// call in Awake(), before every other manager: HUD creation is interleaved throughout this
        /// method (e.g. CreateReceivingHUD() fires right after ReceivingManager, long before most
        /// other managers exist), and every HUD self-serves this reference via
        /// FindFirstObjectByType&lt;UIManager&gt;() the same way it already self-serves its own
        /// manager — so UIManager must exist before the first such HUD is created, not merely
        /// "before the HUDs" as a class of calls.
        /// </summary>
        private void CreateUIManager()
        {
            if (FindFirstObjectByType<UIManager>() != null)
            {
                return;
            }

            GameObject managerObject = new GameObject("UIManager");
            UIManager manager = managerObject.AddComponent<UIManager>();
            manager.Initialize(uiThemeData);
            DontDestroyOnLoad(managerObject);
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
        /// tracking. Returns the manager so it can be wired back into ConveyorManager afterward
        /// (#7 — main-line tracing needs ConveyorManager to reference ReceivingManager in turn), and
        /// so SetPlacementManager (#15 — goods-selection click gating) can be wired afterward too.
        /// Also passed shippingScheduleData/waveEscalationData (#15) to derive its player-selectable
        /// goods list — the same Bootstrapper-level asset references ShippingManager itself reads,
        /// available regardless of manager creation order since they're plain serialized fields, not
        /// sourced from another manager's own runtime state.
        /// </summary>
        private ReceivingManager CreateReceivingManager(GridManager gridManager, ConveyorManager conveyorManager)
        {
            ReceivingManager existing = FindFirstObjectByType<ReceivingManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject managerObject = new GameObject("ReceivingManager");
            ReceivingManager manager = managerObject.AddComponent<ReceivingManager>();
            manager.Initialize(gridConfig, gridManager, conveyorManager, receivingData, shippingScheduleData, waveEscalationData);
            DontDestroyOnLoad(managerObject);
            return manager;
        }

        /// <summary>
        /// Creates the ReceivingHUD utility object, unless one already exists. Not a manager
        /// singleton — a passive display with no data to inject beyond finding ReceivingManager
        /// itself — but created here anyway so every runtime object comes from one place rather
        /// than needing a hand-placed scene object.
        /// </summary>
        private void CreateReceivingHUD()
        {
            if (FindFirstObjectByType<ReceivingHUD>() != null)
            {
                return;
            }

            GameObject hudObject = new GameObject("ReceivingHUD");
            hudObject.AddComponent<ReceivingHUD>();
            DontDestroyOnLoad(hudObject);
        }

        /// <summary>
        /// Creates the StorageManager singleton and injects its data references, unless one already
        /// exists. Created after ConveyorManager so a reference can be wired *both* ways afterward —
        /// ConveyorManager queries StorageManager when a GoodsAgent reaches a non-conveyor cell, and
        /// (#22) StorageManager in turn queries ConveyorManager to find a real, open belt cell next
        /// to a container before dispatching a unit onto it, the same reciprocal-wiring shape
        /// PlacementManager/ConveyorManager already established for their own mutual reference.
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

        /// <summary>
        /// Creates the WeightManager singleton and injects its references, unless one already
        /// exists. Created right after StorageManager — it needs both StorageManager and
        /// ConveyorManager (already created earlier) to enumerate/remove their cells during a
        /// platform segment collapse (#14).
        /// </summary>
        private WeightManager CreateWeightManager(GridManager gridManager, StorageManager storageManager, ConveyorManager conveyorManager)
        {
            WeightManager existing = FindFirstObjectByType<WeightManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject managerObject = new GameObject("WeightManager");
            WeightManager manager = managerObject.AddComponent<WeightManager>();
            manager.Initialize(gridConfig, gridManager, storageManager, conveyorManager);
            DontDestroyOnLoad(managerObject);
            return manager;
        }

        /// <summary>
        /// Creates the WeightHUD utility object, unless one already exists. Not a manager
        /// singleton — a passive display with no data to inject beyond finding WeightManager
        /// itself — but created here anyway so every runtime object comes from one place rather
        /// than needing a hand-placed scene object.
        /// </summary>
        private void CreateWeightHUD()
        {
            if (FindFirstObjectByType<WeightHUD>() != null)
            {
                return;
            }

            GameObject hudObject = new GameObject("WeightHUD");
            hudObject.AddComponent<WeightHUD>();
            DontDestroyOnLoad(hudObject);
        }

        /// <summary>
        /// Creates the ShippingManager singleton and injects its data references, unless one
        /// already exists. Created after StorageManager so it can be handed a live reference for
        /// automatic container withdrawal.
        /// </summary>
        private ShippingManager CreateShippingManager(GridManager gridManager, StorageManager storageManager)
        {
            ShippingManager existing = FindFirstObjectByType<ShippingManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject managerObject = new GameObject("ShippingManager");
            ShippingManager manager = managerObject.AddComponent<ShippingManager>();
            manager.Initialize(gridConfig, gridManager, storageManager, shippingScheduleData, orderEventChannel, waveEscalationData, randomJobPoolData, specialEventData);
            DontDestroyOnLoad(managerObject);
            return manager;
        }

        /// <summary>
        /// Creates the ScoreManager singleton and injects its event channel reference, unless one
        /// already exists (#25 — the slot CLAUDE.md's manager hierarchy has reserved since #9). Only
        /// needs the shared OrderEventChannel asset, not a live reference to any other manager, so
        /// its creation order relative to ShippingManager doesn't matter.
        /// </summary>
        private ScoreManager CreateScoreManager()
        {
            ScoreManager existing = FindFirstObjectByType<ScoreManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject managerObject = new GameObject("ScoreManager");
            ScoreManager manager = managerObject.AddComponent<ScoreManager>();
            manager.Initialize(orderEventChannel);
            DontDestroyOnLoad(managerObject);
            return manager;
        }

        /// <summary>
        /// Creates the ScoreHUD utility object, unless one already exists (#25). Not a manager
        /// singleton — a passive display with nothing to inject beyond finding ScoreManager itself —
        /// but created here anyway so every runtime object comes from one place rather than needing
        /// a hand-placed scene object.
        /// </summary>
        private void CreateScoreHUD()
        {
            if (FindFirstObjectByType<ScoreHUD>() != null)
            {
                return;
            }

            GameObject hudObject = new GameObject("ScoreHUD");
            hudObject.AddComponent<ScoreHUD>();
            DontDestroyOnLoad(hudObject);
        }

        /// <summary>
        /// Creates the JobOfferHUD utility object, unless one already exists (#25). Not a manager
        /// singleton — a passive display with nothing to inject beyond finding ShippingManager
        /// itself — but created here anyway so every runtime object comes from one place rather
        /// than needing a hand-placed scene object.
        /// </summary>
        private void CreateJobOfferHUD()
        {
            if (FindFirstObjectByType<JobOfferHUD>() != null)
            {
                return;
            }

            GameObject hudObject = new GameObject("JobOfferHUD");
            hudObject.AddComponent<JobOfferHUD>();
            DontDestroyOnLoad(hudObject);
        }

        /// <summary>
        /// Creates the UpgradeManager singleton and injects its data/manager references, unless one
        /// already exists (#27). Created after ScoreManager, since purchasing an upgrade tier needs a
        /// live TrySpend reference — creation order relative to ConveyorManager/WeightManager doesn't
        /// matter, since both are wired a reference to this manager afterward via
        /// SetUpgradeManager(), the same late-wiring shape every other cross-manager reference in
        /// this file already uses.
        /// </summary>
        private UpgradeManager CreateUpgradeManager(ScoreManager scoreManager)
        {
            UpgradeManager existing = FindFirstObjectByType<UpgradeManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject managerObject = new GameObject("UpgradeManager");
            UpgradeManager manager = managerObject.AddComponent<UpgradeManager>();
            manager.Initialize(upgradeData, scoreManager);
            DontDestroyOnLoad(managerObject);
            return manager;
        }

        /// <summary>
        /// Creates the UpgradeHUD utility object, unless one already exists (#27). Not a manager
        /// singleton — a passive display with nothing to inject beyond finding UpgradeManager
        /// itself — but created here anyway so every runtime object comes from one place rather
        /// than needing a hand-placed scene object.
        /// </summary>
        private void CreateUpgradeHUD()
        {
            if (FindFirstObjectByType<UpgradeHUD>() != null)
            {
                return;
            }

            GameObject hudObject = new GameObject("UpgradeHUD");
            hudObject.AddComponent<UpgradeHUD>();
            DontDestroyOnLoad(hudObject);
        }

        /// <summary>
        /// Creates the SoundManager singleton and injects its data/event-channel references, unless
        /// one already exists (#29). Only needs Bootstrapper-level asset references (SoundLibraryData,
        /// placementEventChannel, orderEventChannel), so its creation order relative to other
        /// managers doesn't matter — same reasoning as ScoreManager's own creation-order note.
        /// </summary>
        private SoundManager CreateSoundManager()
        {
            SoundManager existing = FindFirstObjectByType<SoundManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject managerObject = new GameObject("SoundManager");
            SoundManager manager = managerObject.AddComponent<SoundManager>();
            manager.Initialize(soundLibraryData, placementEventChannel, orderEventChannel);
            DontDestroyOnLoad(managerObject);
            return manager;
        }

        /// <summary>
        /// Creates the AmbienceManager singleton and injects its data/manager references, unless one
        /// already exists (#29). Created after SoundManager, since it needs a live reference to
        /// crossfade ambience tracks through it.
        /// </summary>
        private AmbienceManager CreateAmbienceManager(SoundManager soundManager)
        {
            AmbienceManager existing = FindFirstObjectByType<AmbienceManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject managerObject = new GameObject("AmbienceManager");
            AmbienceManager manager = managerObject.AddComponent<AmbienceManager>();
            manager.Initialize(ambienceData, soundManager);
            DontDestroyOnLoad(managerObject);
            return manager;
        }

        /// <summary>
        /// Creates the ShippingHUD utility object, unless one already exists. Not a manager
        /// singleton, but created here anyway so every runtime object comes from one place rather
        /// than needing a hand-placed scene object. Passed orderEventChannel (#28) so it can prune
        /// its own per-order label cache when an order is fulfilled or missed.
        /// </summary>
        private void CreateShippingHUD()
        {
            if (FindFirstObjectByType<ShippingHUD>() != null)
            {
                return;
            }

            GameObject hudObject = new GameObject("ShippingHUD");
            ShippingHUD hud = hudObject.AddComponent<ShippingHUD>();
            hud.Initialize(orderEventChannel);
            DontDestroyOnLoad(hudObject);
        }

        /// <summary>
        /// Creates the GameManager singleton and injects its references, unless one already exists.
        /// Created last among the managers in Awake() — it needs PlacementManager, ConveyorManager,
        /// ReceivingManager, and ShippingManager to already exist so it can halt all four when the
        /// run ends (#12), plus a live OrderEventChannel reference.
        /// </summary>
        private GameManager CreateGameManager(
            PlacementManager placementManager,
            ConveyorManager conveyorManager,
            ReceivingManager receivingManager,
            ShippingManager shippingManager,
            WeightManager weightManager)
        {
            GameManager existing = FindFirstObjectByType<GameManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject managerObject = new GameObject("GameManager");
            GameManager manager = managerObject.AddComponent<GameManager>();
            manager.Initialize(orderEventChannel, gameRulesData, placementManager, conveyorManager, receivingManager, shippingManager, weightManager);
            DontDestroyOnLoad(managerObject);
            return manager;
        }

        /// <summary>
        /// Creates the GameOverHUD utility object, unless one already exists. Not a manager
        /// singleton — a passive display with no data to inject beyond finding GameManager itself —
        /// but created here anyway so every runtime object comes from one place rather than needing
        /// a hand-placed scene object.
        /// </summary>
        private void CreateGameOverHUD()
        {
            if (FindFirstObjectByType<GameOverHUD>() != null)
            {
                return;
            }

            GameObject hudObject = new GameObject("GameOverHUD");
            hudObject.AddComponent<GameOverHUD>();
            DontDestroyOnLoad(hudObject);
        }

        /// <summary>
        /// Creates the OrderGuideHUD utility object and injects its data references, unless one
        /// already exists (#16). Not a manager singleton — a passive, player-toggled reference
        /// display with nothing to find via FindFirstObjectByType, since it reads
        /// shippingScheduleData/waveEscalationData directly — but created here anyway so every
        /// runtime object comes from one place rather than needing a hand-placed scene object.
        /// </summary>
        private void CreateOrderGuideHUD()
        {
            if (FindFirstObjectByType<OrderGuideHUD>() != null)
            {
                return;
            }

            GameObject hudObject = new GameObject("OrderGuideHUD");
            OrderGuideHUD hud = hudObject.AddComponent<OrderGuideHUD>();
            hud.Initialize(shippingScheduleData, waveEscalationData);
            DontDestroyOnLoad(hudObject);
        }

        /// <summary>
        /// Creates the ControlsHUD utility object, unless one already exists (#19). Not a manager
        /// singleton — a passive display with nothing to inject beyond finding
        /// PlacementManager/ConveyorManager themselves — but created here anyway so every runtime
        /// object comes from one place rather than needing a hand-placed scene object.
        /// </summary>
        private void CreateControlsHUD()
        {
            if (FindFirstObjectByType<ControlsHUD>() != null)
            {
                return;
            }

            GameObject hudObject = new GameObject("ControlsHUD");
            hudObject.AddComponent<ControlsHUD>();
            DontDestroyOnLoad(hudObject);
        }
    }
}
