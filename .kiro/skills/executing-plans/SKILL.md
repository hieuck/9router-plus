---
name: executing-plans
description: Use when executing an implementation plan in the current session as the implementer yourself — your human partner chose inline execution, or no subagent tool is available
origin: obra/superpowers
---

# Executing Plans

Execute the plan yourself, task by task, in this session.

**Core principle:** The plan already did the thinking. Execute it exactly, prove each step with a test you watched fail and then pass.

## Rules

- **Continuous execution:** Do not pause between tasks. No "should I continue?" prompts.
- **Rulings, not stalls:** Conflicts or ambiguities — decide them. Record in ledger as `Ruling: <what> — <why> — <cost if wrong>`.
- **Four things stop you only:** irreversible/destructive operation; security-sensitive action; side effect outside this worktree (merge, push to shared branch, publish); plan so broken every path is a guess.

## Process Per Task

1. Read the task brief
2. Work steps in order — TDD, run every verification, read every output
3. If step output doesn't match plan: rule and ledger if plan wrong; systematic-debugging if code wrong
4. Commit as plan specifies
5. Mark task complete in ledger

## After All Tasks

Run a final whole-branch review (fresh reviewer if available). Fix Critical/Important findings with RED→GREEN cycle. Minor → ledger for later.
