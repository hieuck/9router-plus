---
inclusion: always
name: lessons-learned
description: Project-specific patterns, preferences, and lessons learned over time (user-editable)
---

# Lessons Learned

This file captures project-specific patterns, coding preferences, common pitfalls, and architectural decisions that emerge during development. It serves as a workaround for continuous learning by allowing you to document patterns manually.

**How to use this file:**
1. The `extract-patterns` hook will suggest patterns after agent sessions
2. Review suggestions and add genuinely useful patterns below
3. Edit this file directly to capture team conventions
4. Keep it focused on project-specific insights, not general best practices

---

## Project-Specific Patterns

*Document patterns unique to this project that the team should follow.*

### Example: API Error Handling
```typescript
// Always use our custom ApiError class for consistent error responses
throw new ApiError(404, 'Resource not found', { resourceId });
```

---

## Code Style Preferences

*Document team preferences that go beyond standard linting rules.*

### Example: Import Organization
```typescript
// Group imports: external, internal, types
import { useState } from 'react';
import { Button } from '@/components/ui';
import type { User } from '@/types';
```

---

## Kiro Hooks

### `install.sh` is additive-only — it won't update existing installations
The installer skips any file that already exists in the target (`if [ ! -f ... ]`). Running it against a folder that already has `.kiro/` will not overwrite or update hooks, agents, or steering files. To push updates to an existing project, manually copy the changed files or remove the target files first before re-running the installer.

### README.md mirrors hook configurations — keep them in sync
The hooks table and Example 5 in README.md document the action type (`runCommand` vs `askAgent`) and behavior of each hook. When changing a hook's `then.type` or behavior, update both the hook file and the corresponding README entries to avoid misleading documentation.

### Prefer `askAgent` over `runCommand` for file-event hooks
`runCommand` hooks on `fileEdited` or `fileCreated` events spawn a new terminal session every time they fire, creating friction. Use `askAgent` instead so the agent handles the task inline. Reserve `runCommand` for `userTriggered` hooks where a manual, isolated terminal run is intentional (e.g., `quality-gate`).

---

## Common Pitfalls

*Document mistakes that have been made and how to avoid them.*

### Example: Database Transactions
- Always wrap multiple database operations in a transaction
- Remember to handle rollback on errors
- Don't forget to close connections in finally blocks

---

## Architecture Decisions

*Document key architectural decisions and their rationale.*

### Example: State Management
- **Decision**: Use Zustand for global state, React Context for component trees
- **Rationale**: Zustand provides better performance and simpler API than Redux
- **Trade-offs**: Less ecosystem tooling than Redux, but sufficient for our needs

---

## Notes

- Keep entries concise and actionable
- Remove patterns that are no longer relevant
- Update patterns as the project evolves
- Focus on what's unique to this project

### Hook type `agent` vs `command` — use `command` + exit 2 for hard enforcement
`agent`-type hooks inject a prompt asking the model to self-enforce, but the tool still executes unless the model voluntarily stops. Use `command`-type hooks with `exit 2` when you need a hard block (e.g. TechLead forbidden from reading `src/**/*.cs`). Reserve `agent`-type only for advisory nudges where the model should decide. Found when all three TechLead enforcement hooks were `agent`-type and source reads were silently going through.

### Commit message body can false-trigger `dotnet` command hooks
A hook matching `dotnet\s+(build|test)` anywhere in the shell command will fire on `git commit -m "... dotnet build ..."` because the message text is part of the command string. Fix: split the command on `;` and `\n`, then anchor each line with `^` before matching, so only actual `dotnet build` invocations at statement-start are caught.

### Operator precedence in multi-part boolean expressions — always use explicit parentheses
`&&` binds tighter than `||` in C#. `A && B || A && C` evaluates as `(A&&B)||(A&&C)` not `A&&(B||C)`. When the intent is the latter (shared guard), write `A && (B || C)` explicitly. Found in `ProviderConnection.HasError` — refactored to the clearer form without behavior change.

### Dead HashSet vs inline host checks — use the set, not repetition
Declaring a `HashSet<string>` for allowed hosts and then writing inline `host.Equals("...")` checks in the same class defeats the purpose. All host checks must go through the HashSet. Found in `GoogleLoginStateMachine` — 12 inline `Equals()` calls duplicated `AllowedCompletionHosts` across 3 spots; all replaced with `HashSet.Contains()`. When reviewing Chrome automation code, always check whether host allowlists are consistently used.
