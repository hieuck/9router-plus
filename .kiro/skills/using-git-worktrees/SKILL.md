---
name: using-git-worktrees
description: Use when starting feature work that needs isolation from current workspace or before executing implementation plans
origin: obra/superpowers
---

# Using Git Worktrees

## Overview

Ensure work happens in an isolated workspace.

**Core principle:** Detect existing isolation first. Then use native tools. Then fall back to git.

## Step 0: Detect Existing Isolation

```bash
GIT_DIR=$(cd "$(git rev-parse --git-dir)" 2>/dev/null && pwd -P)
GIT_COMMON=$(cd "$(git rev-parse --git-common-dir)" 2>/dev/null && pwd -P)
```

If `GIT_DIR != GIT_COMMON` (and not a submodule): already in a linked worktree — skip creation.

## Step 1: Create Isolated Workspace

```bash
# Verify directory is ignored first
git check-ignore -q .worktrees 2>/dev/null

# Create worktree
git worktree add ".worktrees/<branch-name>" -b "<branch-name>"
cd ".worktrees/<branch-name>"
```

## Step 2: Project Setup

```powershell
# RouterPlus: restore dependencies
.\.dotnet\dotnet.exe restore RouterPlus.sln
```

## Step 3: Verify Clean Baseline

```powershell
.\.dotnet\dotnet.exe test RouterPlus.sln --configuration Debug
```

Report: "Worktree ready at `<path>`. Tests passing. Ready to implement."

## Common Mistakes

| Mistake | Reality |
|---------|---------|
| "The worktree directory is surely ignored" | Run `git check-ignore` — unignored = commits whole tree |
| "Baseline tests can wait" | A dirty baseline makes every failure ambiguous |
