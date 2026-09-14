---
name: test
description: Runs Storage Lord's Unity Test Framework suite for a given issue and emits a machine-parseable PASS/FAIL result for /handoff. Invoke as /test #<issue>.
---

# Storage Lord — Test Agent

When invoked with `/test #N`, run the automated test suite, determine which systems issue #N touched, classify failures as feature vs. regression, and emit a machine-parseable result for `/handoff`. You run tests. You do NOT write app code or modify specs.

---

## Your Request

$ARGUMENTS

---

## Repo

`ScurvyMonkey/StorageLord`

---

## Test Infrastructure

Unity Test Framework (`com.unity.test-framework`), run in-Editor via `unity-mcp`'s `Unity_RunCommand` tool (compiles and executes C# against the already-open Editor — there is no separate CLI test runner here, and spawning a second `-batchmode -runTests` Unity process against the same project path will fail on the project lock while the interactive Editor is open).

**This mechanism has not yet been exercised on this project as of this skill's creation** (no test assemblies exist yet, and neither does any gameplay code — Phase 1 is starting from scratch). The first real `/test` run should treat the approach below as a hypothesis to validate, not a guarantee — if `TestRunnerApi` doesn't behave as expected via `Unity_RunCommand`, fall back to the manual path (Step 3) and note the gap so the mechanism can be fixed rather than silently papered over.

Expected test locations once they exist: `Assets/Tests/EditMode/` and `Assets/Tests/PlayMode/`, each with their own assembly definition referencing `UnityEngine.TestRunner`/`UnityEditor.TestRunner`. `EditMode` tests are the natural fit for grid math (footprint/occupancy/coordinate conversion) since they don't need Play Mode; conveyor movement and order-fulfillment timing likely need `PlayMode`.

---

## Step 1 — Check Whether Tests Exist

Use `Unity_FindProjectAssets` or `Unity_Grep` to check for any `*.asmdef` under `Assets/Tests/` (or any file referencing `[Test]`/`[UnityTest]`).

**If no tests exist yet:**
```
TEST RESULT: PASS
```
But say explicitly in your own chat output (not the machine-parsed block): `⚠️ No automated tests exist yet for Storage Lord — this is a pass-through, not a real verification. Manual Play Mode check via /run is the only current safety net.` `/handoff` will treat this as PASS and proceed, carrying your warning into its own notification.

## Step 2 — Identify Changed Systems

Read the issue (`gh issue view <N> --repo ScurvyMonkey/StorageLord`) and its `/dev` completion comment for the "Files changed" list. Map changed files to systems (Grid, Placement, Conveyors, Storage, Goods, Docks, UI, Core) by folder per `CLAUDE.md`'s folder structure.

## Step 3 — Run Tests

**Primary mechanism:** via `Unity_RunCommand`, execute a `CommandScript` that calls `UnityEditor.TestTools.TestRunner.Api.TestRunnerApi`, filtered to `TestMode.EditMode` and `TestMode.PlayMode`, writing pass/fail/test-name results out (e.g. via `result.Log(...)` per test, or to a results file under the scratchpad you then read back). `TestRunnerApi.Execute` is callback-driven — you may need one `Unity_RunCommand` call to kick it off and a short follow-up read (console via `Unity_ReadConsole`, or a results file) once it completes, rather than expecting synchronous output from a single call.

**Manual fallback**, if the above doesn't produce reliable results: ask the user to open **Window → General → Test Runner** in the Unity Editor, run all tests, and report back pass/fail counts and failure messages. Do not fabricate a result if you can't get real data — an unverifiable `/test` run should escalate as FAIL/regression per the Output Contract, not guess PASS.

## Step 4 — Classify Failures

Per failed test: if its system is in the changed-systems list from Step 2 → **feature failure**. Otherwise → **regression**. If both occur, emit `FAILURE TYPE: regression` (regressions take precedence).

## Step 5 — Emit Result

**All tests pass:**
```
TEST RESULT: PASS
```

**Any failure:**
```
TEST RESULT: FAIL
FAILURE TYPE: feature | regression
FAILED MODULES: [comma-separated system names, e.g. "Grid,Placement"]
DETAILS:
[test names, error messages, assertion failures]
```

---

## Output Contract (normative)

- `TEST RESULT:` must appear verbatim, on its own line, at or near the end of output.
- `FAILURE TYPE:` must be exactly `feature` or `regression`.
- No `TEST RESULT:` line (e.g. the runner crashed or couldn't be reached) → `/handoff` treats it as regression and escalates.

---

## Error Handling

- `Unity_RunCommand` fails to compile/execute the test-runner script → `TEST RESULT: FAIL` / `FAILURE TYPE: regression` / `DETAILS: [error]` — don't guess a PASS.
- Unity Editor not reachable via `unity-mcp` → same treatment; tell the user to focus/open the Editor.
- `gh` not authenticated → still run tests and emit a result; issue reading is best-effort.

---

## Notes for the Developer

- When Phase 1's first real tests get written, update this skill's "not yet exercised" caveat once the `TestRunnerApi`-via-`Unity_RunCommand` path is confirmed working, and record the working pattern in `memory/PATTERNS.md`.
- The grid/placement contract (footprint occupancy, snap validity, anchoring) is the single highest-value thing to cover with `EditMode` tests early — it's the invariant every other system depends on, and it's pure logic (no Play Mode needed) if `GridManager` is written cleanly.
- Keep spec/test files independently runnable per system, matching the folder-to-system mapping above, so `FAILED MODULES` stays accurate without manual remapping later.
