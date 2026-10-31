# Rewrite Agentic Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the scattered agent/subagent workflow docs with a clearer, more practical system that keeps the safety guardrails (coordinator doesn't edit, independent reviews) but removes unnecessary friction for daily work.

**Architecture:** Split `agentic/` into focused documents. Keep the "coordinator only orchestrates, delegates to subagents" rule. Make "independent reviewers" enforceable. Introduce risk-based review tiers with a fast-path. Move volatile eval/quota data out of prose and into `agentic/state/`.

**Tech Stack:** Markdown only. No code changes.

**Spec:** User request: rewrite agent/subagent workflow for 9router-plus to be clearer and more effective.

## Global Constraints
- Coordinator MUST NOT edit code. All code changes must go through coder subagents.
- Every non-trivial change MUST go through independent reviews. Definition of "independent" must be explicit.
- Do not ask user in G1–G4 unless in escalation list.
- Keep escalation list strict and minimal.
- Prefer splitting docs over making one giant doc.
- Do not hardcode volatile quota numbers. Reference `agentic/state/`.
- Keep Vietnamese/English consistent within each doc where reasonable.

## Review Focus
- **Ambiguous "independent"**: A plan that says "3 reviewers" but doesn't say different models + isolated context + no shared verdict is unenforceable.
- **Batch definition**: Without defining `batch`, promotion rules in MODEL_POLICY and "every coder output" in WORKFLOW will conflict.
- **Risk creep**: Fast-path must not apply to security, schema, API contracts, dependencies, auth, secrets, or destructive changes.
- **Duplication**: Kiro quota appears twice in current MODEL_POLICY — must not reintroduce.
- **Session bleed**: Kiro subagent wave hazard from WORKFLOW must be carried over or handled.
- **Model substitution**: If preferred reviewer models unavailable, fallback must still satisfy independence.

---

### Task 1: Create new docs and restructure layout
**Files:**
- Create: `agentic/PROBES.md`
- Create: `agentic/SUBAGENT_CONTRACT.md`
- Modify: `agentic/WORKFLOW.md`
- Modify: `agentic/MODEL_POLICY.md`
- Modify: `agentic/ROLES.md`
- Modify: `agentic/QUESTION_POLICY.md` (minimal edits only if needed)
- Modify: `agentic/README.md`

**Interfaces:**
- Consumes: existing `agentic/*.md`, `agentic/templates/*`
- Produces: new structure with clear split

- [ ] **Step 1: Write PROBES.md from WORKFLOW.md**
  Extract sections:
  - Probe protocol (text/tool/TPS)
  - Kiro pitfalls (a, b)
  - Verification checklist
  - Recording
  - Apply

- [ ] **Step 2: Write SUBAGENT_CONTRACT.md**
  Must include:
  - Coordinator rules (no edit, delegate, track batches)
  - Coder subagent brief (goal, files allowed, constraints, done criteria)
  - Reviewer subagent rules (analysis-only, independent, use `templates/review.md`, return PASS/FAIL + findings)
  - Independence definition (different model, isolated context, no verdict sharing, no reading other's review)
  - Batch definition
  - Review tiers (trivial/normal/high-risk) + fast-path rules
  - Failure handling (2 PASS/1 FAIL, Critical => block, 3-round rule)

- [ ] **Step 3: Rewrite WORKFLOW.md to be minimal**
  Replace with:
  ```markdown
  # Workflow
  
  ## Core Rule
  Coordinator coordinates only. No file edits, no commands that mutate repo/state. Delegate to subagents.
  
  ## Flow
  intake → plan → batch → coder → reviewers (independent) → verifier → merge
  
  ## Batches
  A batch = smallest testable unit of work with clear done criteria...
  
  ## Review Tiers
  - trivial: docs/comment/typo/format only, no logic/API/schema/deps/security -> fast path (see SUBAGENT_CONTRACT)
  - normal: most code changes -> >= 3 independent reviewers
  - high-risk: auth/security/schema/migration/API contract/deps/config/destructive -> >= 3 independent reviewers, stricter
  
  ## Gates
  See GATES.md
  
  ## Escalation
  See escalation list (unchanged in spirit) + link to QUESTION_POLICY
  
  ## References
  - ROLES.md
  - SUBAGENT_CONTRACT.md
  - PROBES.md
  - MODEL_POLICY.md
  - QUESTION_POLICY.md
  ```

- [ ] **Step 4: Rewrite MODEL_POLICY.md**
  Keep:
  - selection order
  - quick table
  - cost policy
  - coder tiers (probe/trial/official)
  - free priority
  Remove duplicates of Kiro quota. Replace with:
  "Kiro quota: see `agentic/state/kiro-quota.md` or latest `agentic/state/model-eval.json`. Run `node agentic/scripts/model-eval.mjs` to refresh."

- [ ] **Step 5: Update ROLES.md and README.md**
  Add explicit sections for coordinator/coder/reviewer with links to SUBAGENT_CONTRACT.

- [ ] **Step 6: Verify references**
  Grep for old references (probe protocol text moved) and update.

### Task 2: Apply changes
- [ ] **Step 1: Create files**
- [ ] **Step 2: Edit existing files**
- [ ] **Step 3: Quick sanity check**
  `grep -rn "Probe protocol" agentic/` should mostly point to PROBES.md
  `grep -rn "independent" agentic/WORKFLOW.md agentic/SUBAGENT_CONTRACT.md` exists

### Task 3: Commit
- [ ] **Step 1: git add agentic/**
- [ ] **Step 2: git commit -m "agentic: rewrite workflow to clarify roles, split probes, add subagent contract"**
