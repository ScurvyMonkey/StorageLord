---
name: triage
description: Scores and ranks the Storage Lord backlog by value/effort. Advisory only — never starts implementation. Invoke as /triage or /triage --label.
disable-model-invocation: true
---

# Storage Lord — Triage Agent

You read the open backlog, score each issue by value and effort, and present a ranked list. You **read and score issues only** — you do NOT start implementation or modify app code.

---

## Your Request

$ARGUMENTS

---

## Repo

`ScurvyMonkey/StorageLord`

---

## Invocation

```
/triage
/triage --label
```

No argument: score and rank, don't apply labels. `--label`: also apply `priority-1`/`priority-2`/`priority-3` labels.

---

## Process

### Step 1 — Fetch Eligible Issues

```
gh issue list --repo ScurvyMonkey/StorageLord --state open --limit 100 \
  --json number,title,labels,body,createdAt,updatedAt
```

Include: `spec-approved`, `arch-approved`, `feature`, `enhancement`, `bug`, `tech-debt`. Exclude: `stale`, `blocked`, `in-dev`, `in-test`, `wontfix`.

### Step 2 — Score Each Issue

**Value**

| Condition | Score |
|---|---|
| Label `bug` | 40 |
| Label `feature` | 30 |
| Label `enhancement` | 20 |
| Label `tech-debt` | 10 |
| Label `arch-approved` | +10 bonus |
| Label `spec-approved` | +5 bonus |
| Label `phase-1` | +15 bonus — current-phase work should surface over later-phase work by default |
| Issue age > 30 days | +5 bonus |

**Effort** (from body length): < 300 words → Low (1) · 300–700 → Medium (2) · > 700 → High (3)

**Priority Score = Value / Effort.** Sort descending. Ties: bugs first, then older issues first.

### Step 3 — Assign Tiers

Top third → Priority 1, middle third → Priority 2, bottom third → Priority 3. Round up for uneven counts.

### Step 4 — Present Ranked List

```
## Backlog Triage — [date]

**N issues scored**

### Priority 1 — Start Here
| # | Title | Type | Phase | Effort | Score |
|---|---|---|---|---|---|
| #N | [title] | bug | phase-1 | Low | 55.0 |

### Priority 2 — Next
...

### Priority 3 — Later
...

---
To start the top issue: `greenlight #N`
To override this ranking: reply with the issue number you want next before running /handoff.
To apply these as GitHub labels: /triage --label
```

### Step 5 — Apply Labels (only with `--label`)

```
gh issue edit <N> --repo ScurvyMonkey/StorageLord \
  --remove-label "priority-1,priority-2,priority-3" \
  --add-label "priority-1"
```

Add to the summary: `Labels applied: priority-1 (N), priority-2 (N), priority-3 (N)`

---

## Edge Cases

- No eligible issues: `No issues are ready for triage. All open issues are either in-flight, blocked, or not yet specced.`
- Single issue: skip ranking, report it, recommend greenlighting directly.
- `gh` not authenticated: `Run gh auth login, then re-run /triage.`
- Missing issue body: treat as Low effort — flag as a spec gap.

---

## Notes

- This skill never starts work — the user always confirms before `/handoff` begins.
- The `phase-1` bonus is deliberate: this project is explicitly sequenced (see CLAUDE.md Roadmap), and triage should reinforce that ordering by default rather than let a shiny Phase 2 idea jump the queue while the core loop is still incomplete.
- Re-run any time the backlog changes significantly.
