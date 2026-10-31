# Probes & Model Evaluation

This document contains the model evaluation probe protocol and operational notes. It was moved from `WORKFLOW.md` to keep the workflow focused on day-to-day execution.

## Probe protocol (model evaluation)

- **Text probe:** prompt `Reply with exactly: MODEL_EVAL_OK. Nothing else.`, max_tokens 80.
- **Tool probe:** prompt `Use the shell tool to run exactly: echo MODEL_EVAL_TOOL_OK. Then reply with only the stdout.`, OpenAI tool schema `shell` with `{command: string}`, max_tokens 120, no `tool_choice`. `toolRequested` = model asked for shell; `toolOk` = the model-requested command is exactly the expected echo, that command is executed, and its stdout contains `MODEL_EVAL_TOOL_OK`.
- **Optional TPS probe (with `--tps`):** prompt `Write the numbers from 1 to 200 separated by spaces. No other text.`, max_tokens 300; compute `tps = completion_tokens / (ms/1000)` only when completion >= 50, else null.
- **Timeout:** <= 20s per request.
- **Prompts:** Unique prompts are fixed; do not change them.
- **Parsing:** Parse both response shapes: SSE `data: {...}` chunks and a single JSON object followed by `data: [DONE]`. Merge `delta`/`message`, content, reasoning, tool_calls by index.
- **Record:** model, context, status, ms, textOk, toolStatus, toolOk, promptTokens, completionTokens, credits (`usage.kiro_credits` if present), tps, note/error.

## Kiro pitfalls (from live incidents)

(a) **profileArn/authMethod:** accounts imported as `idc` with null profileArn can return `400 profileArn is required`; a wrong ARN returns `403 bearer token invalid`; fix per-account ARN or use the provider's placeholder only after testing one account.

(b) **session bleed:** OpenCode sends `X-Session-Id` = parent session for subagents; if the router uses it as Kiro conversation id, all subagents of one parent share a conversation and a new subagent can replay the previous tool call/answer. **Do not run Kiro subagent waves without session isolation; use a fresh parent or ephemeral sessions.**

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
