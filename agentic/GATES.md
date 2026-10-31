# Gates

| Gate | Criteria | Evidence |
|---|---|---|
| G0 Intake | goal + scope + success criteria stated | intake note |
| G1 Plan | spec + plan approved | files in `templates/` form |
| G2 Code | smallest change; callers updated; coordinator did not edit | diff + batch brief |
| G3 Review | Independent reviewers PASS per tier in `agentic/SUBAGENT_CONTRACT.md` (>=3 for normal/high-risk; >=1 only for trivial per fast-path). Must satisfy independence rules. No Critical findings. | review files matching required count, with models used and verdicts |
| G4 Verify | build + tests + selftest green as applicable | command output |
| G5 Merge | all above passed; no Critical findings remain | merge commit + evidence chain |

## Evidence required

Each gate needs concrete evidence (command output, diff, review verdict).
A gate with empty assertions does not count.