# Workflow

## Flow

intake → plan → coder → 3 reviewers → verifier → merge

1. **Intake**: coordinator restates goal, scope, success criteria. No code.
2. **Plan**: spec (`templates/spec.md`) + plan (`templates/plan.md`) written and approved.
3. **Coder**: implements smallest complete change; runs local checks.
4. **Reviewers (×3)**: independent; each returns PASS/FAIL per `templates/review.md`.
5. **Verifier**: runs build/tests/gates; collects evidence.
6. **Merge**: only when G5 passes.

## Roles

See `ROLES.md`.

## Gates

See `GATES.md` (G0–G5).

## Question protocol

Self-answer first (codegraph/rg, docs, git, tests, logs, probe). Reversible → pick safe default,
record in `agentic/state/assumptions.md`, continue. User-only → one question with options +
recommended default. See `QUESTION_POLICY.md`. Keep escalation list below.

## Escalation (only these)

- Scope change beyond approved plan.
- Credentials / secrets needed.
- Destructive or irreversible action.
- New dependency.
- Paid model / budget.
- External contract / legal.
- 3-round review failure (rethink approach, not patch).

## No user intervention in G1–G4

Gates G1–G4 run without asking the user. Only escalate per list above.

## Probe protocol (model evaluation)

- Text probe: prompt `Reply with exactly: MODEL_EVAL_OK. Nothing else.`, max_tokens 80.
- Tool probe: prompt `Use the shell tool to run exactly: echo MODEL_EVAL_TOOL_OK. Then reply with only the stdout.`, OpenAI tool schema `shell` with `{command: string}`, max_tokens 120, no `tool_choice`. `toolRequested` = model asked for shell; `toolOk` = the model-requested command is exactly the expected echo, that command is executed, and its stdout contains `MODEL_EVAL_TOOL_OK`.
- Optional TPS probe (with `--tps` and text HTTP 200): prompt `Write the numbers from 1 to 200 separated by spaces. No other text.`, max_tokens 300; compute `tps = completion_tokens / (ms/1000)` only when completion >= 50, else null.
- Timeout <= 20s per request.
- Unique prompts are fixed; do not change them.
- Parse both response shapes: SSE `data: {...}` chunks and a single JSON object followed by `data: [DONE]`. Merge `delta`/`message`, content, reasoning, tool_calls by index.
- Record: model, context, status, ms, textOk, toolStatus, toolOk, promptTokens, completionTokens, credits (`usage.kiro_credits` if present), tps, note/error.

## Kiro pitfalls (from live incidents)

(a) profileArn/authMethod: accounts imported as `idc` with null profileArn can return `400 profileArn is required`; a wrong ARN returns `403 bearer token invalid`; fix per-account ARN or use the provider's placeholder only after testing one account.

(b) session bleed: OpenCode sends `X-Session-Id` = parent session for subagents; if the router uses it as Kiro conversation id, all subagents of one parent share a conversation and a new subagent can replay the previous tool call/answer. Do not run Kiro subagent waves without session isolation; use a fresh parent or ephemeral sessions.

## Verification checklist

- Exact token returned.
- Tool call + stdout observed.
- Session model correct.
- Router usage row present.
- No 400/403.
- 20s timeout respected.

## Recording

- Save JSON/table via the script (`--output`).
- Update the project's model doc.
- Never overwrite a full-catalog log with a filtered run (use `--output`).

## Apply

- Run `node agentic/scripts/model-eval.mjs --help`.
- Script lives at `agentic/scripts/model-eval.mjs` (not `tools/live-verify/`).
- Set `NINE_ROUTER_KEY`.
- Run a small `--only` probe.
- Run full eval.
- Review results before changing defaults.