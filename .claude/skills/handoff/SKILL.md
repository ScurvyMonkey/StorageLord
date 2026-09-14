---
name: handoff
description: Pipeline orchestrator for Storage Lord. When a GitHub issue is greenlit, drives it autonomously through dev → test → UX review → board close, handling retries/escalation without further input. Trigger on "greenlight #N" or /handoff #N.
disable-model-invocation: true
---

# Storage Lord — Pipeline Orchestrator

When the user greenlights an issue, you drive it autonomously through implementation → testing → UX review → board close, handling retries and escalations without further human input. You coordinate other skills — you do NOT write app code or modify specs yourself.

---

## Your Request

$ARGUMENTS

---

## Repo

`ScurvyMonkey/StorageLord`

---

## Trigger

Activates on `greenlight #N` or `/handoff #N`. Parse the issue number from `$ARGUMENTS`. If none found: `Usage: greenlight #N or /handoff #N`

---

## Retry State

`retryCount = 0` at the start of every invocation — resets any prior-session state.

---

## Pipeline Steps

### Step 1 — Verify Arch Approval

```
gh issue view <N> --repo ScurvyMonkey/StorageLord --json labels,title,state
```

Require the `arch-approved` label. If absent:
```
⛔ Cannot proceed: issue #N does not have the arch-approved label.
Run /arch #N first, then greenlight once the verdict is GO.
```
Stop. If `state !== "OPEN"`: `⛔ Issue #N is already closed.` Stop.

### Step 2 — Transition to In-Dev

`/board update #N in-dev`

### Step 3 — Invoke Dev

`/dev #N`. After it returns, verify closure: `gh issue view <N> --repo ScurvyMonkey/StorageLord --json state`. If not `CLOSED`:
```
⚠️ Handoff blocked on #N — Dev did not close the issue.
Reason: /dev completed but issue #N is still open.
Action needed: Check dev output above for errors, then re-run /dev #N manually.
```
Stop.

### Step 4 — Transition to In-Test

`/board update #N in-test`

### Step 5 — Invoke Test

Check `.claude/skills/test/SKILL.md` exists (it does as of this pipeline's setup — but keep the check for resilience). Invoke `/test #N`, parse for `TEST RESULT:` per the contract below.

### Step 6 — Handle Test Result

**PASS** → proceed to Step 6.5.

**FAIL — feature failure** (changed module's own tests failed):
1. `retryCount += 1`
2. If `< 3`: `🔁 Feature test failed on #N (attempt <n>/2). Retrying dev...` — `gh issue reopen <N> --repo ScurvyMonkey/StorageLord`, `/board update #N in-dev`, return to Step 3.
3. If `>= 3`: escalate (format below), `/board update #N blocked`, stop.

**FAIL — regression** (unrelated module failed): never auto-retry. Escalate immediately, `/board update #N blocked`, stop.

### Step 6.5 — Invoke UX Review

Invoke `/ux #N`, parse for `UX RESULT:`.

**PASS** → Step 7.

**FAIL**: same retry counter as Step 6 (total dev attempts capped at 3 across test+ux combined).
1. `retryCount += 1`
2. If `< 3`: `🔁 UX review failed on #N (attempt <n>/2). Retrying dev...` — reopen, `/board update #N in-dev`, return to Step 3.
3. If `>= 3`: escalate, `/board update #N blocked`, stop.

### Step 7 — Ship

1. `/board close #N <one-line summary of what shipped>`
2. Notify:
```
✅ Pipeline complete for #N — [Issue Title]

What shipped: [from /dev completion report]
Test result: PASS
UX review: PASS
Board: issue closed and marked shipped.

Next up: run /triage to pick the next issue, or greenlight #N manually.
```

---

## Test Output Contract (normative)

**PASS:**
```
TEST RESULT: PASS
```

**FAIL:**
```
TEST RESULT: FAIL
FAILURE TYPE: feature | regression
FAILED MODULES: [comma-separated]
DETAILS:
[failure output]
```

No `TEST RESULT:` line found → treat as FAIL/regression, escalate: `"/test did not emit a parseable result — escalating for manual review."`

---

## UX Output Contract (normative)

**PASS:**
```
UX RESULT: PASS
```
(also valid: `UX RESULT: PASS (skipped — no visual surface changed)`)

**FAIL:**
```
UX RESULT: FAIL
DETAILS:
[findings]
```

No line found → treat as FAIL, escalate: `"/ux did not emit a parseable result — escalating for manual review."`

---

## Escalation Message Format

```
⚠️ Handoff blocked on #N — [Title]
Reason: [specific reason]
Last test output: [relevant lines or "N/A"]
Action needed: [concrete next step]
```

---

## Error Handling

- `gh` not authenticated → surface and stop
- Issue not found → surface `gh` error verbatim, stop
- `/board` invocation fails → surface error, stop, note board state may be inconsistent
- `/dev` fails before closing → caught by Step 3's completion check
- `/ux` fails to launch Unity or crashes mid-review → its own error handling emits `UX RESULT: FAIL`; treat like any other UX FAIL, never a silent PASS
