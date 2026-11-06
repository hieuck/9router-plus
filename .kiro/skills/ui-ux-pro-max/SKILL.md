---
name: ui-ux-pro-max
description: "UI/UX design intelligence for web, mobile, and desktop. Use when designing, building, reviewing, or fixing interfaces: pages, components, design systems, accessibility, interaction, responsive layout, typography, color, charts."
origin: nextlevelbuilder/ui-ux-pro-max-skill
version: "2.15.0"
---

# UI/UX Pro Max — Design Intelligence

Searchable local UI/UX guidance: 80 styles, 192 product palettes, 74 font pairings, 119 UX guidelines, 105 icons, 25 chart types, 22 stacks.

## When to Apply

Use when the task involves **UI structure, visual design decisions, interaction patterns, or user experience quality control**.

Skip for pure backend logic, API/database design, non-visual performance work, or infrastructure.

## Rule Categories by Priority

| Priority | Category | Key Checks | Anti-Patterns |
|----------|----------|------------|---------------|
| 1 | Accessibility | Contrast 4.5:1, Alt text, Keyboard nav, Aria-labels | Removing focus rings, Icon-only buttons without labels |
| 2 | Touch & Interaction | Min size 44×44px, 8px+ spacing, Loading feedback | Hover-only, Instant state changes (0ms) |
| 3 | Performance | WebP/AVIF, Lazy loading, Reserve space (CLS < 0.1) | Layout thrashing |
| 4 | Style Selection | Match product type, Consistency, SVG icons | Mixing flat & skeuomorphic randomly |
| 5 | Layout & Responsive | Mobile-first breakpoints, No horizontal scroll | Fixed px container widths |
| 6 | Typography & Color | Base 16px, Line-height 1.5, Semantic color tokens | Text < 12px body, Gray-on-gray |
| 7 | Animation | Context-aware timing, Motion conveys meaning | One duration for everything, No reduced-motion |
| 8 | Forms & Feedback | Visible labels, Error near field, Progressive disclosure | Placeholder-only label |
| 9 | Navigation Patterns | Predictable back, Bottom nav ≤5, Deep linking | Overloaded nav |
| 10 | Charts & Data | Legends, Tooltips, Accessible colors | Color-only meaning |

## Sub-skills

- `brand` — brand identity, voice, assets
- `design-system` — tokens, CSS vars, component specs
- `ui-styling` — shadcn/ui + Tailwind implementation
- `banner-design` — social media, ads, web banners
