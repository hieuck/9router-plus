# Subagent Contract

This document defines how agents and subagents must work together in this repo. It is the single source of truth for delegation, independence, and review.

## Core Rules

- **Coordinator coordinates only.** The coordinating agent MUST NOT edit files, run mutating commands, or change repo state. It may read files and inspect results.
- **Delegate everything code-related.** All code changes, fixes, and refactors go through a **coder subagent**.
- **Every non-trivial coder output gets independent reviews.** See Review Tiers below.
- **No user intervention in G1–G4** unless the issue is in the escalation list.

## Batch Definition

A **batch** is the smallest testable unit of work that:
- Has a clear goal and done criteria
- Can be verified independently (build/tests/gates as applicable)
- Is worth reviewing as one unit

A batch may touch 1–N files if they are part of the same logical change. Do not split a tightly coupled change across multiple batches just to satisfy "one file" unless there is a strong reason.

## Coordinator Responsibilities

1. **Intake** - Restate goal, scope, success criteria. Identify affected files/areas.
2. **Plan** - Ensure `templates/spec.md` and `templates/plan.md` are written/approved if non-trivial.
3. **Dispatch** - Create a clear subagent brief (see below). Track batch state.
4. **Orchestrate reviews** - Dispatch exactly the required number of **independent** reviewers per tier.
5. **Verify** - Run/collect verifier evidence against gates.
6. **Merge decision** - Only when all gates pass.

## Coder Subagent Brief (required)

When dispatching a coder subagent, the coordinator MUST provide:

- **Goal** - Clear description of what to achieve
- **Allowed paths** - Files/directories the coder may touch
- **Constraints** - Don't add deps unless escalated, follow existing patterns, minimal change
- **Done criteria** - Specific, testable conditions
- **Context** - Relevant files, specs/plans, related code (minimal, focused)
- **Model** - Use appropriate model per `MODEL_POLICY.md`

The coder subagent MUST:
- Make the **smallest complete change** that satisfies done criteria
- Follow project conventions
- Run local checks if reasonable
- Report back with: files changed, summary, self-checks, risks

## Reviewer Subagent Rules

Reviewers are **analysis-only**. They MUST NOT edit files or run mutating commands.

Each reviewer MUST:
- Review the coder's diff/output against the batch goal and done criteria
- Use `templates/review.md` format
- Return **PASS/FAIL** with findings classified as **Critical/Major/Minor/Suggestion**
- Be thorough but focused

## Independence Definition (MANDATORY)

For reviews to be considered **independent**, ALL of the following must hold:

1. **Different models** - Reviewers MUST use different models from each other when possible. If forced to substitute due to unavailability, document it.
2. **Isolated context** - Each reviewer gets its own subagent session with minimal context. Reviewers MUST NOT see each other's verdicts, comments, or rationale.
3. **No coder leakage** - Reviewers should not see the coder's internal reasoning/tool calls. Only provide diff/files/goal/done criteria.
4. **No cross-contamination** - Do not copy-paste one reviewer's feedback to another reviewer.
5. **Model diversity preferred** - Avoid running all 3 reviews with the same model family if alternatives exist.

**If independence cannot be satisfied**, the coordinator MUST escalate or find alternative models. Do not "fake" independence.

## Review Tiers

| Tier | When | Reviewers | Notes |
|---|---|---|---|
| **trivial** | Pure docs/comments/typo/rename-only/formatting. No logic change. No API/schema/deps/security/auth/config. Must be documented with rationale. | 1 review (or skip only if truly trivial AND reversible) | Still must follow "coordinator doesn't edit". If uncertain, use `normal`. |
| **normal** | Most code changes, bug fixes, UI tweaks without security impact. | **3 independent reviewers** | Default for code changes. |
| **high-risk** | Auth/security/permissions, DB schema/migrations, API contract changes, new dependencies, config that affects prod behavior, secrets handling, destructive operations, cross-cutting refactors. **Also includes any change to `AGENTS.md` or `agentic/*.md` classified as material.** | **3 independent reviewers** | May require extra scrutiny. See escalation list. Must satisfy full independence. |

**Rule:** When in doubt, use `normal` or `high-risk`. Err on the side of more review.

## Meta-Reviews (agentic/AGENTS.md changes)

- **Material:** adds/removes/weakens constraints, gates, roles, escalation rules, review counts/independence, or coordinator boundaries. When in doubt → **material**.
- **Required:** `dev-team` with >=3 independent reviewers, isolated context, different models when possible. Save to `agentic/state/meta-reviews/` with reviewer models, sessions, verdicts, rationale, diff summary.
- **Trivial exemption:** only pure formatting/typo/link. Must log rationale + file list. Conflicts with verified rules resolved in favor of verified rules.
- **Enforcement:** tracked by G5; missing evidence/independence/Critical → block.


## Review Resolution

- **All PASS, no Critical** → Can proceed to verifier
- **Any Critical** → **Block**. Must fix. Cannot merge.
- **2 PASS / 1 FAIL (Major/Minor)** → Fix issues, re-review only the failing areas or full batch depending on severity. At least the reviewer(s) that raised concerns must be satisfied.
- **3-round review failure** → Escalate per `WORKFLOW.md`. Rethink approach, don't just patch endlessly.

## Fast Path Guidelines

Fast path is ONLY for **trivial** tier. Even then:
- Coordinator still must not edit
- Must delegate to coder subagent or follow explicit rules
- Must document why it's trivial in batch notes

## Subagent Wave Safety (Kiro)

If using Kiro models in subagent waves, follow `PROBES.md` section on session bleed. Use fresh sessions/parents or avoid waves that would share conversation IDs.
