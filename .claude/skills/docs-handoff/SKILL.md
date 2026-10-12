---
name: docs-handoff
description: XrealScreen documentation and AI-handoff rules. Use at the start and end of every task, and whenever you update docs/STATUS.md, write or supersede an ADR, record a finding, add an external source, or commit a completed step.
---

# Docs handoff

Goal: any agent can resume the project from the repo alone. Docs are short, current and tagged.

## Start of task
1. Read `docs/STATUS.md` (milestone, next steps, blockers).
2. Read the ADRs and findings relevant to your area (see your agent file under `.claude/agents/`).

## End of task: update `docs/STATUS.md`
- **Current:** milestone and date.
- **Done:** tick completed items; remove items from finished milestones (ROADMAP keeps the record).
- **Next 3 steps:** concrete and ordered.
- **Blockers:** what is needed, from whom.
- **Last hardware verification:** date / device / firmware / what was verified. Leave as is if no hardware was used.
- **Session log:** add one row `| YYYY-MM-DD | agent | one-line summary |`; keep ~10 newest rows.

Tick `docs/ROADMAP.md` exit criteria only when there is evidence (test, recording, measured number).

## Writing an ADR
1. Copy the template from `docs/decisions/README.md` to `docs/decisions/ADR-NNNN-short-title.md` (next free number).
2. Sections: Status, Date, Context, Decision, Consequences, Alternatives. Decision first sentence = the decision.
3. Add a row to the index in `docs/decisions/README.md`.
4. Replacing a decision: new ADR; set the old one to `Superseded by ADR-NNNN` (do not rewrite its body); update the index.

## Findings and tags
Every factual claim in `docs/findings/*` carries one tag:

| Tag | Use when |
|-----|----------|
| `[verified-hw]` | Observed on our glasses (add date, device, firmware) |
| `[verified-local]` | Observed on the dev PC |
| `[from-source:MIT]` | Read in a permissive source listed in SOURCES.md |
| `[from-GPL:facts-only]` | ID/constant from a GPL source; no code |
| `[from-sdk:facts-only]` | Fact from the XREAL SDK |
| `[hypothesis]` | Not verified |

- Confirmed or refuted? **Edit the line in place** (change tag, add date). Never append a contradicting note below the old one.
- New file? Add it to `docs/findings/README.md`.

## External sources
Add every new source to `docs/legal/SOURCES.md`: name, URL, license, how used (`facts` / `reference` / `code-ported` / `imported`), date. Code ported from MIT/Apache sources also goes in `THIRD-PARTY-NOTICES.md`. GPL, XREAL SDK, VertoXR: facts only, never code (ADR-0006).

## Pruning
- Delete notes that are superseded, duplicated or describe removed code.
- Prefer tables and bullets; no narrative history (git has it).
- Keep each doc focused on current truth.

## Commits (when the user asked you to commit)
- On a feature branch, never `main`; push and give the PR link (skill `git-branch-pr`).
- One commit per completed step, small and self-describing: `M1: add IMU frame parser with golden tests`.
- STATUS.md update is part of the same commit.
- Do not commit secrets, captured frames or large binaries (fixtures < 5 MB each).
