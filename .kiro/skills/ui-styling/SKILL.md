---
name: ui-styling
description: Create beautiful, accessible user interfaces with shadcn/ui components (built on Radix UI + Tailwind), Tailwind CSS utility-first styling. Use when building UI, implementing design systems, creating responsive layouts, adding accessible components, customizing themes, implementing dark mode.
origin: nextlevelbuilder/ui-ux-pro-max-skill
---

# UI Styling Skill

Comprehensive skill for creating accessible user interfaces with shadcn/ui + Tailwind CSS.

## Core Stack

- **shadcn/ui** — Pre-built accessible components via Radix UI primitives, copy-paste model, TypeScript-first
- **Tailwind CSS** — Utility-first CSS, mobile-first responsive, consistent design tokens

## Quick Start

```bash
# Install shadcn/ui with Tailwind
npx shadcn@latest init

# Add components
npx shadcn@latest add button card dialog form
```

## Usage Pattern

```tsx
import { Button } from "@/components/ui/button"
import { Card, CardHeader, CardTitle, CardContent } from "@/components/ui/card"

export function Dashboard() {
  return (
    <div className="container mx-auto p-6 grid gap-6 md:grid-cols-2 lg:grid-cols-3">
      <Card className="hover:shadow-lg transition-shadow">
        <CardHeader>
          <CardTitle className="text-2xl font-bold">Analytics</CardTitle>
        </CardHeader>
        <CardContent>
          <Button variant="default" className="w-full">View Details</Button>
        </CardContent>
      </Card>
    </div>
  )
}
```

## References

- shadcn/ui: https://ui.shadcn.com/llms.txt
- Tailwind CSS: https://tailwindcss.com/docs
