# CLAUDE.md — Storage Lord

## Project Overview

**Storage Lord** is a Unity 6 satisfying, grid-based space logistics micro-game. The player manages one storage platform for an indifferent Company: goods arrive at Receiving, get routed via conveyors into grid-anchored containers, and get routed back out to Shipping to fulfill the Company's orders before their deadlines. Full design context lives in `docs/GDD.md` — **read it before scoping any new system**, especially its Open Questions section, since several core mechanics (camera style, grid cell size, container type-matching, conveyor complexity) aren't nailed down yet.

Built with C# and Universal Render Pipeline (URP), 3D. Uses the **SpacePlatformKit** (modular station geometry) and **ScifiCommoditiesTradeGoodsLootCollection** (goods/cargo meshes) asset packs — both are static meshes/prefabs only; there is no gameplay logic to inherit from them.

**Repo:** `ScurvyMonkey/StorageLord` (GitHub — hosts both code and the Issues backlog)

---

## Current Phase: Phase 1 — Core Loop

Nothing is implemented yet — this is a from-scratch start. Phase 1 is the MVP: a working Receive → Store → Ship loop on a single platform, with clean grid placement as a first-class requirement (not a later polish pass). Phase 2 (goods categories/type-matching, conveyor junctions/splitters, order complexity, audio/VFX polish) is explicitly out of scope until Phase 1's loop is playable end-to-end. See [Roadmap](docs/GDD.md#roadmap) in the GDD for the full phase breakdown.

**Phase 1 sub-system sequencing** (update this list as each sub-system is picked up/completed):
- **Grid & placement system** — not yet started. Foundational — conveyors, containers, and platform modules all depend on it. Cell size must be derived from the SpacePlatformKit's actual module bounds (inspect in-editor), not guessed.
- Platform assembly from SpacePlatformKit pieces — not yet started (depends on grid system for placement, but the platform itself may be partly hand-authored rather than player-placed — resolve via `/ba` before building)
- Conveyor movement — not yet started (depends on grid system)
- Containers/storage — not yet started (depends on grid system)
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

No code exists yet, so this is a **starting proposal for the first `/dev` pass to validate and refine**, not settled fact — update this section the moment real implementation diverges from it.

All managers are singletons created by `Bootstrapper.cs` via `DontDestroyOnLoad`. **Do not create singletons outside this pattern.**

```
Bootstrapper
├── GameManager        — Game state machine (MainMenu, Playing, Paused, RunSummary)
├── GridManager         — Grid definition, cell occupancy, world↔cell coordinate conversion
├── PlacementManager     — Active placement tool: ghost preview, rotation, validity checks against GridManager, confirm/cancel
├── ConveyorManager       — Registry of placed conveyor segments; drives belt movement each tick
├── StorageManager         — Registry of placed containers; tracks stored goods per container, capacity, (Phase 2) type filters
├── ReceivingManager        — Spawns incoming goods at the Receiving dock on a schedule
├── ShippingManager          — Issues Company orders, validates fulfillment at the Shipping dock, tracks deadlines
├── ScoreManager              — Orders fulfilled/missed, (Phase 2) throughput/efficiency
├── UIManager                  — HUD, order queue display, placement UI, run summary
└── CameraManager                — Platform view control (style TBD — see GDD Open Questions)
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
├── Core/       — Bootstrapper, all Managers, interfaces, base classes
├── Grid/       — GridManager, GridCell, world↔cell coordinate math
├── Placement/  — PlacementManager, IPlaceable, ghost preview, rotation/snap logic
├── Conveyors/  — ConveyorSegment, ConveyorManager, belt movement
├── Storage/    — ContainerBase, container variants, StorageManager
├── Goods/      — GoodsData (ScriptableObject), GoodsAgent (the physical item in transit/storage)
├── Docks/      — ReceivingDock, ShippingDock, ReceivingManager, ShippingManager, OrderData
├── UI/         — HUD, order queue, placement UI, run summary screen
└── Utilities/  — Debug helpers, editor tools
```

### Grid & Placement System
- One shared grid underlies platform modules, containers, and conveyors — no independent placement systems per piece type.
- Cell size derives from the SpacePlatformKit's actual module bounds — check `PlatformFloor1Blue`/`PlatformElement1Blue`-style prefabs in-editor rather than hardcoding a guessed value.
- Every placeable implements a common `IPlaceable`-style contract: footprint (cells), facing/rotation, live validity check (occupied/out-of-bounds/bad-adjacency) shown via ghost preview before confirming.
- Anchoring is permanent until explicit player removal — nothing drifts, nothing free-floats off-grid. This is a hard design requirement (GDD Pillar 3: "Clean by construction"), not a nice-to-have — any placement path that can leave a piece off-grid or unconfirmed is a bug.

### Conveyor System
- Directional belt segments moving goods between adjacent grid cells.
- Goods in transit are real simulation objects (`GoodsAgent`), not a shader/UV-scroll illusion — they need real position state for storage queries and for holding mid-belt when a downstream container is full.
- Phase 1 scope (straight + turns only, vs. also junctions/splitters) is an open question — confirm via `/ba` before building, don't assume.

### Storage System
- Containers are placed via the same grid/placement system as everything else.
- `StorageManager` is the single source of truth for "what's stored where" — `ShippingManager` queries it to validate order fulfillment, never reads container contents by scanning the scene directly.

### Receiving & Shipping
- `ReceivingManager` spawns `GoodsData`-defined goods at the Receiving dock on a schedule that ramps over a session (exact curve TBD, keep it data-driven/tunable, not hardcoded).
- `ShippingManager` owns the Company's order queue (`OrderData`: required goods + quantities + deadline) and marks an order fulfilled the moment matching goods reach the Shipping dock, or expired if the deadline passes first.

---

## Key Conventions

### Unity 6 API
- Use `FindFirstObjectByType<T>()` — **not** the deprecated `FindObjectOfType<T>()`
- Use the new Input System — **not** legacy `Input.GetKey` / `Input.GetAxis`
- URP shaders only — no Standard shader materials

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

This project is connected to the Unity Editor via the `unity-mcp` relay (`Unity_*` tools). Use it to enter/exit Play Mode, capture the scene/game view, and read the Editor console instead of asking the user to check manually. See the `run` skill for the standard verification loop — Storage Lord is a 3D project (not 2D top-down like other projects using this same pipeline), so prefer `Unity_Camera_Capture` for verification screenshots; only reach for `Unity_SceneView_Capture2DScene` if the resolved camera style (see GDD Open Questions) turns out to be a strict top-down orthographic view.

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
