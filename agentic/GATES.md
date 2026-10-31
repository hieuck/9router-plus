# Gates

| Gate | Criteria | Evidence |
|---|---|---|
| G0 Intake | goal + scope + success criteria stated. **Dev-team invoked and logged** (read-only inspection exempt). | intake note + dev-team invocation log in `agentic/state/dev-team-invocations/` |
| G1 Plan | spec + plan approved | files in `templates/` form |
| G2 Code | smallest change; callers updated; coordinator did not edit | diff + batch brief |
| G3 Review | Independent reviewers PASS per tier in `agentic/SUBAGENT_CONTRACT.md` (>=3 for normal/high-risk; >=1 only for trivial per fast-path). Must satisfy independence rules. No Critical findings. | review files matching required count, with models used, sessions, verdicts, rationale |
| G4 Verify | build + tests + selftest green as applicable | command output |
| G5 Merge | all above passed; no Critical findings remain. **If changing `AGENTS.md` or `agentic/*.md`:** must have `dev-team` meta-review (>=3 independent, high-risk) saved under `agentic/state/meta-reviews/` with full evidence, or explicit trivial exemption log. | merge commit + evidence chain including meta-review if applicable |

## Evidence required

Each gate needs concrete evidence (command output, diff, review verdict).
A gate with empty assertions does not count.