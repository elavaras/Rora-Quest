# Choosing a task week

On **Tasks by Week**, use **Pick week** to choose any date in the week you want
to view, then select **Show week** (or press Enter in the date field).

- Weeks always run **Monday through Sunday**, including weeks that cross a month
  or year boundary. The picker shows that week's Monday after you apply it.
- The date field follows your browser's date format. Dates are interpreted in
  your local timezone, not converted to UTC.
- **Prev** and **Next** move one week. **This Week** returns to the week containing
  today's local date. The picker follows all three buttons.
- Editing or clearing the field does not change the displayed tasks until you
  submit a valid date. **Show week** is disabled for an empty or invalid date.
  Use the existing navigation buttons to discard a draft and change weeks.
- Tasks, workload, and pattern confidence load for the selected week. Your
  Grid/List choice is retained; bulk task selection resets when the week changes.
- Loading another week clears the previously displayed data. If loading fails,
  the error is shown without presenting the old week's tasks or confidence as
  the selected week's data; workload resets to the default Yellow.
- On first opening the screen, an empty current week may automatically jump to a
  week with planned tasks. Once you explicitly choose a week, an empty week
  remains selected rather than redirecting. Late errors from that canceled
  automatic lookup are ignored too.
- On narrow screens, the sidebar navigation moves above Tasks by Week and the
  week controls wrap to fit. The seven-day task grid can still scroll horizontally.

This only changes which week you view; it does not move tasks or change their
schedules.

## Focused browser regression checks

`source\apps\web\tests\week-navigation.browser.cjs` exercises failed loads,
canceled auto-jump errors, and picker reachability/touch at 320/375/390px against
the production UI. Every API request is mocked; no real tasks are changed.

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
