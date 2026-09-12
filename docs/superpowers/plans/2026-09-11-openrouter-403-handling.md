# Extend OpenRouter Auto‑Disable Logic to Handle 403 “Inference is blocked” Error

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Modify `QuotaAutoDisablePolicy.IsOpenRouterRateLimitExceeded` and `IsOpenRouterRateLimitRecovered` so that an OpenRouter connection receiving the 403 error *Inference is blocked on this account* is treated as being over‑quota (auto‑disable) and is **not** considered recovered until the error clears.

**Architecture:**  
- Keep the existing single‑responsibility methods; extend their string checks to also look for the new error message.  
- No new types or services are required – the change is purely logical within the existing policy class.  
- The change preserves all existing behavior for other providers and for the original rate‑limit message.  

**Tech Stack:** C# .NET 8, existing `RouterPlus.Core.Providers.QuotaAutoDisablePolicy` class.

**Spec:** This plan implements the behavior described in the issue; the specification is the goal statement above.

## Global Constraints
- Must not break existing auto‑disable/recover logic for the original rate‑limit message.  
- Must not affect other providers (Codex, Ollama, Kiro, GitHub, Kimchi).  
- All new logic must be covered by unit tests.  
- No changes to public interfaces beyond the two private methods.

---  

### Task 1: Review current implementation

**Files:**  
- Read: `src/RouterPlus.Core/Providers/QuotaAutoDisablePolicy.cs` (lines 38‑48)

**Interfaces:**  
- Consumes: None  
- Produces: Understanding of the two methods’ current logic.

**Steps:**  
- [ ] **Step 1:** Read the file and note the exact strings being checked.  
- [ ] **Step 2:** Commit a brief note (no code change).  

```bash
git add docs/superpowers/plans/2026-09-11-openrouter-403-handling.md
git commit -m "plan: record current IsOpenRouterRateLimitExceeded/Recovered implementation"
```

### Task 2: Add failing unit tests for the new error

**Files:**  
- Create/Modify: `tests/RouterPlus.Core.Tests/QuotaAutoDisablePolicyTests.cs`

**Interfaces:**  
- Consumes: None  
- Produces: Two new test methods that verify the extended behavior.

**Steps:**  
- [ ] **Step 1:** Write a failing test `CanAutoDisable_returns_true_for_openrouter_when_inference_blocked`.  
- [ ] **Step 2:** Write a failing test `HasRecovered_returns_false_for_openrouter_when_inference_blocked`.  
- [ ] **Step 3:** Run the tests to confirm they fail.  
- [ ] **Step 4:** Commit the test file.  

```bash
git add tests/RouterPlus.Core.Tests/QuotaAutoDisablePolicyTests.cs
git commit -m "test: add failing tests for OpenRouter 403 inference blocked error"
```

### Task 3: Implement the extended check in IsOpenRouterRateLimitExceeded

**Files:**  
- Modify: `src/RouterPlus.Core/Providers/QuotaAutoDisablePolicy.cs` (method `IsOpenRouterRateLimitExceeded`)

**Interfaces:**  
- Consumes: None  
- Produces: Updated method that returns `true` for either the original rate‑limit message or the new inference‑blocked message.

**Steps:**  
- [ ] **Step 1:** Replace the single `Contains` check with a logical OR that also checks for `"Inference is blocked on this account"` (case‑insensitive).  
- [ ] **Step 2:** Run the two new tests to verify they now pass.  
- [ ] **Step 3:** Ensure existing tests still pass.  
- [ ] **Step 4:** Commit the change.  

```bash
git add src/RouterPlus.Core/Providers/QuotaAutoDisablePolicy.cs
git commit -m "feat: extend IsOpenRouterRateLimitExceeded to treat 403 inference blocked as over quota"
```

### Task 4: Implement the extended check in IsOpenRouterRateLimitRecovered

**Files:**  
- Modify: `src/RouterPlus.Core/Providers/QuotaAutoDisablePolicy.cs` (method `IsOpenRouterRateLimitRecovered`)

**Interfaces:**  
- Consumes: None  
- Produces: Updated method that returns `false` when the inference‑blocked message is present (i.e., treats it as not recovered).

**Steps:**  
- [ ] **Step 1:** Change the method to return `false` if the error contains `"Inference is blocked on this account"` (in addition to the existing rate‑limit check).  
- [ ] **Step 2:** Run the two new tests to verify they pass.  
- [ ] **Step 3:** Run the full `QuotaAutoDisablePolicyTests` suite to ensure no regressions.  
- [ ] **Step 4:** Commit the change.  

```bash
git add src/RouterPlus.Core/Providers/QuotaAutoDisablePolicy.cs
git commit -m "feat: extend IsOpenRouterRateLimitRecovered to treat 403 inference blocked as not recovered"
```

### Task 5: Update documentation

**Files:**  
- Modify: `docs/AUDIT_QUOTA_AUTODISABLE.md`

**Interfaces:**  
- Consumes: None  
- Produces: Updated table and notes reflecting the new error handling.

**Steps:**  
- [ ] **Step 1:** Edit the OpenRouter row in the “Supported Providers” table to indicate that auto‑disable triggers on either the rate‑limit message **or** the inference‑blocked message, and that recovery requires the absence of **both** messages.  
- [ ] **Step 2:** Add a brief note under the table explaining the 403‑blocked behavior.  
- [ ] **Step 3:** Commit the documentation change.  

```bash
git add docs/AUDIT_QUOTA_AUTODISABLE.md
git commit -m "docs: document OpenRouter 403 inference blocked error handling"
```

### Task 6: Run full test suite to ensure no regressions

**Files:**  
- No file changes (verification).

**Steps:**  
- [ ] **Step 1:** Run `dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj` – expect all pass.  
- [ ] **Step 2:** Run `dotnet test tests/RouterPlus.Infrastructure.Tests/RouterPlus.Infrastructure.Tests.csproj` – expect all pass (or at least no new failures).  
- [ ] **Step 3:** Run `dotnet test tests/RouterPlus.App.Tests/RouterPlus.App.Tests.csproj` – expect no new failures (existing test fixture errors are unchanged).  
- [ ] **Step 4:** Commit a final summary.  

```bash
git commit -m "test: verify all existing test suites still pass after extending OpenRouter 403 handling"
```

### Task 7: Final cleanup and documentation of the plan

**Files:**  
- Modify: `docs/superpowers/plans/2026-09-11-openrouter-403-handling.md` (add completion note).

**Steps:**  
- [ ] **Step 1:** Update the plan file with a “Completed” checkbox for each task.  
- [ ] **Step 2:** Commit the updated plan.  

```bash
git add docs/superpowers/plans/2026-09-11-openrouter-403-handling.md
git commit -m "plan: mark all tasks as completed"
```

---  

**Execution Hint:**  
If you choose the subagent‑driven approach, I will spawn a fresh subagent for each task, run the steps, and report back after each checkpoint. If you prefer inline execution, I will work through the tasks in this session, pausing for your confirmation after each major step. Let me know which approach you’d like to take.