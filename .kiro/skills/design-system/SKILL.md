---
name: design-system
description: Token architecture, component specifications, and slide generation. Three-layer tokens (primitive→semantic→component), CSS variables, spacing/typography scales, component specs. Use for design tokens, systematic design, brand-compliant presentations.
origin: nextlevelbuilder/ui-ux-pro-max-skill
---

# Design System

Token architecture, component specifications, systematic design.

## Token Architecture — Three Layers

```
Primitive (raw values) → Semantic (purpose aliases) → Component (component-specific)
```

```css
/* Primitive */
--color-blue-600: #2563EB;

/* Semantic */
--color-primary: var(--color-blue-600);

/* Component */
--button-bg: var(--color-primary);
```

**Rule:** Never use raw hex in components — always reference tokens. This enables theme switching.

## Quick Start

```bash
# Generate tokens from config
node scripts/generate-tokens.cjs --config tokens.json -o tokens.css

# Validate no hardcoded values
node scripts/validate-tokens.cjs --dir src/
```

## Component Spec Pattern

| Property | Default | Hover | Active | Disabled |
|----------|---------|-------|--------|----------|
| Background | primary | primary-dark | primary-darker | muted |
| Text | white | white | white | muted-fg |
| Shadow | sm | md | none | none |

## Integration

- **With brand:** Extract primitives from brand colors/typography
- **With ui-styling:** Component tokens → Tailwind config
