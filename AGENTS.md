# Agent workflow (9router-plus)

- The coordinating agent coordinates only: it may read files and inspect results, but must not edit code, run builds/tests, run the app, automate the UI, or commit.
- Delegate all code changes to a subagent using an appropriate model per `agentic/MODEL_POLICY.md`.
- Every coder output must go through independent, read-only reviews before commit. Review count and rules follow `agentic/SUBAGENT_CONTRACT.md` (tiers: trivial/normal/high-risk). For **normal/high-risk**, minimum **3** independent reviewers. For **trivial**, follow the fast-path rules strictly.
- Reviewers MUST be independent: different models when possible, isolated context, no shared verdicts/rationale. See `agentic/SUBAGENT_CONTRACT.md`.
- If a preferred reviewer model is unavailable, substitute another model; **never** reduce below the minimum required by the batch tier.
- Do not push. Commit only when the user asks.
- Any probe or diagnostic script used more than once must be saved under `tools/live-verify/` and reused instead of rewritten ad hoc.
- Live OAuth verification must use a profile with no existing provider connection; a connected profile can reuse the session/connection and skip the target step.
- Agentic workflow: `agentic/WORKFLOW.md`, contract: `agentic/SUBAGENT_CONTRACT.md`, roles: `agentic/ROLES.md`, gates: `agentic/GATES.md`, model policy: `agentic/MODEL_POLICY.md`, question policy: `agentic/QUESTION_POLICY.md`, probes: `agentic/PROBES.md`, script: `agentic/scripts/model-eval.mjs` (exception: not under `tools/live-verify/`).
