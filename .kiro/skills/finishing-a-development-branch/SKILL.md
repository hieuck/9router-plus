---
name: finishing-a-development-branch
description: Use when implementation is complete, all tests pass, and you need to decide how to integrate the work
origin: obra/superpowers
---

# Finishing a Development Branch

## Overview

**Core principle:** Verify tests → Detect environment → Present options → Execute choice → Clean up.

## Step 1: Verify Tests

Run the project's full test suite. If tests fail, stop — fix before completing.

## Step 2: Detect Environment

```bash
GIT_DIR=$(cd "$(git rev-parse --git-dir)" 2>/dev/null && pwd -P)
GIT_COMMON=$(cd "$(git rev-parse --git-common-dir)" 2>/dev/null && pwd -P)
WORKTREE_PATH=$(git rev-parse --show-toplevel)
```

## Step 3: Determine Base Branch

Confirm the base branch before merging — merging into the wrong base is expensive to undo.

## Step 4: Present Options

```
Implementation complete. What would you like to do?

1. Merge back to <base-branch> locally
2. Push and create a Pull Request
3. Keep the branch as-is (I'll handle it later)
```

Wait for their answer — the integration decision is theirs.

## Step 5: Execute Choice

- **Option 1:** `git checkout <base>` → `git merge <feature>` → verify tests → cleanup
- **Option 2:** `git push -u origin <branch>` → create PR → keep worktree
- **Option 3:** Report branch name and worktree path

## Common Rationalizations

| Excuse | Reality |
|--------|---------|
| "Tests passed earlier" | Run the suite on the tree you are about to integrate |
| "They obviously want it merged" | Present the menu and wait |
| "The base branch is obviously main" | Confirm or ask — wrong base is expensive |
