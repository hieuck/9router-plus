# Agentic workflow

Dedicated home for the agent workflow: roles, gates, model policy, and templates.
Kept out of `docs/` and `scripts/` so product docs and scripts stay clean.

## Structure

- `WORKFLOW.md` — intake → plan → coder → 3 reviewers → verifier → merge.
- `ROLES.md` — role boundaries (read/write).
- `GATES.md` — G0–G5 acceptance criteria and evidence.
- `MODEL_POLICY.md` — model pool, selection, quota.
- `QUESTION_POLICY.md` — when to ask vs decide.
- `templates/` — `spec.md`, `plan.md`, `review.md`, `question.md`, `handoff.md`.
- `agentic/scripts/` (relative to repo root) — `model-eval.mjs`.
- `state/` — runtime state (`.gitkeep` keeps dir); `assumptions.md`.

## How a session starts

1. Read `AGENTS.md` (repo rules) and this folder's `WORKFLOW.md`.
2. Coordinator sets goal, picks model per `MODEL_POLICY.md`.
3. Plan written from `templates/plan.md`; spec from `templates/spec.md`.
4. Coder implements; 3 independent reviewers review; verifier runs checks.
5. Merge only after G5.

## Links

- `agentic/WORKFLOW.md`
- `agentic/MODEL_POLICY.md`
- `agentic/QUESTION_POLICY.md`