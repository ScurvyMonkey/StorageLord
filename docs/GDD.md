# Storage Lord — Game Design Document

## Concept

**Storage Lord** is a satisfying, grid-based logistics micro-game set on a lone space storage platform. You don't work *for* the Company in any heroic sense — you work *for* the goods. The Company only cares that incoming cargo gets received, held, and shipped out on schedule. Everything you do is in service of keeping that pipeline clean: **Receiving → Storage → Shipping.**

The player builds out their platform with conveyors and containers on a fixed grid, routing goods from the Receiving dock into storage, then from storage out to the Shipping dock to fulfill the Company's orders before their deadlines.

## Pillars

1. **Satisfying, not mind-bending.** This is not a puzzle game about clever constraint-solving under scarcity, and it is not a spaghetti-factory sandbox. Placement is quantized, snapped, and anchored — every piece clicks into a clean grid position. The fun is in the flow of goods moving smoothly through a station the player built, not in untangling a mess.
2. **Grids everywhere.** All placement — conveyors, containers, platform modules — snaps to a shared grid. No freeform rotation/offset placement. Consistency of grid logic is a hard requirement, not a nice-to-have.
3. **Clean by construction.** The tools available to the player (grid snapping, anchored containers, straight/turn/junction conveyor pieces) should make an ugly layout hard to build, not just possible to avoid. Contrast with Factorio/Satisfactory-style sandboxes, which are explicitly *not* the reference here — Storage Lord should feel closer to a tidy warehouse-management toy.
4. **The Company doesn't care about you.** Tone comes through in flavor text/UI, not mechanical punishment — orders are transactional, deadlines are strict, there's no narrative reward for going above and beyond. The satisfaction is intrinsic to running a tight operation, not from being told you're special.

## Setting

A single, modest space storage platform, built from modular sci-fi station pieces (docking arms, floor sections, hangars, tunnels). Backdrop is open space — a starfield/nebula skybox, no atmosphere, no horizon. The platform is the entire playable space; there's no traversal to other locations in scope for the core loop.

## Core Loop

1. **Receive.** Goods arrive at the Receiving dock on a schedule (a steady trickle, not all at once) — sci-fi commodities and trade goods (raw resources, cargo crates, foodstuffs, contraband, artifacts — see Assets below).
2. **Store.** The player routes each incoming good via conveyor to an appropriate container. Containers are placed on the grid ahead of time by the player; routing goods into a container that doesn't match its type (if type-matching is in scope — see Open Questions) should be visibly blocked, not silently wrong.
3. **Fulfill.** The Company posts shipping orders: a goods list, a quantity, and a deadline. The player routes the matching stored goods via conveyor to the Shipping dock before time runs out.
4. **Repeat, under rising pressure.** Order frequency/complexity increases over a session, pushing the player to keep expanding and re-routing their platform — without it ever devolving into an unreadable tangle, per Pillar 3.

## Systems

### Grid & Placement
- A single shared grid underlies the whole platform: platform modules, containers, and conveyors all occupy and snap to grid cells.
- Every placeable piece has a footprint (in cells) and a facing/rotation (conveyors and dock-facing containers care about direction).
- Placement is validated live (ghost preview) before confirming — occupied cells, invalid adjacency (e.g., a conveyor with nothing to connect to), and out-of-bounds placement are all rejected with clear feedback, not silently allowed.
- Anchoring: once placed, a piece is locked to its cell(s) until explicitly picked up/removed by the player — nothing drifts or free-floats.

### Conveyors
- Directional belt segments that move goods from one grid cell to an adjacent one.
- Straight, turn, and (likely, pending scope) junction/splitter pieces — see Open Questions on how much conveyor complexity Phase 1 actually needs.
- Goods on a conveyor are simulation objects (not just a visual shader trick) — this matters for later scoring/throughput systems and for goods needing to be "held" mid-belt if a container downstream is full.

### Storage (Containers)
- Placeable containers with a capacity and (pending scope) an accepted-goods filter.
- A container's stored contents are queryable by the Shipping system when fulfilling an order.

### Receiving
- A fixed dock (or docks) where incoming goods spawn/arrive, one at a time onto the adjacent conveyor, on a schedule that ramps in frequency/variety as a session progresses.

### Shipping & Orders
- A fixed dock where the Company's orders are fulfilled — goods routed here that match an open order's requirements count toward it; the order completes (and clears) once fully met, or fails/expires if its deadline passes first.
- The Company posts a rolling queue of orders; multiple can be open at once.

### Scoring
- Orders fulfilled vs. missed is the core score signal. Throughput/efficiency (time-to-fulfill, no dropped goods) is a likely secondary signal — exact scoring formula is an open design question, not a Phase 1 blocker.

## Assets On Hand

- **SpacePlatformKit** — modular station geometry (floors, platform elements, bridges, tunnels, hangars, docking/turret modules, in five color variants) for building the physical platform the grid sits on.
- **ScifiCommoditiesTradeGoodsLootCollection** — the goods themselves: cargo crates/pallets, raw resources/metals (iron, gold, uranium, etc.), foodstuffs, drugs, alien artifacts, fuel. Wide enough variety to support goods categories/types down the line.

Both packs are static meshes/prefabs only (no gameplay scripts) — all grid, placement, conveyor, and storage logic is built from scratch on top of them.

## Roadmap

| Phase | Adds |
|---|---|
| **1 — Core Loop** (current) | Grid + placement system, platform built from SpacePlatformKit pieces, conveyor movement, containers, Receiving dock spawning goods on a schedule, Shipping dock + Company orders with deadlines, basic scoring (fulfilled/missed), clean anchored placement UX |
| **2 — Depth & Polish** | Goods categories/type-matching against containers, conveyor junctions/splitters, order complexity (multi-good orders, priority/rush orders), efficiency scoring, audio/VFX feedback (satisfying "click" of placement, belt hum, shipping confirmation), UI polish |

Later ideas (multiple platforms, hazards/events, meta-progression/unlocks, narrative beats from "the Company") are explicitly **not scoped** yet — they're backlog material for after Phase 2, not assumptions baked into Phase 1 architecture.

## Open Questions (flag for `/ba` before their systems are specced)

- **Camera:** top-down orthographic vs. angled isometric vs. free-orbit? Genre precedent (and the "grids we love grids" framing) points toward top-down or isometric, but this hasn't been stated explicitly.
- **Grid cell size:** should be derived from the SpacePlatformKit's actual module bounds (inspect in-editor), not assumed.
- **Container type-matching:** do containers filter by goods category (Phase 1) or is any container general-purpose until Phase 2 introduces categories?
- **Conveyor complexity for Phase 1:** straight + turns only, or do junctions/splitters belong in Phase 1 given how central routing is to the loop?
- **Session structure:** endless/score-attack, or bounded "shifts"/days with a summary screen between them?
- **Failure state:** can a session be "lost" (e.g., too many missed orders), or is it purely a running score with no hard fail?

## Documentation References
- Unity Manual: https://docs.unity3d.com/Manual/UnityManual.html
- Scripting API: https://docs.unity3d.com/ScriptReference/index.html
