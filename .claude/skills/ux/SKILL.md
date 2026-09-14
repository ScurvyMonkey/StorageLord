---
name: ux
description: Visual/usability review for Storage Lord changes tied to a GitHub issue — checks the rendered platform against the GDD pillars (clean, anchored, grid-snapped placement; satisfying not spaghetti) and basic usability heuristics, plus a static pass over code conventions. Invoke as /ux #<issue>.
---

# Storage Lord — UX & Identity Review Agent

When invoked with `/ux #N`, check the changes made for issue #N against usability conventions and, once established, visual identity. Look at both the rendered game and the code diff, then emit a machine-parseable result for `/handoff`. You review. You do NOT write app code or modify specs.

---

## Your Request

$ARGUMENTS

---

## Repo

`ScurvyMonkey/StorageLord`

---

## Where This Skill Is Thin Right Now

Storage Lord has no defined visual identity yet beyond the two asset packs it's built on (SpacePlatformKit, ScifiCommoditiesTradeGoodsLootCollection) and the GDD's stated tone ("the Company doesn't care about you" — transactional, not punishing). Phase 1 has no UI polish pass yet. This skill currently reviews **structure and usability — especially the grid/placement contract**, not brand fit. Once an actual UI style guide exists, expand Step 3's checklist the same way `CLAUDE.md`'s conventions section would grow — don't invent identity rules here that aren't backed by an actual decision.

---

## Review Method

Two passes:
1. **Visual pass** — enter Play Mode, capture the affected platform/screen, judge against GDD pillars and usability heuristics.
2. **Static pass** — check the code diff against the conventions checklist.

Skip the visual pass (run static only) if the issue has no visual surface — see Step 1.

---

## App Launch

Use the `run` skill's mechanism (`Unity_ManageEditor` Play, `Unity_Camera_Capture` for the platform view, `Unity_ReadConsole` for errors) — don't reinvent it. Save any screenshots to the session scratchpad, never commit them.

---

## Step 1 — Determine Affected Surface

```
gh issue view <N> --repo ScurvyMonkey/StorageLord
```

Read the "Affected Files" section from the `/ba` spec and the "Files changed" list from `/dev`'s completion comment — don't infer scope from a raw `git diff`, since the working tree may carry unrelated in-progress changes.

| File is under… | Treat as |
|---|---|
| `Assets/Scripts/Grid/`, `Placement/` (any placeable-facing change) | Direct visual surface — capture the platform mid-placement, not just idle |
| `Assets/Scripts/Conveyors/`, `Goods/` (with a prefab/visual component) | Direct visual surface — capture goods actually moving |
| `Assets/Scripts/Docks/` (Receiving/Shipping, with a visual component) | Direct visual surface — capture the dock in use |
| `Assets/Scripts/UI/` | Direct visual surface — capture the relevant HUD/screen |
| `Assets/Scripts/Core/` (manager logic only, no new visuals) | Indirect — capture the platform anyway if timing/spawn behavior changed, since that's observable even without new meshes |
| `Assets/Scripts/Storage/` (stats/capacity only, no new visual) | Indirect — static pass only unless a new container visual was added |
| `.claude/skills/*.md`, `CLAUDE.md`, `docs/*.md` | No visual surface |

**If no visual surface:** skip to Step 3 (static pass), then emit `UX RESULT: PASS (skipped — no visual surface changed)` and go to Step 4.

---

## Step 2 — Visual Pass

1. Enter Play Mode via the `run` skill.
2. If the issue describes a specific interaction (placing a piece, a good moving down a conveyor, an order being fulfilled, a deadline expiring), drive it before capturing — an idle platform doesn't show the actual change.
3. Capture via `Unity_Camera_Capture` (or `Unity_SceneView_Capture2DScene` only if the resolved camera is confirmed strict top-down orthographic), centered on the platform/grid.
4. Judge against:
   - **Grid/placement pillar fit** — the single most important check for this game. Is the affected piece snapped to the grid, fully anchored, with no visible drift or overlap? Does the ghost/preview (if applicable) clearly communicate valid vs. invalid placement? A piece that looks "almost aligned" is a fail, not a nitpick — GDD Pillar 3 ("Clean by construction") is a hard requirement, not an aesthetic preference.
   - **Readability** — can you tell container from conveyor from platform structure from goods at a glance? Is anything invisible, a magenta/missing-material placeholder, or overlapping unreadably?
   - **Flow legibility** — for conveyor/goods changes, is it clear where a good is coming from and going to? A satisfying logistics game lives or dies on the player being able to read their own routing at a glance — this is the closest analogue to SMASH-style "readability" checks, adapted to this genre.
   - **Usability heuristics** — is there feedback when a placement confirms/rejects, a good arrives, or an order fulfills/expires? Is the HUD (once it exists) legible against the platform/space backdrop?

---

## Step 3 — Static Pass

Read the changed files and check against `CLAUDE.md`:

- [ ] Tunable values are in a ScriptableObject, not inline magic numbers
- [ ] No scene lookups inside `Update`/`FixedUpdate`
- [ ] New manager (if any) only instantiated via `Bootstrapper`
- [ ] Any new placeable implements the shared grid/placement contract (footprint, live validity, permanent anchoring) — no one-off positioning code
- [ ] Nothing here belongs to Phase 2 or unscoped backlog (goods categories, junctions/splitters, multi-platform, meta-progression) per CLAUDE.md's Roadmap — flag it even if `/arch` already should have caught it; a second check here is cheap insurance
- [ ] New UI elements don't hardcode strings that should obviously be data-driven later (soft check — Phase 1 doesn't need full localization infrastructure, just don't paint yourself into a corner)

---

## Step 4 — Emit Result

Post a comment:
```
gh issue comment <N> --repo ScurvyMonkey/StorageLord --body "<UX Review body>"
```

Body format:
```
## UX Review
**Verdict:** PASS | FAIL
**Screens reviewed:** [or "none — no visual surface changed"]

### Findings
- [specific issue, or "None — matches grid/placement pillars and usability heuristics" if PASS]
```

Then end your own output with exactly one of:

**PASS:**
```
UX RESULT: PASS
```

**FAIL:**
```
UX RESULT: FAIL
DETAILS:
[specific findings — file/line or screen, and what's wrong]
```

---

## Output Contract (normative)

- `UX RESULT:` must appear verbatim, on its own line, near the end of output.
- No `UX RESULT:` line found → `/handoff` treats it as FAIL and escalates: `"/ux did not emit a parseable result — escalating for manual review."`
- No `FAILURE TYPE` distinction — every `/ux` FAIL follows the same retry path as a feature test failure.

---

## Error Handling

- Unity/Play Mode fails to launch or capture → `UX RESULT: FAIL` / `DETAILS: App failed to launch — [error]`. Never silently pass.
- A screen/interaction can't be reached or driven → report it as a finding with specifics, don't crash the whole review.
- `gh` not authenticated → still perform the review and emit the result; note in chat that the comment couldn't be posted.
