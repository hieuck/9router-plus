---
inclusion: auto
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

### `AsyncRelayCommand<T>` with value-type `T`: `CanExecute(null)` always returns false

`AsyncRelayCommand<T>` uses `parameter is T value` in `CanExecute`. When `T` is a value type (e.g. `ProviderKind` enum), `null is ProviderKind` is always `false`, so WPF button binding with no `CommandParameter` will always appear disabled. The fix in `AsyncRelayCommand<T>`:

```csharp
// After (null = no param supplied → fall back to canExecute-less check)
public bool CanExecute(object? parameter) =>
    !_isRunning && (parameter is T value ? (_canExecute?.Invoke(value) ?? true) : (_canExecute is null));
```

Always pass `CommandParameter="{Binding Kind}"` in XAML when using `AsyncRelayCommand<ProviderKind>`.

### Auto-get-key flows must route by provider — don't hard-code OpenRouter

Both Ollama and OpenRouter show the "Tự lấy key" button (driven by `WorkflowKind.ApiKey`). The `AutoGetKeyCommand` must use `AsyncRelayCommand<ProviderKind>` with `CommandParameter="{Binding Kind}"` in XAML so the correct flow runs. Hard-coding `ProviderKind.OpenRouter` inside `AutoGetKeyAsync` silently runs the wrong browser flow when the user clicks the Ollama card.

### Adding a new ApiKey provider flow requires 4 wiring points

1. Add `ConnectXxxFlowAsync()` to `ChromeManagedSession` (returns `(XxxBrowser, IGoogleLoginBrowser)`)
2. Add `_xxxKeyFlow` field + `CreateDefaultXxxKeyFlow()` factory in `MainViewModel`
3. Initialize `_xxxKeyFlow` in `MainViewModel` constructor
4. Add a routing branch in `AutoGetKeyAsync` for the new `ProviderKind`

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
