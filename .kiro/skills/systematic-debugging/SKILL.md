---
name: systematic-debugging
description: Use when encountering any bug, test failure, or unexpected behavior, before proposing fixes
origin: obra/superpowers
---

# Systematic Debugging

## Overview

**Core principle:** ALWAYS find root cause before attempting fixes. Symptom fixes are failure.

## The Iron Law

```
NO FIXES WITHOUT ROOT CAUSE INVESTIGATION FIRST
```

## The Four Phases

### Phase 1: Root Cause Investigation

**BEFORE attempting ANY fix:**

1. **Read Error Messages Carefully** — stack traces, line numbers, error codes
2. **Reproduce Consistently** — can you trigger it reliably?
3. **Check Recent Changes** — git diff, recent commits, new dependencies
4. **Gather Evidence in Multi-Component Systems** — add diagnostic instrumentation at each boundary, run once to see WHERE it breaks
5. **Trace Data Flow** — where does bad value originate? trace up the call stack

### Phase 2: Pattern Analysis

1. Find working examples in same codebase
2. Compare against references — read completely, not skim
3. Identify differences between working and broken
4. Understand dependencies and assumptions

### Phase 3: Hypothesis and Testing

1. Form ONE hypothesis based on evidence
2. Design minimal test to confirm/deny
3. Run test, observe result
4. If confirmed → fix root cause; if denied → new hypothesis

### Phase 4: Fix and Verify

1. Fix at root cause, not symptom
2. Run full test suite
3. Verify original issue is gone
4. Check for regressions

## Red Flags

- Trying a fix without understanding why it should work
- "Just this once" skipping root cause
- Fixing symptoms instead of causes
- Under time pressure (emergencies make guessing tempting — systematic is faster)
