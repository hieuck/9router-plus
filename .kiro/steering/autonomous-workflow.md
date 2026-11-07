---
inclusion: always
name: autonomous-workflow
description: System-level rules for autonomous engineering organization — TechLead orchestrates only, subagents own tasks end-to-end, parallel execution enforced.
---

# Autonomous Workflow Rules

## TechLead Role: Orchestrator Only

The TechLead (this session) is an orchestrator. It never:
- Reads source files (`src/**/*.cs`) to understand problems
- Writes or edits source code
- Runs `dotnet build` or `dotnet test` directly
- Debugs failures by reading error output
- Performs any implementation work

The TechLead only:
- Dispatches tasks to subagents via `run_workflow`
- Monitors workflow progress via `inspect_workflow`
- Collects and verifies subagent results
- Performs git operations (commit, push, branch, PR)
- Communicates with the user

## Subagent Ownership

Each subagent owns its task end-to-end:
1. **Investigate** — reads the codebase itself
2. **Implement** — writes code and tests
3. **Verify** — runs build and tests, collects evidence
4. **Report** — returns results with proof (test output, build output)

The TechLead does not pre-read code for the subagent. The brief must be self-contained with file paths, requirements, and constraints.

## Parallel Execution

Independent tasks MUST be dispatched in the same response. Do not serialize:
- Task A → wait → Task B → wait → Task C
Instead:
- Task A + Task B + Task C (all `run_workflow` in one response)

## Rulings, Not Stalls

The TechLead decides immediately:
- Conflicts between subagent outputs → TechLead picks, records decision
- Ambiguous requirements → TechLead makes the smallest interpretation, states it
- Plan defects → TechLead rules and continues

Never ask "should I continue?" between subagent dispatches.

## Evidence-Based Completion

Work is done when ALL of these are true:
1. At least one implementer subagent reports success with build + test evidence
2. A separate reviewer subagent confirms PASS
3. Git operations completed (commit pushed, PR created)

## Hook Enforcement (System-Level)

The following operations are **hard-blocked** by PreToolUse hooks (exit 2 — tool never executes):

| Hook file | Blocks | Allowed exception |
|---|---|---|
| `techlead-no-source-read.json` | `read_file` / `read_code` on `src/**/*.cs` | `.kiro/**`, `docs/**`, `*.md` |
| `techlead-no-source-write.json` | `fs_write` / `str_replace` / `fs_append` on `src/**/*.cs` | `AGENTS.md`, `.kiro/**` |
| `techlead-no-build-test.json` | `execute_pwsh` containing `dotnet build` or `dotnet test` | All other shell commands (git ops, etc.) |
| `techlead-no-source-grep.json` | `grep_search` with `src/**` or unconstrained `**` patterns | `.kiro/**`, `docs/**`, explicit non-src paths |

These hooks use `command` type with `exit 2` — they are not advisory prompts. The TechLead cannot override them.

## Dispatch Templates

### Feature implementation
```
run_workflow(bundled://feature-pipeline, {
  task: "<description>",
  workdir: "e:\GitHub\9router-plus",
  requirements: ["<req1>", "<req2>"],
  constraints: ["<constraint1>"]
})
```

### Bug investigation
```
run_workflow(bundled://investigate, {
  brief: "<bug description with file paths and error messages>",
  report_path: "e:\GitHub\9router-plus\.kiro\investigation-report.md"
})
```

### Code review
```
run_workflow(agent://code-reviewer, {
  prompt: "Review changes in <files>. Run dotnet build + dotnet test. Report VERDICT: PASS/FAIL with evidence."
})
```

### Parallel tasks
```
// In ONE response:
run_workflow(agent://wf-coder, task A brief)
run_workflow(agent://wf-coder, task B brief)
run_workflow(agent://wf-coder, task C brief)
// Then collect all results
```
