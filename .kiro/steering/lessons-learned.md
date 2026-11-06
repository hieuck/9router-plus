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

### Don't duplicate in AGENTS.md what a skill already covers
When a skill (e.g. `finishing-a-development-branch`) already defines a step-by-step procedure, AGENTS.md should only add project-specific constraints (branch naming, commit format, PR template path) — not repeat the generic steps. Duplication causes drift and dilutes the authority of the skill.

### Always load the matching skill before making changes
Before editing any file (code or documentation), call `disclose_context` with the skill name that matches the task. Reading a skill file directly without the tool call does not activate it. If no skill applies, state "no skill" explicitly. Skipping this step causes the agent to miss task-specific guidelines that are only surfaced by activation.

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

### Partial class split — tìm class declaration trước khi thay line index
Khi thêm `partial` keyword bằng PowerShell, phải tìm dòng chứa `class Name` bằng `Select-String` thay vì hardcode index. Index sai sẽ thay dòng `{` thành class declaration thứ hai, gây lỗi CS1514. Luôn verify: `$content[$idx]` phải chứa `class` keyword, dòng kế tiếp phải là `{`.

### `git checkout HEAD` mất uncommitted changes — kiểm tra trước khi restore
Khi restore file từ `git checkout HEAD -- <file>`, toàn bộ working copy changes bị mất vĩnh viễn. Nếu file có uncommitted changes từ session trước (thêm parameter, thêm method), cần diff trước: `git diff HEAD -- <file>` để xem có thay đổi nào cần giữ không. Trong session này, `ProviderWorkflowService.cs` có `getGoogleCredential` parameter chưa commit bị mất khi restore.

### `TreatWarningsAsErrors` + DLL lock khi app đang chạy
`RouterPlus.App` đang chạy lock DLL trong `bin/Release/`, khiến `dotnet build --configuration Release` fail với MSB3027. Workaround: build `--configuration Debug` (output khác thư mục). CI không gặp vì chạy trong môi trường sạch.

### Dependabot major bumps vs pinned SDK
Project pin .NET 8 SDK qua `global.json`. Dependabot sẽ mở PR cho package major versions (System.Management 10.x, Test.Sdk 18.x, coverlet 10.x, xunit.runner 4.x) — những package này target .NET 10 và sẽ không build được trên .NET 8. Đóng các PR dependabot major bump cho đến khi project upgrade SDK. Chỉ merge minor bumps (ví dụ Moq 4.20→4.21).
