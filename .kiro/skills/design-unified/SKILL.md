---
name: design-unified
description: "Comprehensive design skill: brand identity, design tokens, UI styling, logo generation, corporate identity, HTML presentations, banner design, icon design, social photos. Use when designing logos, CIP, slides, banners, icons, or social media images."
origin: nextlevelbuilder/ui-ux-pro-max-skill
version: "2.1.0"
---

# Design (Unified)

Unified design skill: brand, tokens, UI, logo, CIP, slides, banners, social photos, icons.

## Sub-skill Routing

| Task | Sub-skill |
|------|-----------|
| Brand identity, voice, assets | `brand` |
| Tokens, specs, CSS vars | `design-system` |
| shadcn/ui, Tailwind, code | `ui-styling` |
| Logo creation | Logo (built-in) |
| Corporate identity program (CIP) | CIP (built-in) |
| Presentations, pitch decks | Slides (built-in) |
| Banners, covers, headers | `banner-design` |
| Social media images | Social Photos (built-in) |

## Logo Generation (Built-in)

55+ styles, 30 color palettes, 25 industry guides.

Search styles:
```bash
python3 scripts/logo/search.py "minimalist clean" --domain style
```

Generate:
```bash
python3 scripts/logo/generate.py --brand "BrandName" --style minimalist --industry tech
```

**ALWAYS** generate with white background. After generation, offer HTML gallery preview.

## CIP (Corporate Identity Program)

50+ deliverables, 20 styles, 20 industries.

```bash
python3 scripts/cip/search.py "tech startup" --cip-brief -b "BrandName"
```

## Slides

Brand-compliant presentations using design tokens + Chart.js.

```bash
/slides:create "10-slide investor pitch for [Brand]"
```

All slides MUST:
1. Import `assets/design-tokens.css`
2. Use CSS variables: `var(--color-primary)`, etc.
3. Use Chart.js for charts (not CSS-only)
4. Include keyboard navigation
