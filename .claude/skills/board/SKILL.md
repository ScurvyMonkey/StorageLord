---
name: board
description: GitHub Issues board manager for Storage Lord — manages pipeline-stage labels, audits stale issues, posts board summaries. Invoke as /board update|clean|summary|close.
---

# Storage Lord — GitHub Board Manager

You keep the GitHub Issues board accurate through the pipeline — label transitions, stale-issue auditing, board summaries. You use the `gh` CLI exclusively. You do NOT modify app code or skill files.

---

## Your Request

$ARGUMENTS

---

## Repo

`ScurvyMonkey/StorageLord`

---

## Pipeline Label Set

Remove all of these before applying a new one — safer than tracking a fixed predecessor:

```
spec-approved
arch-approved
arch-blocked
in-dev
in-test
test-failed
ux-failed
shipped
blocked
stale
```

**Phase labels** (`phase-1`, `phase-2`) are applied by `/ba` at creation and are NOT part of the pipeline-stage set above — never remove or transition these; they describe which roadmap phase the issue belongs to, independent of where it is in the pipeline.

---

## Commands

Parse `$ARGUMENTS`. If empty/unrecognized, print the command reference below.

### `/board update #N <stage>`

Valid stages: `spec-approved`, `arch-approved`, `arch-blocked`, `in-dev`, `in-test`, `test-failed`, `ux-failed`, `shipped`, `blocked`, `stale`.

```
gh issue edit <N> --repo ScurvyMonkey/StorageLord \
  --remove-label "spec-approved,arch-approved,arch-blocked,in-dev,in-test,test-failed,ux-failed,shipped,blocked,stale"
gh issue edit <N> --repo ScurvyMonkey/StorageLord --add-label "<stage>"
```

Confirm: `✓ Issue #N transitioned to <stage>.`

### `/board clean`

Flag open issues with no activity in 30+ days:

```
gh issue list --repo ScurvyMonkey/StorageLord --state open --limit 200 --json number,title,updatedAt,labels
```

For each stale one:
```
gh issue comment <N> --repo ScurvyMonkey/StorageLord --body "🕰️ This issue has had no activity for 30+ days. Is it still relevant? Please close, update, or label it — otherwise it will be marked stale."
gh issue edit <N> --repo ScurvyMonkey/StorageLord --add-label "stale"
```

Report every issue flagged with number, title, last-updated date. If none: "No stale issues found."

### `/board summary`

```
gh issue list --repo ScurvyMonkey/StorageLord --state open --limit 200 --json number,title,labels,milestone
```

Group by pipeline stage, most urgent first (`blocked` → `test-failed` → `ux-failed` → `in-test` → `in-dev` → `arch-approved` → `arch-blocked` → `spec-approved` → no pipeline label (backlog) → `stale`). Within each group, note the phase label if present (e.g. `[phase-1]`). Output as a markdown table per stage, omitting empty sections:

```
## Board Summary — <today's date>

### 🔴 Blocked
| # | Title | Phase |
|---|---|---|
| #N | [title] | phase-1 |

### 📥 Backlog (no stage label)
...
```

### `/board close #N <summary>`

```
gh issue comment <N> --repo ScurvyMonkey/StorageLord --body "✅ Shipped.\n\n<summary>"
gh issue edit <N> --repo ScurvyMonkey/StorageLord \
  --remove-label "spec-approved,arch-approved,arch-blocked,in-dev,in-test,test-failed,ux-failed,blocked,stale" \
  --add-label "shipped"
gh issue close <N> --repo ScurvyMonkey/StorageLord
```

Confirm: `✓ Issue #N closed and marked shipped.`

---

## Command Reference (shown when $ARGUMENTS is empty)

```
/board update #N <stage>   — Transition issue to a pipeline stage
/board clean               — Flag issues with no activity for 30+ days
/board summary              — Show all open issues grouped by stage
/board close #N <summary>  — Close issue with completion comment + shipped label

Valid stages: spec-approved | arch-approved | arch-blocked | in-dev | in-test | test-failed | ux-failed | shipped | blocked | stale
```

---

## Error Handling

- `gh` not authenticated → `gh CLI not authenticated. Run: gh auth login`
- Issue not found → surface the `gh` error verbatim
- Label removal is best-effort — `gh issue edit --remove-label` silently skips absent labels

---

## Calling Contract for `/handoff`

`/handoff` makes exactly two calls to `/board`: `/board update #N <stage>` at each pipeline step, and `/board close #N <summary>` after test+UX PASS. Don't change these two commands' behavior without updating `/handoff` accordingly.
