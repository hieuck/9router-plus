# Gates

| Gate | Criteria | Evidence |
|---|---|---|
| G0 Intake | goal + scope + success criteria stated | intake note |
| G1 Plan | spec + plan approved | files in `templates/` form |
| G2 Code | smallest change; callers updated | diff |
| G3 Review | 3 independent reviewers PASS | 3 review files |
| G4 Verify | build + tests + selftest green | command output |
| G5 Merge | all above; no Critical findings | merge commit |

## Evidence required

Each gate needs concrete evidence (command output, diff, review verdict).
A gate with empty assertions does not count.