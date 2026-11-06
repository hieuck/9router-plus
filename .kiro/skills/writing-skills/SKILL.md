---
name: writing-skills
description: Use when creating new skills, editing existing skills, or verifying skills work before deployment
origin: obra/superpowers
---

# Writing Skills

## Overview

Writing skills IS Test-Driven Development applied to process documentation.

**Core principle:** If you didn't watch an agent fail without the skill, you don't know if the skill teaches the right thing.

## TDD Mapping

| TDD | Skill Creation |
|-----|----------------|
| Test case | Pressure scenario with subagent |
| Production code | Skill document (SKILL.md) |
| Test fails (RED) | Agent violates rule without skill |
| Test passes (GREEN) | Agent complies with skill present |
| Refactor | Close loopholes while maintaining compliance |

## When to Create a Skill

**Create when:**
- Technique wasn't intuitively obvious
- You'd reference this again across projects
- Pattern applies broadly (not project-specific)

**Don't create for:**
- One-off solutions
- Standard practices well-documented elsewhere
- Project-specific conventions (put in steering instead)

## SKILL.md Structure

```
---
name: skill-name
description: Use when [triggering conditions and symptoms]
---

# Title

## Overview
[Core principle in one line]

## When to Use
[Conditions]

## The Process / Pattern
[Steps or patterns]

## Red Flags / Common Mistakes
[Anti-patterns]
```

The `description` field says ONLY when to use it — never summarize the workflow.
