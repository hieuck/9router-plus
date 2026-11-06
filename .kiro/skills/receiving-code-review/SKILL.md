---
name: receiving-code-review
description: Use when receiving code review feedback, before implementing suggestions — requires technical rigor and verification, not performative agreement or blind implementation
origin: obra/superpowers
---

# Code Review Reception

**Core principle:** Verify before implementing. Ask before assuming. Technical correctness over social comfort.

## The Response Pattern

```
1. READ: Complete feedback without reacting
2. UNDERSTAND: Restate requirement in own words (or ask)
3. VERIFY: Check against codebase reality
4. EVALUATE: Technically sound for THIS codebase?
5. RESPOND: Technical acknowledgment or reasoned pushback
6. IMPLEMENT: One item at a time, test each
```

## Forbidden Responses

- "You're absolutely right!" — explicit violation
- "Great point!" — performative
- "Let me implement that now" — before verification
- ANY gratitude expression ("Thanks for catching that")

## When To Push Back

Push back when:
- Suggestion breaks existing functionality
- Reviewer lacks full context
- Violates YAGNI (unused feature)
- Conflicts with architectural decisions

**How:** Technical reasoning, not defensiveness.

## Acknowledging Correct Feedback

```
✅ "Fixed. [Brief description of what changed]"
✅ "Good catch - [specific issue]. Fixed in [location]."
✅ Just fix it and show in code

❌ "You're absolutely right!"
❌ Any thanks/gratitude
```

## Unclear Feedback

If any item is unclear: STOP, ask for clarification on unclear items FIRST — then implement all at once. Partial understanding = wrong implementation.
