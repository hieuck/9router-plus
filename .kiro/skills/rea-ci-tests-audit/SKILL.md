---
name: rea-ci-tests-audit
description: Audit REA CI and test suites for correctness, fixture drift, false passes, and measured performance. Use for suite or workflow investigations and efficiency changes.
origin: morluto/rea
---

# REA CI and Tests Audit

Establish which checks still catch real regressions and which costs can be removed without losing that protection.

## Process

### 1. Establish the Executed Baseline
- Pin the baseline SHA and inspected workflow revision
- Use an isolated worktree when changing or comparing code
- Record exact command, toolchain, host, selected tests, prerequisites

### 2. Prove the Failure or Protection

For each candidate:
- **Drift**: compare fixtures with current admission requirements
- **Suspicious green test**: perturb the relevant behavior in an isolated copy and verify assertion detects it
- **Shared setup**: map distinct assertions to their retained scenarios

Classify findings as: reproduced defects, supported costs, hypotheses, or unavailable evidence.

### 3. Assess Efficiency Without Losing the Gate

Prefer removing proven duplicate preparation, reusing immutable setup, and reducing repeated subprocess work — before shrinking correctness coverage.

### 4. Fix, Verify, and Hand Off Within Scope

If authorized:
- Make the smallest structural change at the owning boundary
- Add a regression that fails on the baseline for the right reason
- Run the repository's applicable checks before merge

## Rules

- Do not suppress a failure, add blanket retries, or raise timeouts solely to make CI green
- A passing retry does not establish a root cause or a fix
- Keep audit notes outside the tracked tree (normally in `/tmp`)
