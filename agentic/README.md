# Agentic workflow

Dedicated home for the agent workflow: roles, gates, model policy, and templates.
Kept out of `docs/` and `scripts/` so product docs and scripts stay clean.

## Structure

- [`WORKFLOW.md`](WORKFLOW.md) — flow, gates, escalation
- [`SUBAGENT_CONTRACT.md`](SUBAGENT_CONTRACT.md) — delegation, batches, independence, review tiers
- [`ROLES.md`](ROLES.md) — role boundaries (read/write)
- [`GATES.md`](GATES.md) — G0–G5 acceptance criteria and evidence
- [`MODEL_POLICY.md`](MODEL_POLICY.md) — model pool, selection, quota
- [`QUESTION_POLICY.md`](QUESTION_POLICY.md) — when to ask vs decide
- [`PROBES.md`](PROBES.md) — model eval probes, Kiro pitfalls
- [`templates/`](templates/) — `spec.md`, `plan.md`, `review.md`, `question.md`, `handoff.md`
- [`scripts/`](scripts/) — `model-eval.mjs`
- [`state/`](state/) — runtime state (`.gitkeep` keeps dir); `assumptions.md`

## How a session starts

1. Read `AGENTS.md` (repo rules) and [`WORKFLOW.md`](WORKFLOW.md).
2. Coordinator sets goal, picks model per [`MODEL_POLICY.md`](MODEL_POLICY.md).
3. Follow [`SUBAGENT_CONTRACT.md`](SUBAGENT_CONTRACT.md) for batching/delegation/independence.
4. Plan written from `templates/plan.md`; spec from `templates/spec.md` as appropriate.
5. Coder implements via subagent; independent reviewers per tier; verifier runs checks.
6. Merge only after G5.

## Links

- [`agentic/WORKFLOW.md`](WORKFLOW.md)
- [`agentic/SUBAGENT_CONTRACT.md`](SUBAGENT_CONTRACT.md)
- [`agentic/MODEL_POLICY.md`](MODEL_POLICY.md)
- [`agentic/QUESTION_POLICY.md`](QUESTION_POLICY.md)
- [`agentic/PROBES.md`](PROBES.md)
