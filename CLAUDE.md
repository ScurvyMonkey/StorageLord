# CLAUDE.md — Storage Lord

## Project Overview

**Storage Lord** is a Unity 6 satisfying, grid-based space logistics micro-game. The player manages one storage platform for an indifferent Company: goods arrive at Receiving, get routed via conveyors into grid-anchored containers, and get routed back out to Shipping to fulfill the Company's orders before their deadlines. Full design context lives in `docs/GDD.md` — **read it before scoping any new system**, especially its Open Questions section, since several core mechanics (grid cell size, container type-matching, conveyor complexity) aren't nailed down yet (camera style was resolved by #2 — free-orbit perspective).

Built with C# and Universal Render Pipeline (URP), 3D. Uses the **SpacePlatformKit** (modular station geometry) and **ScifiCommoditiesTradeGoodsLootCollection** (goods/cargo meshes) asset packs — both are static meshes/prefabs only; there is no gameplay logic to inherit from them.

**Repo:** `ScurvyMonkey/StorageLord` (GitHub — hosts both code and the Issues backlog)

---

## Current Phase: Phase 1 — Core Loop

Nothing is implemented yet — this is a from-scratch start. Phase 1 is the MVP: a working Receive → Store → Ship loop on a single platform, with clean grid placement as a first-class requirement (not a later polish pass). Phase 2 (goods categories/type-matching, conveyor junctions/splitters, order complexity, audio/VFX polish) is explicitly out of scope until Phase 1's loop is playable end-to-end. See [Roadmap](docs/GDD.md#roadmap) in the GDD for the full phase breakdown.

**Phase 1 sub-system sequencing** (update this list as each sub-system is picked up/completed):
- **Grid & placement system** — first pass landed: `GridConfig` (cell size + rotation snap, `Assets/Data/GridConfig.asset`), `GridVisualizer` (Scene view grid gizmo), and the `Platform Grid Placer` Editor window (see Platform Assembly below) all exist. **Not yet done:** `cellSize` (4m) is an unverified placeholder — check it against SpacePlatformKit piece footprints via the grid gizmo and adjust; occupancy tracking is one-cell-per-piece-origin, not real per-piece footprint checking, so larger pieces can still visually overlap a neighbor. No runtime `GridManager` exists yet — that's still needed once containers/conveyors (player-placed, at runtime) start development; it should reuse `GridConfig`'s math rather than duplicating it.
- **Platform assembly from SpacePlatformKit pieces** — first pass landed, resolved as an **Editor-time, hand-authored** workflow (not player-placed at runtime): `Assets/Scripts/Grid/Editor/PlatformGridPlacerWindow.cs` (**Window → Storage Lord → Platform Grid Placer**) lets you pick a prefab and click-place it in the Scene view, snapped to the grid, parented under a `Platform` root. This is a level-design tool for you, not a system players interact with — kept clearly separate from the future runtime `PlacementManager` (containers/conveyors), though both will share `GridConfig`.
- Conveyor movement — not yet started (depends on grid system)
- **Containers/storage (placement)** — first pass landed (#1), placement rules reworked to support-required auto-stacking (#3): `PlacementManager` (runtime, `Assets/Scripts/Placement/`) is the game's first real manager, created by the new `Bootstrapper` (`Assets/Scripts/Core/`). Players toggle placement mode (Tab), aim with the mouse to snap a ghost preview to the grid — height is no longer a manual input; the piece always snaps onto the deck or directly atop whatever's already placed in that X/Z column, so a second piece placed at the same column auto-stacks one level higher — rotate with R, confirm with left-click, remove with right-click (removing a piece also removes everything stacked directly above it in that column). Uses `ContainerData` (minimal — display name + prefab only; capacity/type-filtering is still Phase 2) and a placeholder container visual (`ContainerData_CargoBoxPlaceholder.asset`, since no real container art exists yet). **Not yet done:** real container art/design, actual goods storage/capacity (that's `StorageManager`, a separate not-yet-started piece), any stack-height cap (unlimited for now), and verifying a deck-level (height 0) placement actually lands on real platform geometry rather than open space past its edge (`PlacementManager` has no concept of platform footprint/bounds yet).
- **Camera** — first pass landed (#2): `CameraManager` (runtime, `Assets/Scripts/Camera/`) drives the scene's pre-placed `MainCamera`-tagged `Camera` as a free-orbit rig (Shapez 2-style, resolving the GDD's camera Open Question) — hold middle-mouse and drag to orbit/tilt, WASD/arrows to pan, scroll to zoom. Uses `CameraConfig` (`Assets/Data/CameraConfig.asset`) for all tuning. Also fixed a pre-existing gap while wiring this in: `Bootstrapper` had never actually been added to `SampleScene` — nothing in the scene created it, so `PlacementManager` never ran either. Added a `Bootstrapper` GameObject to the scene with all its references wired (`GridConfig`, `PlacementEventChannel`, `ContainerData_CargoBoxPlaceholder`, `CameraConfig`, the new `CameraRig`).
- Receiving dock + goods spawn scheduling — not yet started
- Shipping dock + Company orders/deadlines — not yet started (depends on containers, for querying fulfillment)
- Basic scoring (orders fulfilled/missed) — not yet started

If a spec or a piece of work seems to require a later-phase system (goods categories, junctions/splitters, multi-platform, meta-progression), stop and flag it rather than building a placeholder for it.

---

## Roadmap

| Phase | Adds |
|---|---|
| **1 — Core Loop** (current) | Grid + placement, platform geometry, conveyor movement, containers, Receiving dock w/ scheduled spawns, Shipping dock + Company orders w/ deadlines, basic fulfilled/missed scoring, clean anchored placement UX |
| **2 — Depth & Polish** | Goods categories/type-matching, conveyor junctions/splitters, order complexity (multi-good, priority/rush), efficiency scoring, audio/VFX feedback, UI polish |

Multiple platforms, hazards/events, meta-progression, and narrative beats are **not scoped** — backlog material for after Phase 2, not assumptions to design around now.

---

## Proposed Manager Hierarchy (Phase 1)

`Bootstrapper`, `PlacementManager`, and `CameraManager` are real (#1, #2) — everything else below is still a **starting proposal**, not settled fact. Update each entry the moment real implementation diverges from it.

All managers are singletons created by `Bootstrapper.cs` via `DontDestroyOnLoad`. **Do not create singletons outside this pattern.** `Bootstrapper` itself is deliberately minimal — it only creates what its own issues needed (`PlacementManager`, `CameraManager`); each other manager below gets added to it only once a real issue builds it, not preemptively.

```
Bootstrapper (exists)
├── GameManager        — Game state machine (MainMenu, Playing, Paused, RunSummary) — proposed
├── GridManager         — Grid definition, cell occupancy, world↔cell coordinate conversion — proposed (GridConfig's static math currently covers this; a runtime GridManager may turn out unnecessary — revisit once ConveyorManager needs shared occupancy state across systems)
├── PlacementManager     — (exists) Ghost preview (support-required auto-stack height, derived from column occupancy)/rotation (R) adjustment, confirm (left-click)/remove (right-click, cascades up the stack), 3D occupancy registry. Created via code by Bootstrapper (not a prefab) — its GridConfig/PlacementEventChannel/ContainerData references are injected via Initialize(), not the Inspector
├── CameraManager         — (exists) Free-orbit camera rig — middle-mouse drag orbits/tilts, WASD/arrows pan, scroll zooms (including during placement mode — placement no longer uses scroll). Drives the scene's pre-placed MainCamera (never destroys/recreates/reparents it — see Camera System below). Created via code by Bootstrapper — its CameraConfig/CameraRig references are injected via Initialize()
├── ConveyorManager       — Registry of placed conveyor segments; drives belt movement each tick — proposed
├── StorageManager         — Registry of placed containers; tracks stored goods per container, capacity, (Phase 2) type filters — proposed; will subscribe to PlacementEventChannel.OnPiecePlaced once it exists
├── ReceivingManager        — Spawns incoming goods at the Receiving dock on a schedule — proposed
├── ShippingManager          — Issues Company orders, validates fulfillment at the Shipping dock, tracks deadlines — proposed
├── ScoreManager              — Orders fulfilled/missed, (Phase 2) throughput/efficiency — proposed
└── UIManager                  — HUD, order queue display, placement UI, run summary — proposed
```

Manager access pattern:
```csharp
// Cache in Awake — never call FindFirstObjectByType<T>() in Update
private GridManager _gridManager;
void Awake() { _gridManager = FindFirstObjectByType<GridManager>(); }
```

---

## Architecture

### Folder Structure (proposed — create as each system is actually built)
```
Assets/Scripts/
├── Core/       — Bootstrapper (exists), future Managers, interfaces, base classes
├── Grid/       — GridConfig (exists, now 3D-aware), GridVisualizer (exists), future runtime GridManager
│   └── Editor/ — Editor-only tooling, e.g. PlatformGridPlacerWindow (exists)
├── Placement/  — PlacementManager (exists), PlacementEventChannel (exists), future IPlaceable contract
├── Camera/     — CameraManager (exists), CameraConfig (exists). Namespace is StorageLord.CameraSystem, not StorageLord.Camera — avoids colliding with UnityEngine.Camera when referenced unqualified
├── Conveyors/  — ConveyorSegment, ConveyorManager, belt movement
├── Storage/    — ContainerData (exists, minimal), future ContainerBase/StorageManager
├── Goods/      — GoodsData (ScriptableObject), GoodsAgent (the physical item in transit/storage)
├── Docks/      — ReceivingDock, ShippingDock, ReceivingManager, ShippingManager, OrderData
├── UI/         — HUD, order queue, placement UI, run summary screen
└── Utilities/  — Debug helpers, editor tools
Assets/Data/    — ScriptableObject asset instances (GridConfig.asset, PlacementEventChannel.asset, ContainerData_CargoBoxPlaceholder.asset, CameraConfig.asset) — one folder for all tunable data assets, not scattered per-system
```

Editor-only scripts (custom windows, gizmo-only code) live in an `Editor/` subfolder of whichever system folder they belong to, per Unity convention — this excludes them from player builds automatically.

### Grid & Placement System
- One shared grid underlies platform modules, containers, and conveyors — no independent placement systems per piece type. `GridConfig` (`Assets/Data/GridConfig.asset`) is the single source of truth for cell size, height, and rotation snap — both the Editor-time `PlatformGridPlacerWindow` (X/Z only, `Vector2Int`) and the runtime `PlacementManager` (3D, `Vector3Int` — `WorldToCell3D`/`Cell3DToWorld`/`SnapPosition3D`) read from the same asset, never duplicate the math. The two method sets are additive/parallel, not shared call sites — extending one never required touching the other's call sites.
- Cell size (`GridConfig.cellSize`, currently 4m) and cell height (`GridConfig.cellHeight`, currently 3m) are **placeholders, not verified values** — SpacePlatformKit pieces have non-uniform footprints (confirmed by inspecting raw mesh bounds), so both need checking against real piece dimensions in-editor and adjusting before being treated as settled.
- Every placeable should implement a common `IPlaceable`-style contract: footprint (cells), facing/rotation, live validity check (occupied/out-of-bounds/bad-adjacency) shown via ghost preview before confirming. **Not built yet** — both the Editor tool and `PlacementManager` only check a single cell (the piece's origin) against a simple occupied-cell set, not a real multi-cell footprint. Known, explicitly-deferred gap in both places, not a silent one.
- Anchoring is permanent until explicit player removal — nothing drifts, nothing free-floats off-grid. This is a hard design requirement (GDD Pillar 3: "Clean by construction"), not a nice-to-have — any placement path that can leave a piece off-grid or unconfirmed is a bug.
- Runtime placement (`PlacementManager`) is **support-required and auto-stacking** (#3, reversing #1's original freeform-for-now decision after playtesting): a piece always snaps onto the deck (height level 0) or directly atop whatever's already placed in its aimed X/Z column — the player never chooses height directly, so a floating/unsupported piece can't be confirmed. Height is derived from `_occupiedCells` each frame, not raycast against real platform geometry — deck-level (height 0) placement is valid anywhere in grid bounds without checking whether platform mesh actually exists there; that check is a separate, not-yet-built feature. Removing a piece cascades upward, removing everything stacked directly above it in the same column, so a removal can never leave something unsupported. No stack-height cap yet.

### Camera System
- Resolves the GDD's camera Open Question as **free-orbit, perspective** (Shapez 2-style), not locked top-down — chosen because `PlacementManager` already supports vertical stacking, which a strict top-down view would occlude.
- A `CameraRig` transform (scene-placed, at platform origin) is the pan target; the scene's pre-placed `Camera` (tagged `MainCamera`, a child of `CameraRig`) is positioned each frame as a spherical offset from the rig (yaw/pitch/distance) and always faces it. `CameraManager` never destroys, recreates, or reparents that `Camera` — **this is a hard constraint, not a style choice**: `PlacementManager.Awake()` caches `Camera.main` once and never re-queries it, and since `Bootstrapper.CreatePlacementManager()` uses `AddComponent<PlacementManager>()`, that `Awake()` fires synchronously inline during `Bootstrapper.Awake()`. Swapping the camera out from under it would silently break placement/removal raycasting with no error.
- `CameraManager` has no dependency on `PlacementManager` — scroll is free for zoom at all times, including during placement mode, since placement height is derived from column occupancy rather than manual scroll input (#3). `Bootstrapper.Awake()` still creates `PlacementManager` before `CameraManager`, but that ordering is no longer load-bearing for either manager.
- All tuning (orbit sensitivity, tilt clamp, pan speed/bounds, zoom range, default framing) lives in `CameraConfig` (`Assets/Data/CameraConfig.asset`), same pattern as `GridConfig`.

### Conveyor System
- Directional belt segments moving goods between adjacent grid cells.
- Goods in transit are real simulation objects (`GoodsAgent`), not a shader/UV-scroll illusion — they need real position state for storage queries and for holding mid-belt when a downstream container is full.
- Phase 1 scope (straight + turns only, vs. also junctions/splitters) is an open question — confirm via `/ba` before building, don't assume.

### Storage System
- Containers are placed via the same grid/placement system as everything else. `ContainerData` (exists) currently only carries `displayName` + `prefab` — capacity and type-filtering are still Phase 2.
- `StorageManager` (not yet built) is the single source of truth for "what's stored where" — `ShippingManager` queries it to validate order fulfillment, never reads container contents by scanning the scene directly. It should subscribe to `PlacementEventChannel.OnPiecePlaced` (exists, currently has no listeners) rather than `PlacementManager` calling into it directly.

### Receiving & Shipping
- `ReceivingManager` spawns `GoodsData`-defined goods at the Receiving dock on a schedule that ramps over a session (exact curve TBD, keep it data-driven/tunable, not hardcoded).
- `ShippingManager` owns the Company's order queue (`OrderData`: required goods + quantities + deadline) and marks an order fulfilled the moment matching goods reach the Shipping dock, or expired if the deadline passes first.

---

## Key Conventions

### Unity 6 API
- Use `FindFirstObjectByType<T>()` — **not** the deprecated `FindObjectOfType<T>()`
- Use the new Input System — **not** legacy `Input.GetKey` / `Input.GetAxis`
- URP shaders only — no Standard shader materials

### Input System — current state
`PlacementManager` (#1) polls the new Input System's low-level API directly (`Keyboard.current`, `Mouse.current`) rather than going through `InputSystem_Actions.inputactions`' formal action maps — this is still "the new Input System," just not the Actions-asset layer. Deliberate for now: no `PlayerController` exists yet to define a real "Gameplay" action map, and hand-editing the `.inputactions` asset's binding/composite structure without a live Editor to verify it against was judged too risky for this pass. **Revisit once a real `PlayerController`/action map exists** — at that point `PlacementManager`'s raw polling (Tab/R/click) should move into a proper action map so it doesn't double-handle the same physical inputs as gameplay controls.

### Script Quality
- Every public method and every Unity message (`Awake`, `Start`, `Update`, etc.) **must** have an XML summary comment describing its purpose, parameters, and any side effects
- Scripts over ~150 lines should be audited — they are likely doing too much (SRP)
- Prefer many small focused components over one large script
- No `GetComponent<T>()`, `FindFirstObjectByType<T>()`, or any scene lookups inside `Update` / `FixedUpdate` — cache everything in `Awake`

### ScriptableObject-First Design
All of the following **must** live in ScriptableObject assets, never hardcoded:
- Goods definitions (`GoodsData` — display name, prefab, category, footprint)
- Container stats (`ContainerData` — capacity, footprint, Phase 2 accepted-goods filter)
- Conveyor stats (`ConveyorData` — speed, footprint)
- Order composition/timing (`OrderData` — required goods, quantities, deadline)
- Receiving spawn scheduling/pacing tuning

### Event-Driven Communication
| Situation | Use |
|---|---|
| Game state changes (Playing → Paused → RunSummary) | C# `event Action` / `event Action<T>` |
| Designer-wired Inspector callbacks | `UnityEvent` |
| Cross-system broadcasts (good delivered, order fulfilled, order expired, placement confirmed) | ScriptableObject event channel |

Always unsubscribe (`-=`) in `OnDisable` or `OnDestroy`.

### Comment Standard
```csharp
/// <summary>
/// Attempts to place a piece at the given cell, validating footprint occupancy and adjacency.
/// </summary>
/// <returns>True if placement succeeded and the piece was anchored; false if the cell(s) were invalid.</returns>
public bool TryPlace(GridCell origin, PlaceableData data) { ... }
```

### Grid & Placement Convention
- Snapping is the only placement mode — no freeform position/rotation for gameplay pieces (platform modules, containers, conveyors).
- Ghost preview must reflect the real validity check (occupied cells, out-of-bounds, bad adjacency) before the player confirms — never allow a confirm that a subsequent frame could reject.
- Rotation snaps to grid-aligned facings only (typically 90° increments) — match whatever the SpacePlatformKit's own module grid uses, don't invent an independent rotation scheme.

---

## Unity MCP

This project is connected to the Unity Editor via the `unity-mcp` relay (`Unity_*` tools) **when a session has it available** — it's not guaranteed present every session (depends on whether the Unity Editor is open with the relay running and reachable from that session). Use it to enter/exit Play Mode, capture the scene/game view, and read the Editor console instead of asking the user to check manually. See the `run` skill for the standard verification loop — Storage Lord is a 3D project (not 2D top-down like other projects using this same pipeline) with a free-orbit perspective camera (#2, resolving the GDD's camera Open Question), so always prefer `Unity_Camera_Capture` for verification screenshots over `Unity_SceneView_Capture2DScene`.

**When unavailable:** `/run`, `/test`, and `/ux`'s visual pass all require it. Don't fabricate a result if it's missing — `/test` already has this rule built in (falls back to a flagged pass-through when no tests exist, never guesses PASS). `/ux` should do the same: if the issue has a real visual surface, a missing connection is an honest `UX RESULT: FAIL` with a clear "environment gap, not a code defect" note, not a skip. `/handoff` should treat that specific case as a pause for manual verification, not a mechanical dev-retry loop — retrying `/dev` can't fix a missing Editor connection.

---

## Dependencies (UPM)

**Present:** Universal Render Pipeline (URP), Input System (new), AI Navigation, Timeline, Visual Scripting, Test Framework, Tilemap module (present by default, not currently used by any 3D grid logic).

**Needed for Phase 1 (not yet added):** none identified yet — grid/placement/conveyor systems are custom code, no additional package dependency expected. Revisit if pathfinding (AI Navigation) turns out to be relevant to conveyor routing.

---

## Dev Pipeline

This project uses an agent pipeline modeled on a proven multi-skill setup (BA → Architecture Review → Dev → Test → UX → Board), all backed by GitHub Issues on `ScurvyMonkey/StorageLord`:

| Skill | Responsibility |
|---|---|
| `/ba` | Requirements, spec definition, GitHub Issues management |
| `/arch #<issue>` | Architecture review — validates fit, risk, and phase-scope compliance before dev starts |
| `/dev #<issue>` | Implementation — reads issue, implements, closes when done |
| `/test #<issue>` | Runs Unity Test Framework suites, classifies failures as feature vs. regression |
| `/ux #<issue>` | Visual/usability review via Play Mode capture |
| `/board` | GitHub label/pipeline-stage management |
| `/triage` | Scores and ranks the open backlog |
| `/handoff #<issue>` (alias: `greenlight #<issue>`) | Drives an approved issue autonomously through dev → test → ux → ship |

See each skill's file in `.claude/skills/` for full detail. Recommended flow: `/ba <idea>` → `/arch` → `/board` creates the issue → `greenlight #N` to run the rest autonomously, or drive steps manually.

---

## Documentation References
- Unity Manual: https://docs.unity3d.com/Manual/UnityManual.html
- Scripting API: https://docs.unity3d.com/ScriptReference/index.html
- Design doc: `docs/GDD.md`
