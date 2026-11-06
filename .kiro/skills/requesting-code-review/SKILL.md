---
name: requesting-code-review
description: Use when completing tasks, implementing major features, or before merging to verify work meets requirements
origin: obra/superpowers
---

# Requesting Code Review

Dispatch a code reviewer subagent to catch issues before they cascade.

**Core principle:** Review early, review often.

## When to Request

**Mandatory:**
- After each task in subagent-driven development
- After completing major feature
- Before merge to main

## How to Request

```bash
BASE_SHA=$(git rev-parse HEAD~1)
HEAD_SHA=$(git rev-parse HEAD)
```

Dispatch a code reviewer subagent with:
- `DESCRIPTION` — brief summary of what you built
- `PLAN_OR_REQUIREMENTS` — what it should do
- `BASE_SHA` / `HEAD_SHA` — commit range

## Act on Feedback

- Fix **Critical** issues immediately
- Fix **Important** issues before proceeding
- Note **Minor** issues for later
- Push back if reviewer is wrong (with reasoning)

## Red Flags

- Skip review because "it's simple"
- Ignore Critical issues
- Proceed with unfixed Important issues
