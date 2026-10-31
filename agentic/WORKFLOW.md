# Workflow

## Core Rule

**Coordinator coordinates only.** No file edits, no mutating commands, no state changes. All code changes go through subagents per [`SUBAGENT_CONTRACT.md`](SUBAGENT_CONTRACT.md).

## Flow

`intake → plan → batch → coder → reviewers (independent) → verifier → merge`

1. **Intake** - Coordinator restates goal, scope, success criteria. Identify affected areas. No code.
2. **Plan** - Write/use `templates/spec.md` and `templates/plan.md` as appropriate. Get approval if non-trivial.
3. **Batch** - Define batch per contract (smallest testable unit). Pick review tier.
4. **Coder** - Dispatch coder subagent with brief. Implements smallest complete change.
5. **Reviewers (independent)** - Dispatch per tier. Each returns PASS/FAIL via `templates/review.md`. Must satisfy independence rules.
6. **Verifier** - Collect evidence, run gates `G0–G5` (see [`GATES.md`](GATES.md)).
7. **Merge** - Only when all gates pass and no Critical findings remain.

## Roles

See [`ROLES.md`](ROLES.md).

## Batches & Review Tiers

See [`SUBAGENT_CONTRACT.md`](SUBAGENT_CONTRACT.md) for batch definition, independence rules, and tiering (trivial/normal/high-risk).

## Gates

See [`GATES.md`](GATES.md).

## Question Protocol

Self-answer first (codegraph/rg/docs/git/tests/logs/probes). Reversible → pick safe default, record in `agentic/state/assumptions.md`, continue. User-only → one question with options + recommended default. See [`QUESTION_POLICY.md`](QUESTION_POLICY.md).

**No user intervention in G1–G4** unless in escalation list below.

## Escalation (only these)

- Scope change beyond approved plan
- Credentials / secrets needed
- Destructive or irreversible action
- New dependency
- Paid model / budget
- External contract / legal
- 3-round review failure (rethink approach, not patch)

## References

- [`SUBAGENT_CONTRACT.md`](SUBAGENT_CONTRACT.md) - Delegation, independence, batches, tiers
- [`ROLES.md`](ROLES.md) - Role responsibilities
- [`PROBES.md`](PROBES.md) - Model eval probes, Kiro pitfalls
- [`MODEL_POLICY.md`](MODEL_POLICY.md) - Model selection & budgets
- [`QUESTION_POLICY.md`](QUESTION_POLICY.md) - Question/escalation rules
- [`GATES.md`](GATES.md) - Gates
