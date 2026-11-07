# AGENTS.md — RouterPlus workflow rules

`AGENTS.md` is the sole authority for workflow in this repo.

---

## Project snapshot

RouterPlus is a Windows WPF desktop companion for 9Router (C# .NET 8, `net8.0-windows`).
Key libraries: `RouterPlus.Core`, `RouterPlus.Infrastructure`, `RouterPlus.App`, `RouterPlus.Updater`.
Tests: xUnit + Moq in `tests/` (6 projects). Build: `scripts/build.ps1`. CI: `.github/workflows/ci.yml`.

Read `PROJECT-CONTEXT.md` for the long-term objective before starting any work.

---

## Session start

Before the first code change in a session, verify the build is green:

```
cd e:\GitHub\9router-plus
dotnet build RouterPlus.sln
```

A red build is a blocker — diagnose before writing new code.

---

## Workflow phases

Work flows through four phases in order:

| Phase | What happens | Proof required |
|---|---|---|
| **Kickoff** | Understand the objective; read relevant source; identify risks | Written summary of findings |
| **Ready** | One objective, one failing test (red), allowed files listed | Red test exists and fails |
| **Implement** | Write minimal code to make the test green; keep 80 %+ coverage | `dotnet test` green |
| **Close** | Commit on a `feat/*` or `fix/*` branch; push; open PR | PR description cites test evidence |

Never skip phases. Mechanical doc/config edits may skip Kickoff.

**Commit at every phase boundary.** Before moving to the next phase — and whenever a meaningful, self-contained unit of work is complete — commit what you have. Never carry uncommitted changes across a phase boundary. A WIP commit is acceptable to checkpoint progress, but it must be squashed or cleaned up before the PR is opened.

---

## Skills

Before making any code or documentation change, check the available skills with `disclose_context` and load the one that best matches the task. The full skill catalog is visible in the Kiro skill system — do not assume the list is limited to any examples here.

**How to pick:** match the task type to the skill name. Examples of common matches:
- Writing or fixing code → look for a `tdd-*`, `test-driven-*`, or language/framework pattern skill
- Debugging an unexpected failure → `systematic-debugging`
- Finishing a branch, committing, pushing, opening a PR → `finishing-a-development-branch`
- Planning a multi-file feature → `writing-plans`
- Code review → `requesting-code-review` or `receiving-code-review`
- Architecture or new subsystem → `agentic-engineering`
- Security-sensitive change → `security-review`
- Verification before claiming done → `verification-before-completion`

If genuinely no skill applies, state **"no skill"** explicitly.

After loading, **quote one line from the skill** to confirm it was applied — e.g.:

> *"Verify tests → Detect environment → Present options → Execute choice → Clean up."* — `finishing-a-development-branch`

Reading a skill file directly without calling `disclose_context` does **not** count.

---

## Development rules

### Build
- `dotnet build RouterPlus.sln` must exit 0 with **0 warnings** (`TreatWarningsAsErrors` is on).
- `dotnet test` must be green before any commit.
- After CI edits verify `.github/workflows/ci.yml` still passes locally: `dotnet build && dotnet test`.

### Code style
- Nullable and ImplicitUsings are enabled — no `#nullable disable`, no suppression without justification.
- Immutable patterns preferred; never mutate shared state without a lock or immutable replacement.
- No hardcoded secrets; DPAPI (`CurrentUser`) for API keys at rest.
- Functions ≤ 50 lines, files ≤ 800 lines; extract when either limit is approached.

### Tests (TDD)
- Write the red test **before** implementation code.
- Tests live in the matching project under `tests/`; use the shared `TestHelpers` project.
- Behavior-style assertions preferred (describe what the system does, not how).
- Mocks via Moq; never reach the real filesystem or registry in unit tests.
- Coverage target: **80 %+** across all test projects.

### Git

**Branching:**
- Cut branches from `main`: `feat/<topic>` for features, `fix/<topic>` for bugs.
- `main` is protected — never push directly; always go via PR.
- Small single-objective batches (≤ 3 source files) work fine on a regular branch.
  Parallel or high-risk work should use a worktree to isolate changes.

**Commit message format** (Conventional Commits):
```
<type>: <short description>

<optional body — what changed and why>
```
Types: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `perf`, `ci`.

- Test + implementation go in the **same commit**. Never commit a red test alone.
- Never `git add .` blindly — stage specific files only.
- Never `--no-verify`; never `--force` unless you own the branch exclusively.

**PR:** use `gh pr create` and fill in `.github/PULL_REQUEST_TEMPLATE.md`. Title ≤ 70 characters.

### Security
- No secrets in source. Validate all external inputs at system boundaries.
- API keys stored via DPAPI vault (`RouterPlus.Infrastructure`); never logged.
- Before any commit: confirm no credentials, tokens, or passwords are staged.

### Versioning and releases
- Versions follow semver; bump via `scripts/bump-version.ps1`.
- Release artifacts built with `scripts/build.ps1` → `scripts/package-release.ps1`.
- Self-update only installs checksum-verified (`*.sha256`) stable packages.

---

## Roles (Autonomous Engineering Organization)

### Core principle

The TechLead is an **orchestrator only**. It never reads source files, writes code, runs builds, or debugs issues directly. Every substantive task is delegated to a specialized subagent that owns it end-to-end: investigation, implementation, testing, debugging, and verification.

The TechLead's toolkit is limited to: `run_workflow`, `inspect_workflow`, `update_workflow`, `send_message`, `list_processes`, `get_process_output`, `control_pwsh_process`, `execute_pwsh` (for git operations only: commit, push, branch, PR), and `update_session_information`.

### Delegation law (system-enforced)

The following actions are **forbidden** for the TechLead and will be blocked by hooks:

| Action | Forbidden for TechLead | Delegated to |
|---|---|---|
| Read source files to understand a problem | `read_file` on `src/**/*.cs` | Workflow agent / `bundled://investigate` |
| Search codebase for patterns | `grep_search` on `src/**` | Workflow agent |
| Write or edit source code | `fs_write`, `str_replace` on `src/**/*.cs` | `run_workflow` with implementer brief |
| Run builds or tests | `execute_pwsh` with `dotnet build\|dotnet test` | Workflow agent runs verification |
| Debug failures | Reading error output to diagnose | `bundled://investigate` or `build-error-resolver` |

**Allowed for TechLead** (orchestration scope):
- `run_workflow` — launch subagent workflows
- `inspect_workflow` / `update_workflow` — monitor and manage running workflows
- `send_message` — communicate with subagents and user
- `execute_pwsh` — git operations only (`git add`, `git commit`, `git push`, `git branch`, `git checkout`, `gh pr create`)
- `update_session_information` — track progress
- `list_processes` / `get_process_output` / `control_pwsh_process` — manage background processes
- `read_file` on `AGENTS.md`, `.kiro/**`, `docs/**`, `*.md` — read config and docs only
- `fs_write` / `str_replace` on `AGENTS.md`, `.kiro/**` — edit workflow config only

### Autonomous execution rules

1. **No sequential hand-holding.** When a subagent is dispatched, it owns the task completely. The TechLead does not check in mid-task unless the subagent signals via `send_message`.

2. **Parallel execution.** Independent tasks must be dispatched simultaneously. The TechLead launches multiple `run_workflow` calls in a single response when tasks have no dependencies.

3. **Subagent ownership.** Each subagent:
   - Reads the codebase itself (the TechLead does not pre-read for it)
   - Implements the change
   - Writes and runs tests
   - Verifies the build passes
   - Reports back with evidence (test output, build output)

4. **Rulings, not stalls.** The TechLead decides conflicts and ambiguities immediately. It does not ask the user "should I continue?" between subagent dispatches.

5. **Evidence-based completion.** The TechLead marks work done only after:
   - At least one subagent reports success with build + test evidence
   - A review subagent (separate from the implementer) confirms PASS
   - Git operations (commit, push, PR) are the TechLead's last step

### Role matrix

| Role | Agent / Workflow | Owns end-to-end | Tools available |
|---|---|---|---|
| **TechLead** | *(orchestrator session)* | Scope, routing, commit, PR, user comms | `run_workflow`, `inspect_workflow`, `update_workflow`, `send_message`, git ops, session info |
| **Planner** | `planner` or `bundled://goal` | Break features into tasks; identify risks | Read-only: `read_file`, `grep_search`, `file_search` |
| **Implementer** | `bundled://feature-pipeline` or `agent://wf-coder` | Source edits, test edits, build, test | Full toolset except git push/commit/tag |
| **Test author** | `agent://tdd-guide` | Write red tests, verify they fail, hand off to implementer | Read, write, shell (test commands) |
| **Reviewer** | `agent://code-reviewer` | Independent build + test; written VERDICT | Read, shell (build/test only) |
| **Investigator** | `bundled://investigate` | Root-cause analysis, bug diagnosis | Read, grep, shell (diagnostic commands) |
| **Build resolver** | `agent://build-error-resolver` | Fix build/compile errors with minimal diffs | Read, write, shell (build commands) |
| **Security reviewer** | `agent://security-reviewer` | Security audit of changes | Read, shell (scan commands) |

### Dispatch patterns

**Single task (sequential):**
```
TechLead → run_workflow(bundled://feature-pipeline, brief) → subagent implements + tests + reports
TechLead → run_workflow(agent://code-reviewer, review brief) → reviewer verifies → reports PASS/FAIL
TechLead → git commit + push + PR
```

**Parallel independent tasks:**
```
TechLead → run_workflow(agent://wf-coder, task A)  ┐
          run_workflow(agent://wf-coder, task B)  ├─ all run simultaneously
          run_workflow(agent://wf-coder, task C)  ┘
TechLead ← collect all results → verify no conflicts → review → commit
```

**Bug investigation → fix pipeline:**
```
TechLead → run_workflow(bundled://investigate, bug brief) → investigator finds root cause
TechLead ← receive findings → run_workflow(bundled://feature-pipeline, fix brief) → implementer fixes
TechLead → run_workflow(agent://code-reviewer) → review → commit
```

### Review rules

- No self-review: the agent that wrote code cannot be its own reviewer.
- Reviewer verdict row must include: date, batch, verdict, command run, output line count.
- Behavior changes (code/tests) require **3 independent PASS** verdicts before merge.
- Mechanical changes (docs, comments, config) require **1 independent PASS**.
- The TechLead may not serve as reviewer — it only collects and forwards verdicts.

---

## Checks before marking work done

- [ ] `dotnet build RouterPlus.sln` — 0 errors, 0 warnings
- [ ] `dotnet test` — all tests green
- [ ] No hardcoded secrets staged
- [ ] PR description includes: what changed, why, test evidence
- [ ] `README.md` / `docs/user-guide.md` updated if UI or behavior changed

---

## PowerShell notes (Windows dev environment)

- Use `$LASTEXITCODE` to check native command success, not `$?`.
- Prefer file tools (Kiro's `read_file`, `fs_write`) over `Get-Content`/`Set-Content` for UTF-8 safety.
- Long-running commands (build, tests) that may exceed 20 s should be run with an explicit timeout or in background.
- Exclude `bin/`, `obj/`, `.git/` from recursive file scans.
