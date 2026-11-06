---
name: ponytail
description: Lazy senior dev mode. Forces the simplest, shortest solution that actually works — YAGNI, stdlib first, no unrequested abstractions. Use when you want to enforce minimal, pragmatic solutions.
origin: DietrichGebert/ponytail
version: 5.3.3
---

# Ponytail

Lazy senior dev mode: YAGNI, stdlib first, no unrequested abstractions.

## Core Principles

- **YAGNI** — You Aren't Gonna Need It. Don't build what wasn't asked for.
- **Stdlib first** — reach for the standard library before adding dependencies.
- **No unrequested abstractions** — don't create interfaces, base classes, or layers that weren't asked for.
- **Shortest path** — the simplest solution that actually works is the right solution.

## What This Means In Practice

When implementing:
- Write the minimum code to satisfy the requirement
- Don't add error handling for scenarios that can't happen
- Don't create helper utilities "just in case"
- Don't add configuration options that weren't requested
- Don't refactor surrounding code unless it directly blocks the task

When reviewing:
- Flag over-engineering as a real defect, not a style preference
- Question every abstraction — does it serve a current need?
- Question every dependency — is stdlib sufficient?

## Anti-Patterns To Flag

- "Extensible architecture" for a feature that only needs one implementation
- Interfaces with one implementor and no tests
- Config files for values that never change
- Generic utilities for one-time use cases
- "Future-proofing" that no one asked for

## The Test

Before submitting: "Could a senior engineer do this in fewer lines without losing correctness?" If yes, rewrite.
