---
name: dispatching-parallel-agents
description: Use when facing 2+ independent tasks that can be worked on without shared state or sequential dependencies
origin: obra/superpowers
---

# Dispatching Parallel Agents

## Overview

Delegate tasks to specialized agents with isolated context. They should never inherit your session's context — you construct exactly what they need.

**Core principle:** Dispatch one agent per independent problem domain. Let them work concurrently.

## When to Use

- 3+ test files failing with different root causes
- Multiple subsystems broken independently
- Each problem can be understood without context from others
- No shared state between investigations

## The Pattern

### 1. Identify Independent Domains

Group failures by what's broken — each domain is independent.

### 2. Create Focused Agent Tasks

Each agent gets:
- **Specific scope:** One test file or subsystem
- **Clear goal:** Make these tests pass
- **Constraints:** Don't change other code
- **Expected output:** Summary of what you found and fixed

### 3. Dispatch in Parallel

Issue all dispatch calls in the SAME response — they run in parallel.
One per response = sequential.

### 4. Review and Integrate

- Read each summary
- Verify fixes don't conflict
- Run full test suite

## Good Agent Prompt Structure

1. **Focused** - One clear problem domain
2. **Self-contained** - All context needed
3. **Specific about output** - What should the agent return?
4. **Constrained** - "Do NOT change production code"

## When NOT to Use

- Failures are related (fix one might fix others)
- Need to understand full system state
- Agents would interfere (editing same files)
