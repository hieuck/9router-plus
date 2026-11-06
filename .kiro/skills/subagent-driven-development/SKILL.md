---
name: subagent-driven-development
description: Use when executing implementation plans with independent tasks in the current session
origin: obra/superpowers
---

# Subagent-Driven Development

Execute plan by dispatching a fresh implementer subagent per task, a task review after each, and a broad whole-branch review at the end.

**Core principle:** Fresh subagent per task + task review + final review = high quality, fast iteration.

## vs. Executing Plans (inline)

- Fresh subagent per task (no context pollution) vs. one context doing everything
- Review after each task vs. only at the end
- Costs more context; inline costs less

## Rules

- **Continuous execution:** Do not pause between tasks
- **Rulings, not stalls:** Decide conflicts, record in ledger
- **Four stops only:** irreversible op; security action; side effect outside worktree; plan completely broken

## Per Task Loop

1. Dispatch implementer subagent with focused prompt
2. If subagent has questions — answer, provide context
3. Dispatch task reviewer subagent
4. If issues found — fix loop
5. Append completion to ledger, mark todo complete

## Final Review

After all tasks: dispatch fresh whole-branch reviewer. Re-grade findings. Critical/Important → one fix pass with RED→GREEN. Minor → ledger.
