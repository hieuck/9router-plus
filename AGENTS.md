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

## Roles (Kiro multi-agent)

### Delegation law (mandatory)

The coordinator (Tech Lead / orchestrator session) **never does implementation work itself**. It only routes, coordinates, and communicate with the owner. All substantive work goes to subagents via workflows:

- **Read/understand code** → dispatch to a workflow agent; do not read source files in the orchestrator session to "understand the problem" first.
- **Implement a feature or fix** → `run_workflow` with a brief; the workflow agent writes the code.
- **Investigate a bug or diagnose** → `bundled://investigate` or `agent://wf-coder` with a self-contained brief.
- **Write tests** → `tdd-guide` / test-author agent.
- **Review** → `code-reviewer` / specialist reviewer agent; never self-review.
- **Run builds/tests/verification** → the workflow agent that owns the work runs them.

**What the coordinator keeps:**
- Conversations, clarifications, decisions with the user
- Lightweight orchestration: launching workflows, checking status, post-run cleanup
- Small mechanical edits the user explicitly asks for inline (steering tweaks, one-liner fixes)
- Commit, push, PR creation (after the workflow produces reviewed work)

**Anti-pattern:** catching yourself calling `read_file` or `grep_search` to understand a problem before launching a workflow — stop, that is the workflow's job. A single grep to confirm a path is fine; reading file contents to "understand" is not.

### Fixed roles

| Role | Agent | Owns |
|---|---|---|
| Coordinator | *(orchestrator session)* | Scope, routing, commit, PR, owner communication |
| Planner | `planner` | Break features into tasks; identify risks and dependencies |
| Implementer | *(coder session)* | Source edits, test edits |
| Test author | `tdd-guide` | Write red tests before implementation; enforce TDD workflow |
| Reviewer | `code-reviewer` | Independent `dotnet build` + `dotnet test`; written VERDICT: PASS/FAIL |

### Specialist agents (invoke when the task matches)

| Agent | When to use |
|---|---|
| `architect` | Architecture decisions, new projects/subsystems, major refactors |
| `build-error-resolver` | `dotnet build` fails with compiler or linker errors |
| `security-reviewer` | Any change touching auth, DPAPI vault, API keys, or user input validation |
| `performance-optimizer` | Profiling, memory leaks, slow startup, render bottlenecks |
| `refactor-cleaner` | Dead code removal, duplicate logic, large consolidation tasks |
| `doc-updater` | Updating `README.md`, `docs/user-guide.md`, or other project docs |
| `loop-operator` | Monitor and intervene in autonomous agent loops when they stall |

### Review rules

- No self-review: the agent that wrote code cannot be its own reviewer.
- Reviewer verdict row must include: date, batch, verdict, command run, output line count.
- Behavior changes (code/tests) require **3 independent PASS** verdicts before merge.
- Mechanical changes (docs, comments, config) require **1 independent PASS**.

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
