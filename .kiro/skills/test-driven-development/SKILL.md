---
name: test-driven-development
description: Use when implementing any feature or bugfix, before writing implementation code
origin: obra/superpowers
---

# Test-Driven Development (TDD)

## Overview

Write the test first. Watch it fail. Write minimal code to pass.

**Core principle:** If you didn't watch the test fail, you don't know if it tests the right thing.

## The Iron Law

```
NO PRODUCTION CODE WITHOUT A FAILING TEST FIRST
```

Write code before the test? Delete it. Start over. No keeping it as "reference."

## Red-Green-Refactor

### RED — Write Failing Test
- One behavior, clear name, tests real code (not mocks)
- Run test, confirm it FAILS for the right reason

### GREEN — Minimal Code
- Simplest code to pass the test
- No extra features, no refactoring other code

### VERIFY GREEN
- Run test: passes
- Run full suite: all still pass

### REFACTOR
- Clean up while keeping tests green
- Then back to RED for next behavior

## Project Context (RouterPlus)

```powershell
# Run tests
.\.dotnet\dotnet.exe test tests/RouterPlus.Core.Tests/ --configuration Debug
```

Coverage target: 80%+ per `testing.md` steering file.
