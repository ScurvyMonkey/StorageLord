# CLAUDE.md — Storage Lord

## Project Overview

**Storage Lord** is a Unity 6 satisfying, grid-based space logistics micro-game. The player manages one storage platform for an indifferent Company: goods arrive at Receiving, get routed via conveyors into grid-anchored containers, and get routed back out to Shipping to fulfill the Company's orders before their deadlines. Full design context lives in `docs/GDD.md` — **read it before scoping any new system**, especially its Open Questions section, since several core mechanics (grid cell size, container type-matching, conveyor complexity) aren't nailed down yet (camera style was resolved by #2 — free-orbit perspective).

Built with C# and Universal Render Pipeline (URP), 3D. Uses the **SpacePlatformKit** (modular station geometry) and **ScifiCommoditiesTradeGoodsLootCollection** (goods/cargo meshes) asset packs — both are static meshes/prefabs only; there is no gameplay logic to inherit from them.

**Repo:** `ScurvyMonkey/StorageLord` (GitHub — hosts both code and the Issues backlog)

---

## Current Phase: Phase 1 — Core Loop

Nothing is implemented yet — this is a from-scratch start. Phase 1 is the MVP: a working Receive → Store → Ship loop on a single platform, with clean grid placement as a first-class requirement (not a later polish pass). Phase 2 (goods categories/type-matching, conveyor junctions/splitters, order complexity, audio/VFX polish) is explicitly out of scope until Phase 1's loop is playable end-to-end. See [Roadmap](docs/GDD.md#roadmap) in the GDD for the full phase breakdown.

**Phase 1 sub-system sequencing** (update this list as each sub-system is picked up/completed):
- **Grid & placement system** — first pass landed: `GridConfig` (cell size + rotation snap, `Assets/Data/GridConfig.asset`), `GridVisualizer` (Scene view grid gizmo), and the `Platform Grid Placer` Editor window (see Platform Assembly below) all exist. A runtime `GridManager` (`Assets/Scripts/Grid/GridManager.cs`) now exists too, landed as part of #4 once `ConveyorManager` needed to share occupancy state with `PlacementManager` — exactly the trigger this file previously named as the reason to build it. **Not yet done:** `cellSize` (4m) is an unverified placeholder — check it against SpacePlatformKit piece footprints via the grid gizmo and adjust; occupancy tracking is one-cell-per-piece-origin, not real per-piece footprint checking, so larger pieces can still visually overlap a neighbor.
- **Platform assembly from SpacePlatformKit pieces** — first pass landed, resolved as an **Editor-time, hand-authored** workflow (not player-placed at runtime): `Assets/Scripts/Grid/Editor/PlatformGridPlacerWindow.cs` (**Window → Storage Lord → Platform Grid Placer**) lets you pick a prefab and click-place it in the Scene view, snapped to the grid, parented under a `Platform` root. This is a level-design tool for you, not a system players interact with — kept clearly separate from the future runtime `PlacementManager` (containers/conveyors), though both will share `GridConfig`.
- **Conveyor movement** — first pass landed (#4), extended by #6: `ConveyorManager` (runtime, `Assets/Scripts/Conveyors/`) is the platform's second placer, alongside `PlacementManager`. Players toggle conveyor placement mode (C), left-click-drag from a start cell to lay a straight run of up to 10 segments (clamped to whichever axis the drag moved further along), cycle flow direction with Q/R while dragging, and release to confirm — a run that overlaps an already-occupied cell (shared with `PlacementManager` via `GridManager`) is rejected outright, nothing is placed. Right-click removes a single segment (no cascade — segments aren't stacked). Segments (and goods riding them) hover `ConveyorData.hoverHeight` (default 0.3m) above the deck surface via `SegmentWorldPosition(cell)`, now public so other goods sources can spawn at the correct height without duplicating the hover math. Introduces the project's first goods concept: `GoodsData`/`GoodsAgent` (`Assets/Scripts/Goods/`) — a real object with position state, not a shader trick. `ConveyorManager.RegisterGoodsAgent(GoodsAgent)` (public, added in #6) is the one entry point for handing an externally-spawned good into per-frame movement tracking — both the Editor-only debug spawn (G, aimed at a placed segment — a legacy manual-testing convenience now that `ReceivingManager` is the real source) and `ReceivingManager` route through it, so there's one shared movement list, not two. `ConveyorManager` advances any registered `GoodsAgent` sitting on a segment toward the next cell each frame, holding in place at the end of a run. Uses `ConveyorData` (prefab, `beltSpeed`, `hoverHeight`) and `GoodsData` (display name + prefab), both following `ContainerData`'s minimal pattern. **Not yet done:** turn/junction pieces (straight-only for Phase 1, deliberately), real conveyor/goods art (`TurretPlatformFlyingBlue`/`Pallet1` placeholders), and conveyor→container handoff (goods reaching a container and actually being stored — that's the follow-up type-locked storage issue).
- **Containers/storage (placement)** — first pass landed (#1), placement rules reworked to support-required auto-stacking (#3): `PlacementManager` (runtime, `Assets/Scripts/Placement/`) is the game's first real manager, created by the new `Bootstrapper` (`Assets/Scripts/Core/`). Players toggle placement mode (Tab), aim with the mouse to snap a ghost preview to the grid — height is no longer a manual input; the piece always snaps onto the deck or directly atop whatever's already placed in that X/Z column, so a second piece placed at the same column auto-stacks one level higher — rotate with R, confirm with left-click, remove with right-click (removing a piece also removes everything stacked directly above it in that column). Uses `ContainerData` (minimal — display name + prefab only; capacity/type-filtering is still Phase 2) and a placeholder container visual (`ContainerData_CargoBoxPlaceholder.asset`, since no real container art exists yet). Mutually exclusive with `ConveyorManager`'s placement mode as of #4 — entering one force-exits the other, so `R` is never ambiguous between "rotate the container ghost" and "cycle belt flow direction." **Not yet done:** real container art/design, actual goods storage/capacity (that's `StorageManager`, a separate not-yet-started piece), any stack-height cap (unlimited for now), and verifying a deck-level (height 0) placement actually lands on real platform geometry rather than open space past its edge (`PlacementManager` has no concept of platform footprint/bounds yet).
- **Camera** — first pass landed (#2): `CameraManager` (runtime, `Assets/Scripts/Camera/`) drives the scene's pre-placed `MainCamera`-tagged `Camera` as a free-orbit rig (Shapez 2-style, resolving the GDD's camera Open Question) — hold middle-mouse and drag to orbit/tilt, WASD/arrows to pan, scroll to zoom. Uses `CameraConfig` (`Assets/Data/CameraConfig.asset`) for all tuning. Also fixed a pre-existing gap while wiring this in: `Bootstrapper` had never actually been added to `SampleScene` — nothing in the scene created it, so `PlacementManager` never ran either. Added a `Bootstrapper` GameObject to the scene with all its references wired (`GridConfig`, `PlacementEventChannel`, `ContainerData_CargoBoxPlaceholder`, `CameraConfig`, the new `CameraRig`).
- **Receiving dock + goods spawn scheduling** — first pass landed (#6): `ReceivingManager` (runtime, `Assets/Scripts/Docks/`) spawns goods automatically — the platform's first real goods source, replacing the need for `ConveyorManager`'s debug spawn for normal testing. `ReceivingDock` (a single instance, on the scene's `BuildingMediumGreen`) is deliberately **fixed/Editor-placed, not runtime-placeable** (resolved via `/ba` for #6 against the GDD's own "fixed dock" language) — but still registers its own cell with `GridManager` at startup, so `PlacementManager`/`ConveyorManager` can't be confirmed on top of it (a fixed piece still has to participate in the shared occupancy authority, not just the two runtime placers). Output cell is computed from the dock's facing (`ReceivingDock.GetOutputDirection()`, its `transform.forward` rounded to the nearest grid axis) — rotate the dock 90° in the Inspector to change which way it feeds. Spawns at a fixed rate (`ReceivingData.itemsPerMinute`, default 30 = one every 2s); if the dock's last-spawned good is still sitting unmoved on the output cell, that spawn is skipped rather than stacking. Ramping the spawn rate over a session (GDD's open question) stays deliberately unresolved — flat rate only. **Not yet done:** player-placeable Receiving docks (explicitly deferred to its own future `/ba` pass, not forgotten), goods variety, multiple docks with independent rates.
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

`Bootstrapper`, `PlacementManager`, `CameraManager`, `GridManager`, `ConveyorManager`, `ReceivingManager`, and `StorageManager` are real (#1, #2, #4, #5, #6) — everything else below is still a **starting proposal**, not settled fact. Update each entry the moment real implementation diverges from it.

All managers are singletons created by `Bootstrapper.cs` via `DontDestroyOnLoad`. **Do not create singletons outside this pattern.** `Bootstrapper` itself is deliberately minimal — it only creates what its own issues needed (`GridManager`, `PlacementManager`, `CameraManager`, `ConveyorManager`, `ReceivingManager`, `StorageManager`); each other manager below gets added to it only once a real issue builds it, not preemptively.

```
Bootstrapper (exists)
├── GameManager        — Game state machine (MainMenu, Playing, Paused, RunSummary) — proposed
├── GridManager         — (exists) Shared runtime occupancy registry (IsOccupied/Register/Unregister/NextFreeHeightLevel) — landed with #4 once ConveyorManager needed to share cell-occupancy state with PlacementManager, the exact trigger this entry used to name. Created via code by Bootstrapper, before either placer
├── PlacementManager     — (exists) Ghost preview (support-required auto-stack height, derived from GridManager occupancy)/rotation (R) adjustment, confirm (left-click)/remove (right-click, cascades up the stack). Mutually exclusive with ConveyorManager's placement mode (#4) via reciprocal ExitPlacementMode() calls. Created via code by Bootstrapper (not a prefab) — its GridConfig/GridManager/PlacementEventChannel/ContainerData references are injected via Initialize(), not the Inspector
├── CameraManager         — (exists) Free-orbit camera rig — middle-mouse drag orbits/tilts, WASD/arrows pan, scroll zooms (including during placement mode — placement no longer uses scroll). Drives the scene's pre-placed MainCamera (never destroys/recreates/reparents it — see Camera System below). Created via code by Bootstrapper — its CameraConfig/CameraRig references are injected via Initialize()
├── ConveyorManager       — (exists) Drag-based placement (C to toggle, Q/R cycle flow direction) of straight conveyor runs, sharing GridManager occupancy with PlacementManager; also drives per-frame GoodsAgent movement (RegisterGoodsAgent is the shared entry point) along placed segments, hovering at ConveyorData.hoverHeight. Mutually exclusive with PlacementManager's placement mode. Created via code by Bootstrapper — its GridConfig/GridManager/PlacementEventChannel/ConveyorData/debug GoodsData references are injected via Initialize()
├── ReceivingManager      — (exists) Finds every ReceivingDock at startup (registers each one's cell with GridManager), then spawns GoodsAgents onto each dock's output cell on a fixed timer (ReceivingData.itemsPerMinute), handing them to ConveyorManager.RegisterGoodsAgent. Created via code by Bootstrapper, after ConveyorManager (needs a live reference) — its GridConfig/GridManager/ConveyorManager/ReceivingData references are injected via Initialize()
├── StorageManager         — (exists) Registry of placed containers, keyed by cell — subscribes to PlacementEventChannel.OnPiecePlaced, registering only pieces carrying a ContainerInstance marker (that event also fires for conveyor segments). Exposes TryStoreAt(cell, goodsData) for ConveyorManager. Created via code by Bootstrapper — its PlacementEventChannel reference is injected via Initialize()
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
├── Grid/       — GridConfig (exists, now 3D-aware), GridVisualizer (exists), GridManager (exists, shared occupancy registry)
│   └── Editor/ — Editor-only tooling, e.g. PlatformGridPlacerWindow (exists)
├── Placement/  — PlacementManager (exists), PlacementEventChannel (exists), future IPlaceable contract
├── Camera/     — CameraManager (exists), CameraConfig (exists). Namespace is StorageLord.CameraSystem, not StorageLord.Camera — avoids colliding with UnityEngine.Camera when referenced unqualified
├── Conveyors/  — ConveyorManager (exists), ConveyorData (exists) — belt movement + drag-based placement, no separate ConveyorSegment component (a placed segment's flow direction is tracked in ConveyorManager's own dictionary, not a per-instance component)
├── Storage/    — ContainerData (exists, has capacity), StorageManager (exists), ContainerInstance (exists — per-instance runtime marker/state, attached by PlacementManager, not part of the prefab)
├── Goods/      — GoodsData (exists), GoodsAgent (exists) — the physical item in transit/storage
├── Docks/      — ReceivingDock (exists), ReceivingManager (exists), ReceivingData (exists); ShippingDock, ShippingManager, OrderData still proposed
├── UI/         — HUD, order queue, placement UI, run summary screen
└── Utilities/  — Debug helpers, editor tools
Assets/Data/    — ScriptableObject asset instances (GridConfig.asset, PlacementEventChannel.asset, ContainerData_Hangar.asset, CameraConfig.asset, ConveyorData_Segment.asset, GoodsData_Pallet.asset, ReceivingData_Default.asset) — one folder for all tunable data assets, not scattered per-system. ContainerData_CargoBoxPlaceholder.asset is unused since #5 swapped the default container to Hangar — left in the project, not deleted
```

Editor-only scripts (custom windows, gizmo-only code) live in an `Editor/` subfolder of whichever system folder they belong to, per Unity convention — this excludes them from player builds automatically.

### Grid & Placement System
- One shared grid underlies platform modules, containers, and conveyors — no independent placement systems per piece type. `GridConfig` (`Assets/Data/GridConfig.asset`) is the single source of truth for cell size, height, and rotation snap — both the Editor-time `PlatformGridPlacerWindow` (X/Z only, `Vector2Int`) and the runtime `PlacementManager` (3D, `Vector3Int` — `WorldToCell3D`/`Cell3DToWorld`/`SnapPosition3D`) read from the same asset, never duplicate the math. The two method sets are additive/parallel, not shared call sites — extending one never required touching the other's call sites.
- Cell size (`GridConfig.cellSize`, currently 4m) and cell height (`GridConfig.cellHeight`, currently 3m) are **placeholders, not verified values** — SpacePlatformKit pieces have non-uniform footprints (confirmed by inspecting raw mesh bounds), so both need checking against real piece dimensions in-editor and adjusting before being treated as settled.
- Every placeable should implement a common `IPlaceable`-style contract: footprint (cells), facing/rotation, live validity check (occupied/out-of-bounds/bad-adjacency) shown via ghost preview before confirming. **Not built yet** — both the Editor tool and `PlacementManager` only check a single cell (the piece's origin) against a simple occupied-cell set, not a real multi-cell footprint. Known, explicitly-deferred gap in both places, not a silent one.
- Anchoring is permanent until explicit player removal — nothing drifts, nothing free-floats off-grid. This is a hard design requirement (GDD Pillar 3: "Clean by construction"), not a nice-to-have — any placement path that can leave a piece off-grid or unconfirmed is a bug.
- Runtime placement (`PlacementManager`) is **support-required and auto-stacking** (#3, reversing #1's original freeform-for-now decision after playtesting): a piece always snaps onto the deck (height level 0) or directly atop whatever's already placed in its aimed X/Z column — the player never chooses height directly, so a floating/unsupported piece can't be confirmed. Height is derived from `GridManager.NextFreeHeightLevel` each frame, not raycast against real platform geometry — deck-level (height 0) placement is valid anywhere in grid bounds without checking whether platform mesh actually exists there; that check is a separate, not-yet-built feature. Removing a piece cascades upward, removing everything stacked directly above it in the same column, so a removal can never leave something unsupported. No stack-height cap yet.
- `GridManager` (`Assets/Scripts/Grid/GridManager.cs`) is the single shared occupancy authority as of #4 — both `PlacementManager` and `ConveyorManager` register/unregister/query cells through it, so two independently-input-driven placers can never confirm into the same cell. Each placer still keeps its own local piece dictionary (cell → placed `GameObject`) for hit-testing/removal — `GridManager` only answers "is this cell occupied," it doesn't own instances. **Any future third placer must do the same** — route occupancy through `GridManager`, never maintain an independent registry.
- `GridConfig.deckSurfaceHeight` (found and fixed post-#4, during manual playtest): height level 0 resolves to this world Y, **not** world Y=0. SpacePlatformKit floor pieces (`SpacePlatformLargeBlue`, confirmed via `MeshRenderer.bounds`) are pivoted at their own vertical center, not their base — placing a piece's pivot at raw Y=0 buried it halfway inside the deck slab instead of resting it on top. `Cell3DToWorld`/`WorldToCell3D`/`SnapPosition3D` all measure height levels from this offset now. The mouse-aim raycast plane in both `PlacementManager.UpdatePreview` and `ConveyorManager.RaycastDeckCell` was also moved to this height (previously `Plane(Vector3.up, Vector3.zero)`) — same bug, different symptom (aiming precision at oblique camera angles, not just visual burial). Current value (`0.6918354`) is measured from the one floor piece type in the scene; **re-measure if a different-height floor piece is introduced.**

### Camera System
- Resolves the GDD's camera Open Question as **free-orbit, perspective** (Shapez 2-style), not locked top-down — chosen because `PlacementManager` already supports vertical stacking, which a strict top-down view would occlude.
- A `CameraRig` transform (scene-placed, at platform origin) is the pan target; the scene's pre-placed `Camera` (tagged `MainCamera`, a child of `CameraRig`) is positioned each frame as a spherical offset from the rig (yaw/pitch/distance) and always faces it. `CameraManager` never destroys, recreates, or reparents that `Camera` — **this is a hard constraint, not a style choice**: `PlacementManager.Awake()` caches `Camera.main` once and never re-queries it, and since `Bootstrapper.CreatePlacementManager()` uses `AddComponent<PlacementManager>()`, that `Awake()` fires synchronously inline during `Bootstrapper.Awake()`. Swapping the camera out from under it would silently break placement/removal raycasting with no error.
- `CameraManager` has no dependency on `PlacementManager` — scroll is free for zoom at all times, including during placement mode, since placement height is derived from column occupancy rather than manual scroll input (#3). `Bootstrapper.Awake()` still creates `PlacementManager` before `CameraManager`, but that ordering is no longer load-bearing for either manager.
- All tuning (orbit sensitivity, tilt clamp, pan speed/bounds, zoom range, default framing) lives in `CameraConfig` (`Assets/Data/CameraConfig.asset`), same pattern as `GridConfig`.

### Conveyor System
- Directional belt segments moving goods between adjacent grid cells. First pass landed (#4): `ConveyorManager` (`Assets/Scripts/Conveyors/`) handles both placement input and belt movement in one class, mirroring how `PlacementManager` already combines both roles for containers rather than splitting into separate placer/manager classes.
- Placement is **drag-based, straight-line only, deck-level only** (Phase 1 doesn't build turn/junction pieces at all, more conservative than the GDD's straight+turn baseline) — C toggles placement mode, left-click-drag lays a run of up to 10 segments along whichever axis (X or Z) the drag moved further along, Q/R cycle flow direction among the four cardinal directions while dragging, release confirms. A run overlapping any already-occupied cell (containers included, via `GridManager`) is rejected wholesale — nothing is placed, matching the live-validity contract.
- Goods in transit are real simulation objects (`GoodsAgent`, `Assets/Scripts/Goods/`), not a shader/UV-scroll illusion — they carry their defining `GoodsData` and current cell; `ConveyorManager` moves them toward the next cell each frame at `ConveyorData.beltSpeed` (cells/sec) if that next cell is also a conveyor segment *and* isn't already occupied by a different `GoodsAgent` (`IsCellOccupiedByOtherAgent`, added post-#6 — a busy belt visibly queues single-file instead of silently overlapping goods on top of each other), otherwise they hold in place (end of a run, or blocked). This matters for later storage queries and for holding mid-belt when a downstream container is full. Goods ride at `ConveyorManager.GoodsRestPosition(cell)` — the segment's own position plus `ConveyorData.goodsRideHeight` (measured to match `TurretPlatformFlyingBlue`'s own top surface) — not the segment's own hover height, so a base-pivoted goods prefab (`Pallet1`) visibly sits on top of the segment instead of sinking into/behind its larger mesh.
- `ConveyorManager` still has an Editor-only debug spawn action (G, aimed at a placed segment) alongside the real `ReceivingManager` (#6) — a legacy manual-testing convenience now, not the real goods source.
- Junctions/splitters remain out of scope, confirmed by #4's spec — not just an open question anymore for straight-vs-turn-only, since #4 deliberately shipped without even turn pieces.
- **Flow-direction indicator** (post-#6 polish): `TurretPlatformFlyingBlue`'s placeholder visual reads as roughly symmetric, so rotating it per `FlowRotation()` didn't actually communicate which way a segment feeds — players couldn't tell direction at a glance. Fixed with a small runtime-generated flat arrow (`AttachFlowArrow`, a procedural 2-triangle `Mesh` + `Universal Render Pipeline/Unlit` material, both built once and shared across every segment instance) parented under each segment — preview and confirmed alike — with identity local rotation, so it automatically points in the segment's flow direction by inheriting the parent's rotation rather than needing separate direction math. Destroyed automatically with its parent segment (no separate cleanup needed). Deliberately doesn't touch the shared `TurretPlatformFlyingBlue` prefab asset itself.
- **Energy connector** (post-#6 polish, static first pass — an animated flowing version is a likely future iteration): a flat emissive quad (`RebuildEnergyConnectors`/`CreateEnergyConnector`, procedural `Mesh` + `Universal Render Pipeline/Lit` material with `_EMISSION` enabled — this scene has Bloom on, so it actually glows) bridges the gap between two segments *only when they're genuinely flow-connected* (`cell` and `cell + flowDirection` both registered segments — the identical condition `AdvanceGoods` already uses to decide a good can move between them), not just physically adjacent. Rebuilt wholesale on every placement/removal (`RebuildEnergyConnectors`, called from both `ConfirmDrag` and `HandleRemoveInput`) rather than updated incrementally, so a connection completed across two separate drags still shows correctly. This means an unconfigured/wrongly-flowing run of segments deliberately shows **no** connector — reinforcing (rather than undermining) the existing "must actually connect belts, not just place them adjacent" puzzle layer.

### Storage System
- **First pass landed (#5).** Containers are placed via the same grid/placement system as everything else — `HangarBlue` replaced the old cardboard-box placeholder (`ContainerData_Hangar.asset`). `ContainerData` now carries `capacity` (`[Min(1)]`) alongside `displayName`/`prefab` — this resolves the GDD's container type-matching Open Question as a **per-instance identity lock**, not the full Phase-2 category taxonomy (a container locks to whichever single `GoodsData` it first accepts, not a category of several types).
- `StorageManager` (`Assets/Scripts/Storage/`) is the single source of truth for "what's stored where" — subscribes to `PlacementEventChannel.OnPiecePlaced` (its first real subscriber) and registers a placed piece only if it carries a `ContainerInstance` marker component, since that same event also fires for conveyor segments (`ConveyorManager.ConfirmDrag` raises it too) with nothing else in the payload to tell them apart. Exposes `TryStoreAt(cell, goodsData)` for `ConveyorManager` to call.
- `ContainerInstance` (attached by `PlacementManager.TryConfirmPlacement`, not part of the `HangarBlue` prefab itself) holds the per-instance state (`LockedType`, `CurrentCount`) and drives the door `Animator` directly via `Animator.Play(stateName)` — `HangarBlue` ships with a ready-made `HangarGrey` controller (`HangarClosed`/`HangarOpened`/`HangarOpen`/`HangarClose` clips) but **no exposed parameters**, so states are triggered by name, not `SetBool`/`SetTrigger`. Its default state (`HangarOpenCloseDEMO`, a demo loop) is overridden to `HangarClosed` on `Initialize()` — needed on both the real instance and the ghost preview (the preview instead just disables its `Animator` component entirely, since it shouldn't animate at all).
- `ConveyorManager.AdvanceGoods` hands a `GoodsAgent` off to `StorageManager.TryStoreAt` when its next cell isn't a conveyor segment (a container, most likely) — accepted removes the agent from tracking and destroys it (absorbed into storage); rejected (wrong type, full, or nothing there) just holds it in place, matching the existing end-of-run behavior. Needed a new `ConveyorManager` → `StorageManager` reference, wired by `Bootstrapper` after both exist — the original #5 spec (written before #4/#6 landed) didn't know this handoff didn't exist yet; caught by `/arch`.

### Receiving & Shipping
- **Receiving — first pass landed (#6).** `ReceivingManager` spawns `GoodsData`-defined goods at `ReceivingDock`s on a fixed, tunable rate (`ReceivingData.itemsPerMinute`) — ramping that rate over a session (GDD's open question) is deliberately still unresolved, flat rate only for now. `ReceivingDock` is a **fixed, Editor-placed** component (resolved via `/ba` against the GDD's own "fixed dock" language, not a runtime-placeable piece like containers/conveyors) — but still registers its own cell with `GridManager` at startup, since any piece occupying grid space has to participate in the shared occupancy authority, not just the two runtime placers (a gap #6's arch review caught that #4's original `GridManager` fix hadn't covered). Output cell is derived from the dock's facing (`transform.forward`, snapped to the nearest grid axis) — no separate serialized facing field. Backpressure is handled by `ReceivingManager` tracking its own last-spawned agent per dock and skipping a spawn while it's still sitting on the output cell, rather than adding a new query method to `ConveyorManager`.
- Shipping is unstarted. `ShippingManager` (proposed) will own the Company's order queue (`OrderData`: required goods + quantities + deadline) and mark an order fulfilled the moment matching goods reach the Shipping dock, or expired if the deadline passes first.

---

## Key Conventions

### Unity 6 API
- Use `FindFirstObjectByType<T>()` — **not** the deprecated `FindObjectOfType<T>()`
- Use the new Input System — **not** legacy `Input.GetKey` / `Input.GetAxis`
- URP shaders only — no Standard shader materials

### Input System — current state
`PlacementManager` (#1) and `ConveyorManager` (#4) both poll the new Input System's low-level API directly (`Keyboard.current`, `Mouse.current`) rather than going through `InputSystem_Actions.inputactions`' formal action maps — this is still "the new Input System," just not the Actions-asset layer. Deliberate for now: no `PlayerController` exists yet to define a real "Gameplay" action map, and hand-editing the `.inputactions` asset's binding/composite structure without a live Editor to verify it against was judged too risky when this was first decided. **Revisit once a real `PlayerController`/action map exists** — at that point both managers' raw polling should move into a proper action map so it doesn't double-handle the same physical inputs as gameplay controls. Now two managers do this rather than one — if a third arrives, that's probably the trigger to finally build the action map.

**Current bindings** (container placement / conveyor placement are mutually exclusive, so `R` is shared without ambiguity — entering one mode always force-exits the other):
| Key/input | Container mode (`PlacementManager`) | Conveyor mode (`ConveyorManager`) |
|---|---|---|
| Tab | Toggle placement mode | — |
| C | — | Toggle placement mode |
| Left-click | Confirm placement | Start/continue a drag run |
| Right-click | Remove piece (cascades up the stack) | Remove single segment (no cascade) |
| R | Rotate ghost 90° | Cycle flow direction forward |
| Q | — | Cycle flow direction backward |
| Escape | Cancel placement | Cancel in-progress drag |
| G | — | (Editor-only) debug-spawn a `GoodsAgent` at the aimed segment |

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

**`PlayerSettings.runInBackground` must stay `true`** (set during #6's dev pass, persisted to `ProjectSettings/ProjectSettings.asset`) — without it, Play Mode's entire frame loop freezes at frame 0 (`Time.time` never advances) whenever the Editor window isn't OS-focused, which is *always* true for an MCP-driven session since nothing ever clicks into the Editor window. This silently broke more than it looked like: `Unity_RunCommand` calls still succeed and return real data (they run on the Editor's own update, not the Play Mode player loop), so a frozen Play Mode is easy to mistake for "nothing happening yet" rather than "nothing will ever happen." If a future session finds Play Mode state not advancing between calls (timers never firing, input simulation never registering), check this setting hasn't regressed before assuming a code bug.

**Interactive input CAN be simulated reliably** once the above is true — `UnityEngine.InputSystem.InputSystem.QueueStateEvent(device, new KeyboardState(...)/new MouseState{...})`, called via `Unity_RunCommand`, is consumed by the game's normal `Keyboard.current`/`Mouse.current` polling on the next real frame. This only works spread across *separate* `Unity_RunCommand` calls with real wall-clock time between them (e.g. a `Bash sleep 1`) — looping `Thread.Sleep`+`EditorApplication.QueuePlayerLoopUpdate()` *inside* one call does not work, since that blocks the same main thread the player loop needs to advance on. `Unity_RunCommand`'s dynamic-assembly sandbox still cannot use `System.Reflection` (throws an unexplained `NullReferenceException` even wrapped in try/catch) — that limitation is unrelated to this one and still stands.

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
