# Roles

| Role | Reads | Writes |
|---|---|---|
| Coordinator | everything | plan/spec/goal; dispatches; approves merge; never edits product code |
| Coder | spec, plan, relevant code | product code + tests for assigned task only |
| Reviewer ×3 | diff + context | review verdict only (`templates/review.md`) |
| Verifier | changed files + CI | verification report; runs commands |
| Model steward | `MODEL_POLICY.md`, eval output | state files under `agentic/state/` |

## Boundaries

- Coordinator does not write code; dispatches to coder.
- Reviewer does not edit code; reports only.
- Verifier does not change code; reruns after fixes.
- Coordinator: must exhaust `QUESTION_POLICY.md` checklist before asking; subagents report blockers instead of asking.
- Coder: must exhaust `QUESTION_POLICY.md` checklist before asking; subagents report blockers instead of asking.
- Model steward changes only state files and policy docs; never product code.