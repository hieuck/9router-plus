# Dev-team invocations

This directory stores evidence that `dev-team` was invoked before mutating work or non-trivial decisions.

## Required evidence
- `dev-team-session-YYYY-MM-DD-HHMMSS.md` containing:
  - Topic (one paragraph)
  - Classification (trivial/normal/high-risk per SUBAGENT_CONTRACT.md)
  - Dev-team invocation (session ID / models used)
  - Verdict (APPROVE/NEEDS_FIXES/REJECT)
  - Rationale for trivial exemptions (if applicable)
  - Coordinator sign-off

## Enforcement
- Without a logged invocation, delegation is blocked (G0 gate).
- Read-only inspection is exempt (no log required).
- Trivial exemptions must be documented and auditable.