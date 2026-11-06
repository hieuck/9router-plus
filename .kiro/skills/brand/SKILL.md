---
name: brand
description: Brand voice, visual identity, messaging frameworks, asset management, brand consistency. Activate for branded content, tone of voice, marketing assets, brand compliance, style guides.
origin: nextlevelbuilder/ui-ux-pro-max-skill
---

# Brand

Brand identity, voice, messaging, asset management, and consistency frameworks.

## When to Use

- Brand voice definition and content tone guidance
- Visual identity standards and style guide development
- Messaging framework creation
- Brand consistency review and audit
- Color palette management and typography specs

## Brand Sync Workflow

```bash
# 1. Edit docs/brand-guidelines.md
# 2. Sync to design tokens
node scripts/sync-brand-to-tokens.cjs
```

Files synced:
- `docs/brand-guidelines.md` → Source of truth
- `assets/design-tokens.json` → Token definitions
- `assets/design-tokens.css` → CSS variables

## Key Areas

| Topic | Purpose |
|-------|---------|
| Voice Framework | Tone, personality, communication style |
| Visual Identity | Logo, colors, typography standards |
| Messaging | Value props, taglines, positioning |
| Consistency | Compliance checklist across channels |
| Asset Organization | Naming conventions, approval process |
