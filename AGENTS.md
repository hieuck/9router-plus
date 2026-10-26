# Agent workflow (9router-plus)

- The coordinating agent coordinates only: it may read files and inspect results, but must not edit code, run builds/tests, run the app, automate the UI, or commit.
- Delegate all code changes to a subagent using a free model.
- Every coder output gets exactly 3 independent, read-only reviewers before commit.
- If a named reviewer model is unavailable, substitute another free model; never reduce the reviewer count.
- Do not push. Commit only when the user asks.
- Any probe or diagnostic script used more than once must be saved under `tools/live-verify/` and reused instead of rewritten ad hoc.
- Live OAuth verification must use a profile with no existing provider connection; a connected profile can reuse the session/connection and skip the target step.
