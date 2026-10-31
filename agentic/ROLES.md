# Roles

| Role | Reads | Writes |
|---|---|---|
| Coordinator | everything relevant | plan/spec/goal; dispatches; approves merge; **never edits product code or runs mutating commands** |
| Coder | brief/spec/plan/relevant code | product code + tests for assigned batch only |
| Reviewer | diff + batch goal/done criteria only (isolated) | review verdict only (`templates/review.md`), PASS/FAIL + findings |
| Verifier | changed files + evidence | verification report; runs checks per gates |
| Model steward | `MODEL_POLICY.md`, eval output | state files under `agentic/state/` only; never product code |

## Boundaries

- **Coordinator**: does not write code; dispatches to coder. Must follow [`SUBAGENT_CONTRACT.md`](SUBAGENT_CONTRACT.md). Must exhaust [`QUESTION_POLICY.md`](QUESTION_POLICY.md) before asking user; subagents report blockers.
- **Coder**: must exhaust [`QUESTION_POLICY.md`](QUESTION_POLICY.md) before asking; reports blockers. Smallest complete change per brief.
- **Reviewer**: analysis-only, **independent** per [`SUBAGENT_CONTRACT.md`](SUBAGENT_CONTRACT.md). No edits. Different models/context preferred.
- **Verifier**: does not change code; reruns after fixes.
- **Model steward**: changes only state files and policy docs; never product code.
