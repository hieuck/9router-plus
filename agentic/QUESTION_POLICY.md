# Question policy

Default: **decide, don't ask** for technical/repo questions.

## Self-answer first (in order)

1. `codegraph` / `rg` (symbols, callers, files).
2. `AGENTS.md`, `agentic/`, `docs/`, `README`.
3. `git log` / `git blame`.
4. Tests / build commands (`pnpm verify`, `pnpm test`).
5. Logs / state (`logs/`, `agentic/state/`).
6. Run a probe (`scripts/probe.mjs`, `cdp.mjs`).

## Reversible decisions

If reversible: pick a safe default, record the assumption in
`agentic/state/assumptions.md`, continue. Do not stop.

## User-only decisions (the only things you may ask)

- Product scope / acceptance conflict.
- Credentials / secrets.
- Paid model / budget.
- Destructive or irreversible action.
- New dependency.
- External contract / legal.
- 3-round review failure (rethink approach).

## Before asking: checklist

Fill all of these in the question:

- Sources checked (list).
- Why this is user-only (which category).
- Reversible? (yes/no + why).
- Recommended default.
- What happens if no answer.

## Asking rules

- One question at a time.
- Include options + recommended default.
- Use `templates/question.md`.

## Anti-patterns (do not do)

- Asking which file when search can find it.
- Asking style / naming preferences.
- Asking for info already in docs.
- Asking permission for safe read-only actions.
- Asking multiple questions at once.

## Enforcement

- No question in G1–G4 unless it is user-only.
- Subagents must not ask; they report a blocker to the coordinator.
- Reviewers flag unnecessary questions (see `templates/review.md`).