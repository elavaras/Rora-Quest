# Choosing a task week

On **Tasks by Week**, choose any date in the week you want to view using the
date picker. The displayed week and its tasks update immediately; there is no
separate submit button. The picker has the accessible name **Choose week**.
The picker and both week-navigation arrows share a compact, vertically aligned
row, without a visible label or helper sentence.

- Weeks always run **Monday through Sunday**, including weeks that cross a month
  or year boundary. The picker keeps the date you enter so native day, month,
  and year keyboard editing can continue without resetting to Monday.
- The date field follows your browser's date format. Dates are interpreted in
  your local timezone, not converted to UTC.
- **‹** (Previous week) and **›** (Next week) move exactly one week. Both buttons
  have accessible names and tooltips, and the picker follows either button.
  To return to the current week, choose today's date in the picker.
- Empty or invalid dates do not change the displayed tasks. Selecting a date
  in the already displayed week keeps that date without reloading data.
  Use either navigation button to discard an unfinished date and change weeks.
- Selection refreshes task, workload, and confidence requests using the selected
  Monday (`weekStart=YYYY-MM-DD` for tasks). The browser remains on `/tasks`,
  preserving the existing URL behavior; there is no full-page reload.
- Tasks, workload, and pattern confidence load for the selected week. Your
  Grid/List choice is retained; bulk task selection resets when the week changes.
- Loading another week clears the previously displayed data. If loading fails,
  the error is shown without presenting the old week's tasks or confidence as
  the selected week's data; workload resets to the default Yellow.
- On first opening the screen, an empty current week may automatically jump to a
  week with planned tasks. Once you explicitly choose a week, an empty week
  remains selected rather than redirecting. Late errors from that canceled
  automatic lookup are ignored too.
- On narrow screens, the sidebar navigation moves above the page on all screens,
  keeping the top-right account control reachable. The week controls wrap to fit,
  and the seven-day task grid can still scroll horizontally.

This only changes which week you view; it does not move tasks or change their
schedules.

## Account navigation

The top-right initials button opens an account menu with the signed-in user's
name/email, **Settings**, and **Sign out**. Account details remain hidden when
the menu is closed, and opening it does not move the page content. Signed-out
users see **Sign in**, which continues to use the existing Microsoft sign-in
flow and returns to the current page.

Use Enter, Space, or the arrow keys to open the menu, arrows or Home/End to move
between its actions, and Escape to close it and return focus to the button.
Tab, clicking outside, or navigating away also closes the menu.

## Task tile layout

### Requirements (PRD)

The reported Tasks by Week bug shows a completed "Minimum Window Substring"
task with its progress bar and **Move to** dropdown extending beyond Tuesday
into Wednesday.

Acceptance criteria:

1. Every scheduled tile, its progress bar, and its closed move dropdown stay
   inside the day column, including at the grid's 150px minimum column width.
2. Long titles and subcategory names wrap without hiding text or widening the
   tile. Progress remains accurate for 0%, partially complete, and 100% tasks.
3. Normal and bulk-selection tiles remain contained in light and dark themes.
   The shared unscheduled tiles retain their layout and move controls.
4. Same-week moves, cross-week confirmation, task links, and Grid/List switching
   retain their existing behavior. Narrow screens retain horizontal grid
   scrolling rather than shrinking all seven days into the viewport.

Scope is tile sizing only: no API, scheduling, authentication, or data changes.

### Layout design

The task card's implicit grid column takes its minimum width from its contents,
including the move dropdown's longest option. This makes the progress track and
dropdown wider than the visible card.

Use a single `minmax(0, 1fr)` card column, let the card shrink inside the
bulk-selection flex row, and give its dropdown `width: 100%` and `min-width: 0`.
Allow text to wrap anywhere when a word cannot otherwise fit. Do not clip the
card or hide overflow; links, text, and native dropdown options must stay usable.
Keep the seven-day grid widths and all task handlers unchanged.

Validate rendered element bounds against card and day padding, progress-fill
ratios, long-text wrapping, selection mode, and mocked move interactions. Use
the existing optional Playwright/installed-browser approach; no application
dependency is needed.

## Focused browser regression checks

`source\apps\web\tests\week-navigation.browser.cjs` exercises immediate selection,
accessible symbol-only controls, request URLs, invalid/empty/same-week no-ops,
trusted native day/month/year keyboard editing, year boundaries, supported date
limits, failed loads, canceled auto-jump errors, programmatic picker synchronization,
and control layout/touch at mobile and desktop widths against the production UI.
Every API request is mocked; no real tasks are changed.

From `source\apps\web`, run `npm test` and `npm run build`, then start the app
with `npm run start -- --hostname 127.0.0.1 --port 3137`. In another terminal:

```powershell
# Optional browser tooling, kept outside the repository:
$tools = Join-Path $env:TEMP 'rora-week-browser-tools'
# Only needed if require.resolve('playwright-core') reports MODULE_NOT_FOUND:
npm install --prefix $tools --no-save --no-package-lock playwright-core@1.58.2
$env:NODE_PATH = Join-Path $tools 'node_modules'
node tests\week-navigation.browser.cjs
# Requires installed Chrome; use installed Edge instead with:
$env:WEEK_TEST_BROWSER = 'msedge'
node tests\week-navigation.browser.cjs
```

`WEEK_TEST_ORIGIN` can override the default `http://127.0.0.1:3137`.
These are browser-emulated touch checks, not physical-device or live-backend tests.

Run `node tests\task-tile-layout.browser.cjs` with the same server and browser
tooling for the tile-layout regression suite. It covers light/dark themes at
320/390/768/1280/1920px, normal and bulk-selection tiles (including unscheduled
tasks), long text, 0/50/100% progress, same-week moves, previous/next-week
confirmation and cancellation, task link targets, and Grid/List switching.
The assertions measure rendered bounds and fill widths rather than checking
for particular CSS declarations. All API traffic is mocked.

Set `TASK_TILE_SCREENSHOT_DIR` to save a desktop dark-theme grid screenshot
outside the repository. Native dropdown popup rendering and physical-device
interaction are not covered by these checks.

Run `node tests\user-navigation.browser.cjs` with the same server and optional
browser tooling for the shared account menu. It covers signed-in/out/loading
states, identity fallbacks, keyboard and touch interaction, dismissal/navigation,
unchanged login/logout destinations, long names/emails, and 320-1920px layouts
in light/dark themes. All API requests and authentication redirects are mocked.
`USER_NAV_TEST_ORIGIN` and `USER_NAV_TEST_BROWSER` override the server and browser;
`USER_NAV_SCREENSHOT_DIR` optionally saves screenshots outside the repository.
