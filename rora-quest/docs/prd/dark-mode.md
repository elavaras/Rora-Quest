# PRD: Dark Mode Support

## Problem
The Rora Quest web app uses fixed light-theme colors. Users who prefer dark interfaces
(low-light environments, OS dark mode preference, eye strain reduction) have no way to
change the visual theme, leading to reduced comfort during extended use.

## Target users
- Daily active users who spend long sessions in the app
- Users with system-level dark mode preference set on their OS or browser
- Users working in low-light or night-time environments

## User stories

> Issues to be created manually (EMU policy blocks API creation in this environment):

| # | Story | Issue |
|---|-------|-------|
| 1 | As a user, I can select Light, Dark, or System from the Settings page. | TBD |
| 2 | As a user, my chosen theme persists after I close and reopen the browser tab. | TBD |
| 3 | As a user, if I have never set a preference, the app follows my system preference automatically. | TBD |

## Functional requirements
1. Add a Theme preference selector in Settings with three options: System, Light, Dark.
2. Apply selected theme immediately without a full page reload.
3. Persist the theme preference in browser localStorage under the key `rora-theme`.
4. On load, read the stored preference and apply before first paint to avoid flash.
5. In System mode, listen to prefers-color-scheme and apply correct theme.
6. Apply dark tokens to all shared layout primitives: body, sidebar, top bar, cards,
   inputs, buttons, muted text, status pills, task cards, modal overlays.

## Non-functional requirements
- Theme switch must update visually in under 100 ms.
- First paint must not show the wrong theme (no unthemed flash on load).
- All themed text and interactive controls must meet WCAG AA contrast in both themes.
- No back-end change required for this release; preference is client-side only.

## Acceptance criteria
1. User can select System, Light, or Dark from Settings.
2. Selecting a theme applies it immediately across all visible UI.
3. Refreshing the page restores the previously selected theme.
4. In System mode, switching the OS/browser dark-mode setting changes the app theme.
5. Core surfaces (sidebar, cards, forms, buttons, muted text) are legible in dark mode.
6. Invalid or missing localStorage value defaults to System mode without error.

## Edge cases
- localStorage is unavailable (private browsing) -> silently default to System.
- User switches OS preference while app is open in System mode -> app updates live.
- Unsupported stored value (e.g. corrupted string) -> fall back to System.

## Non-goals
- Per-component custom color themes.
- User-defined palette editing.
- Server-side or per-identity persistence (deferred to a future release).
- Chart-specific dark-mode color tuning (tracked as follow-up).

## Open questions
- Should theme sync across tabs via storage events? (Assumption: no, for this release.)
- Should theme preference move to server profile settings in a later milestone?

## Success metrics
- Theme preference adoption rate (% of users who switch from default System).
- Zero critical accessibility regressions measured in post-release review.
- No P1 bug reports related to unreadable text or unthemed flash.

## Approval
- Product Manager approval: Pending human review
- Human product owner approval: Required before implementation begins
- Approved at: TBD
