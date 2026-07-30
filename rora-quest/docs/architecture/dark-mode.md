# Architecture: Dark Mode Support

## PRD reference
`rora-quest/docs/prd/dark-mode.md`

## Approach

CSS custom properties (tokens) + `data-theme` attribute on `<html>`. No third-party theming library. Theme state is owned by a React context, persisted in `localStorage`.

## Token strategy

All theme-sensitive colors are extracted to CSS custom properties defined on `:root` (light defaults) and overridden in `[data-theme="dark"]`.

Brand / semantic colors (`#2563eb` primary blue, `#16a34a` success green, `#dc2626` danger red) are kept hardcoded — they are acceptable in both themes.

## New files

| File | Role |
|------|------|
| `src/app/components/theme-provider.tsx` | `"use client"` — owns theme state, persists to `localStorage`, sets `data-theme` on `document.documentElement` |
| `src/app/components/theme-selector.tsx` | `"use client"` — controlled `<select>` for the Settings page; calls `setTheme` via context |

## Modified files

| File | Change |
|------|--------|
| `src/app/globals.css` | Add `:root` token block; add `[data-theme="dark"]` overrides; replace hardcoded colors with `var(--token)` |
| `src/app/layout.tsx` | Add `suppressHydrationWarning` to `<html>`; inject inline no-flash `<script>` in `<head>`; wrap body with `<ThemeProvider>` |
| `src/app/settings/page.tsx` | Add "Appearance" card with `<ThemeSelector>` |

## No-flash strategy

An inline `<script>` injected into `<head>` runs synchronously before first paint. It reads `localStorage.getItem('rora-theme')` and sets `data-theme` on `document.documentElement` before any CSS renders. `suppressHydrationWarning` on `<html>` suppresses React's hydration mismatch warning (server renders without `data-theme`; client script sets it before React hydrates).

## Theme application flow

```
User selects theme in Settings
  → setTheme('dark')
  → localStorage.setItem('rora-theme', 'dark')
  → document.documentElement.setAttribute('data-theme', 'dark')
  → CSS [data-theme="dark"] vars apply instantly (no re-render needed)
```

In "system" mode, a `matchMedia('(prefers-color-scheme: dark)')` listener updates `data-theme` when the OS preference changes.

## Component tree

```
layout.tsx (server component)
  <html suppressHydrationWarning>
    <head>
      <script>  ← no-flash inline script (sync)
    </head>
    <body>
      <ThemeProvider>  ← client component, owns theme state
        <div class="app-shell">
          <aside class="sidebar">  ← nav (server)
          <ThemeSelector>          ← in settings/page.tsx (client)
        </div>
      </ThemeProvider>
    </body>
  </html>
```

## Token map

| Token | Light | Dark |
|-------|-------|------|
| `--bg-page` | `#f6f8fb` | `#0f172a` |
| `--color-text` | `#111827` | `#f1f5f9` |
| `--bg-sidebar` | `#111827` | `#020617` |
| `--color-sidebar-text` | `#f9fafb` | `#f1f5f9` |
| `--color-nav-link` | `#d1d5db` | `#94a3b8` |
| `--bg-nav-hover` | `#1f2937` | `#1e293b` |
| `--bg-surface` | `#ffffff` | `#1e293b` |
| `--border` | `#e5e7eb` | `#334155` |
| `--border-subtle` | `#f3f4f6` | `#1e293b` |
| `--border-input` | `#d1d5db` | `#475569` |
| `--color-muted` | `#6b7280` | `#94a3b8` |
| `--color-medium` | `#374151` | `#cbd5e1` |
| `--bg-pill` | `#dbeafe` | `#1e3a5f` |
| `--color-pill` | `#1d4ed8` | `#93c5fd` |
| `--bg-chip` | `#e5e7eb` | `#334155` |
| `--color-chip` | `#374151` | `#cbd5e1` |
| `--bg-task-card` | `#fafafa` | `#0f172a` |
| `--bg-modal-overlay` | `rgba(17,24,39,0.45)` | `rgba(2,6,23,0.65)` |
| `--bg-track` | `#e5e7eb` | `#334155` |

## Constraints

- No back-end change required.
- No SSR theme cookie — `localStorage` only.
- Theme switch is pure CSS variable cascade: < 100 ms, no React re-render.
- No flash on load: inline sync script in `<head>`.
- All text / interactive surfaces must meet WCAG AA contrast in dark mode.

## Approval

- Architect approval: Pending human review
- Approved at: TBD
