---
name: writing-plans
description: Use when you have a spec or requirements for a multi-step task, before touching code
origin: obra/superpowers
---

# Writing Plans

## Overview

Write implementation plans for an engineer who has not seen this codebase. Document what you decided: which files, names, signatures, values from spec, which tests prove each task.

**Save plans to:** `docs/superpowers/plans/YYYY-MM-DD-<feature-name>.md`

## Plan Document Header

Every plan MUST start with:

```markdown
# [Feature Name] Implementation Plan

**Goal:** [One sentence]
**Architecture:** [2-3 sentences about approach]
**Tech Stack:** [Key technologies]
**Spec:** [path to spec/design doc]

## Global Constraints
[Project-wide requirements — version floors, naming rules, platform requirements]

## Tasks
```

## Task Right-Sizing

A task is the smallest unit with its own test cycle. Each task ends with an independently testable deliverable.

## Step Granularity

Each step is ONE action with a checkable result:
- "Write the failing test" — step
- "Run it to confirm it fails" — step
- "Implement minimal code to pass" — step
- "Run tests, confirm pass" — step
- "Commit" — step

## File Structure First

Before tasks, map which files will be created/modified and their responsibility. Design units with clear boundaries, one responsibility per file.
