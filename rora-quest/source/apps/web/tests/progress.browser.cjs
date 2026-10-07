// Contract-fixture acceptance only; does not claim backend/live API acceptance.
// Run against a production web server. Uses optional playwright-core and installed Chrome/Edge.
// NODE_PATH can point to the existing temp tooling. No dependencies are added to the app.
const assert = require("node:assert/strict");
const { chromium } = require("playwright-core");
const fixtures = require("./progress-fixtures.cjs");
const origin = process.env.PROGRESS_TEST_ORIGIN || "http://127.0.0.1:3137";
const headers = { "access-control-allow-origin": origin, "access-control-allow-credentials": "true" };
const byDate = (page, date) => page.locator(`button[data-date="${date}"]`);
async function ready(page, date = "2026-10-06") {
  await page.getByRole("heading", { name: /^Day details/ }).waitFor();
  assert.equal(await byDate(page, date).getAttribute("aria-pressed"), "true");
}
async function noOverflow(page) {
  const measure = await page.evaluate(() => ({ width: innerWidth, content: document.documentElement.scrollWidth }));
  assert.ok(measure.content <= measure.width + 1, JSON.stringify(measure));
}
async function fixture(browser, options = {}) {
  const context = await browser.newContext({
    viewport: { width: options.width ?? 1280, height: 1000 },
    colorScheme: options.theme ?? "light", hasTouch: (options.width ?? 1280) <= 720,
    timezoneId: options.browserZone ?? "America/Los_Angeles", serviceWorkers: "block"
  });
  const page = await context.newPage();
  page.setDefaultTimeout(10000);
  const state = { options, owner: "synthetic-owner-a", requests: [], reports: [], errors: [], bad: null, pageFail: false, delay: null, mutate: null };
  page.on("pageerror", error => state.errors.push(error.message));
  await context.route("**/*", async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    if (!path.startsWith("/api/")) {
      if (url.origin === origin && request.method() === "GET") return route.continue();
      throw new Error(`Unexpected external request ${request.method()} ${url}`);
    }
    assert.equal(request.method(), "GET", "Progress acceptance must not mutate data/auth");
    state.requests.push(`${path}${url.search}`);
    const reply = json => route.fulfill({ json, headers });
    if (path === "/api/auth/me") return reply({ userId: state.owner, displayName: "Synthetic Tester", email: "tester@example.invalid" });
    if (path === "/api/progress") {
      assert.ok([...url.searchParams.keys()].every(k => ["from", "to", "selectedDate", "timeZone"].includes(k)));
      const query = Object.fromEntries(url.searchParams), report = fixtures.report(query, state.options);
      state.reports.push(query);
      const mutate = state.mutate;
      if (mutate) mutate(report, query);
      if (state.delay) await state.delay(query);
      if (state.bad === "status") return route.fulfill({ status: 503, json: { code: "progressUnavailable", message: "Synthetic failure", errors: {} }, headers });
      if (state.bad === "null") return route.fulfill({ contentType: "application/json", body: "null", headers });
      if (state.bad === "malformed") { delete report.coverage; return reply(report); }
      return reply(report);
    }
    if (path === "/api/progress/events") {
      assert.deepEqual([...url.searchParams.keys()], ["cursor"]);
      if (state.pageFail) return route.fulfill({ status: 503, body: "Synthetic continuation failure", headers });
      const response = fixtures.continuation(url.searchParams.get("cursor"), state.options);
      if (state.pageMutate) state.pageMutate(response);
      if (state.pageDelay) await state.pageDelay();
      return reply(response);
    }
    if (path === "/api/tasks" || path === "/api/categories" || path.startsWith("/api/week-confidence/")) return reply([]);
    if (path.startsWith("/api/week-plans/")) return reply({ weekStartDate: path.split("/").pop(), workloadMode: "Yellow", notes: null });
    // Existing Dashboard fixtures, keeping legacy consumers intact.
    if (path === "/api/scorecard") return reply({ plannedTasks: 0, completedTasks: 0, completionRatePercent: 0, carryOverMoved: 0, carryOverPending: 0 });
    if (path === "/api/reports/progress") return reply({ plannedTasks: 3, avgProgressPercent: 25 });
    if (path === "/api/reports/timeline") return reply({ items: [] });
    if (path === "/api/tasks/week-summary") return reply({ weekStart: url.searchParams.get("weekStart"), totalActualHours: 2.5 });
    throw new Error(`Unexpected API request ${url}`);
  });
  return { page, state, async close() { await context.close(); } };
}

(async () => {
  const browser = await chromium.launch({ channel: process.env.PROGRESS_TEST_BROWSER || "chrome", headless: true });
  let passed = 0;
  async function check(name, options, run) {
    const value = await fixture(browser, options);
    try {
      await run(value);
      assert.deepEqual(value.state.errors, [], "No browser runtime errors");
      console.log(`PASS ${name}`); passed++;
    } finally { await value.close(); }
  }
  try {
    await check("default, one primary nav, all five areas, coverage, exact navigation and selection", {}, async ({ page, state }) => {
      await page.goto(`${origin}/progress`); await ready(page);
      assert.equal(await page.locator('nav a[href="/progress"]').count(), 1);
      assert.equal(await page.locator('nav a[href="/scorecard"],nav a[href="/tracking"]').count(), 0);
      assert.equal(await page.locator("button[data-date]").count(), 28);
      assert.equal(await page.locator('[data-status="future"]').count(), 5);
      for (const heading of ["Recent participation", "Weekly plan review", "Activity gaps"]) await page.getByRole("heading", { name: heading, exact: true }).waitFor();
      await page.getByText("2 of 3 currently scheduled tasks complete", { exact: true }).waitFor();
      await page.getByText("Current plan progress: 75% (equal-task mean).", { exact: true }).waitFor();
      await page.getByRole("button", { name: "Previous range", exact: true }).click(); await ready(page, "2026-08-17");
      assert.equal(await page.getByLabel("From (inclusive)").inputValue(), "2026-08-17");
      assert.equal(await page.getByLabel("To (inclusive)").inputValue(), "2026-09-13");
      await page.getByRole("button", { name: "Next range", exact: true }).click(); await ready(page);
      await page.getByRole("button", { name: "Current four weeks", exact: true }).click(); await ready(page);
      assert.equal(state.reports.at(-1).from, undefined);
      const main = await page.locator("main").innerText();
      for (const forbidden of ["Streak & Consistency", "Adaptive Suggestion", "Completion Rate", "Notes scorecard"]) assert.ok(!main.includes(forbidden));
    });
    await check("keyboard, exact gap dates, focus, chronological equivalent, clear and range reset", {}, async ({ page }) => {
      await page.goto(`${origin}/progress`); await ready(page);
      await byDate(page, "2026-10-06").focus();
      await page.keyboard.press("ArrowLeft");
      assert.equal(await page.evaluate(() => document.activeElement.dataset.date), "2026-10-05");
      await page.keyboard.press("Enter"); await ready(page, "2026-10-05");
      assert.equal(await page.evaluate(() => document.activeElement.dataset.date), "2026-10-05");
      const focus = await byDate(page, "2026-10-05").evaluate(el => getComputedStyle(el).outlineStyle);
      assert.notEqual(focus, "none");
      await page.getByRole("button", { name: "Highlight longest gap" }).click(); await ready(page, "2026-10-04");
      assert.deepEqual(await page.locator('[data-gap="true"]').evaluateAll(nodes => nodes.map(n => n.dataset.date)), ["2026-10-04", "2026-10-05"]);
      await page.getByRole("button", { name: "Chronological date list" }).click();
      const dates = await page.locator("button[data-date]").evaluateAll(nodes => nodes.map(n => n.dataset.date));
      assert.deepEqual(dates, [...dates].sort()); assert.equal(dates.length, 28);
      assert.equal(await page.locator('[data-gap="true"]').count(), 2);
      await page.getByRole("button", { name: "Clear highlight" }).click();
      assert.equal(await page.locator('[data-gap="true"]').count(), 0);
      await page.getByRole("button", { name: "Highlight longest gap" }).click(); await ready(page, "2026-10-04");
      await page.getByRole("button", { name: "Previous range", exact: true }).click(); await ready(page, "2026-08-17");
      assert.equal(await page.locator('[data-gap="true"]').count(), 0);
    });
    await check("retained names, removed links, positive intensity, partial detail and all current task states", { allTaskStates: true }, async ({ page }) => {
      await page.goto(`${origin}/progress`); await ready(page);
      const classes = await page.locator('[data-status="active"]').evaluateAll(nodes => nodes.map(n => n.className));
      assert.equal(new Set(classes).size, 1, "1 and 10 units have the same intensity");
      await byDate(page, "2026-10-01").click(); await ready(page, "2026-10-01");
      await page.getByText("Recording coverage was interrupted.", { exact: true }).waitFor();
      await byDate(page, "2026-10-02").click(); await ready(page, "2026-10-02");
      const list = page.getByRole("list", { name: "Recorded completions in occurrence order" });
      assert.equal(await list.locator("li").count(), 10);
      assert.equal(await list.locator('a[href="/tasks/00000000-0000-0000-0000-000000000007"]').count(), 0);
      await list.getByText(/Historical record — task removed/).waitFor();
      assert.ok((await list.innerText()).includes("Historical record — substep removed"));
      assert.ok((await page.locator("main").innerText()).includes("99.99% progress · Progress incomplete"));
      assert.ok((await page.locator("main").innerText()).includes("count-based fallback"));
      assert.ok((await page.locator("main").innerText()).includes("Current status: Skipped"));
      await page.getByRole("button", { name: "View date", exact: true }).click(); await ready(page, "2026-10-02");
      assert.equal(await page.locator("button[data-date]").count(), 1);
    });
    await check("bounded dates, invalid explicit filters, one/84 dates, zone and clipping", {}, async ({ page, state }) => {
      await page.goto(`${origin}/progress?from=2026-02-30&to=2026-03-01`);
      await page.locator("main").getByRole("alert").waitFor();
      assert.equal(state.reports.length, 0);
      await page.getByRole("button", { name: "Current four weeks", exact: true }).click(); await ready(page);
      await page.getByLabel("From (inclusive)").fill("2026-08-01");
      await page.getByLabel("To (inclusive)").fill("2026-10-24");
      await page.getByRole("button", { name: "Apply dates", exact: true }).click();
      await page.getByRole("alert").filter({ hasText: "1–84" }).waitFor();
      assert.equal(state.reports.length, 1, "85 dates never requested");
      await page.getByLabel("To (inclusive)").fill("2026-10-23");
      await page.getByRole("button", { name: "Apply dates", exact: true }).click(); await ready(page);
      assert.equal(await page.locator("button[data-date]").count(), 84);
      await page.getByLabel("Reporting timezone").selectOption("America/New_York"); await ready(page);
      assert.equal(state.reports.at(-1).timeZone, "America/New_York");
      await page.getByLabel("From (inclusive)").fill("2026-10-02");
      await page.getByLabel("To (inclusive)").fill("2026-10-02");
      await page.getByRole("button", { name: "Apply dates", exact: true }).click(); await ready(page, "2026-10-02");
      assert.equal(await page.locator("button[data-date]").count(), 1);
      await page.getByText("This reviews the full Monday–Sunday week, including dates outside the activity range.", { exact: true }).waitFor();
    });
    await check("unknown history, reliable empty history, no-plan and future remain distinct", { empty: true, noPlan: true }, async ({ page }) => {
      await page.goto(`${origin}/progress?from=2026-08-01&to=2026-08-28`); await ready(page, "2026-08-01");
      await page.getByText("Historical coverage is unavailable for this period. This is not a count of days worked.", { exact: true }).waitFor();
      await page.getByText("Not enough fully tracked days.", { exact: true }).waitFor();
      await page.getByText("No currently scheduled tasks in this week.", { exact: true }).waitFor();
      await page.goto(`${origin}/progress?from=2026-10-04&to=2026-10-05`); await ready(page, "2026-10-04");
      assert.equal(await page.locator('[data-status="inactive"]').count(), 2);
      await page.goto(`${origin}/progress?from=2026-10-07&to=2026-10-11`); await ready(page, "2026-10-07");
      assert.equal(await page.locator('[data-status="future"]').count(), 5);
    });
    await check("temporary redirects share parser, preserve valid dates, visibly reset invalid filters", {}, async ({ page, state }) => {
      for (const path of ["scorecard", "tracking"]) {
        const response = await page.request.get(`${origin}/${path}?from=2026-10-01&to=2026-10-02&rangeType=Custom`, { maxRedirects: 0 });
        assert.equal(response.status(), 307);
        assert.equal(response.headers().location, "/progress?from=2026-10-01&to=2026-10-02");
        await page.goto(`${origin}/${path}?from=2026-10-01&from=2026-10-01&to=2026-10-02`);
        await ready(page);
        await page.getByText("The old filters could not be applied; showing the current four weeks.", { exact: true }).waitFor();
        assert.ok(state.requests.filter(r => r.startsWith("/api/progress")).every(r => !r.includes("notice") && !r.includes("rangeType")));
      }
    });
    await check("Dashboard remains separate and consumes unchanged legacy API paths", {}, async ({ page, state }) => {
      await page.goto(`${origin}/progress`); await ready(page);
      await page.locator('nav a[href="/dashboard"]').click();
      await page.getByRole("heading", { name: "Dashboard", exact: true }).waitFor();
      await page.getByText("2.5h", { exact: true }).waitFor();
      for (const path of ["/api/scorecard?", "/api/reports/progress?", "/api/reports/timeline?", "/api/tasks/week-summary?"]) {
        assert.ok(state.requests.some(request => request.startsWith(path)), path);
      }
    });
    await check("error/null/malformed never successful empty; retry clears error", {}, async ({ page, state }) => {
      for (const bad of ["status", "null", "malformed"]) {
        state.bad = bad;
        await page.goto(`${origin}/progress`);
        await page.getByRole("heading", { name: "Progress unavailable" }).waitFor();
        assert.equal(await page.locator("button[data-date]").count(), 0);
        assert.equal(await page.getByRole("heading", { name: "Recent participation" }).count(), 0);
        state.bad = null;
        await page.getByRole("button", { name: "Retry Progress" }).click(); await ready(page);
      }
    });
    await check("paged continuation retry, snapshot mismatch rejection, all 205 records reachable", { eventCount: 205 }, async ({ page, state }) => {
      await page.goto(`${origin}/progress?from=2026-10-02&to=2026-10-02`); await ready(page, "2026-10-02");
      await page.getByText("Showing 100 of 205 recorded units.", { exact: true }).waitFor();
      state.pageFail = true;
      await page.getByRole("button", { name: "Load more records", exact: true }).click();
      await page.getByRole("button", { name: "Retry more records", exact: true }).waitFor();
      assert.equal(await page.getByRole("list", { name: "Recorded completions in occurrence order" }).locator("li").count(), 100);
      state.pageFail = false; state.pageMutate = value => { value.snapshotSequence = "999"; };
      await page.getByRole("button", { name: "Retry more records", exact: true }).click();
      await page.getByRole("alert").filter({ hasText: "snapshot" }).waitFor();
      state.pageMutate = null;
      await page.getByRole("button", { name: "Retry more records", exact: true }).click();
      await page.getByText("Showing 200 of 205 recorded units.", { exact: true }).waitFor();
      await page.getByRole("button", { name: "Load more records", exact: true }).click();
      await page.getByText("Showing 205 of 205 recorded units.", { exact: true }).waitFor();
      assert.equal(await page.getByRole("button", { name: "Load more records", exact: true }).count(), 0);
    });
    await check("day/range/zone races clear stale UI and delayed results cannot win", {}, async ({ page, state }) => {
      await page.goto(`${origin}/progress`); await ready(page);
      let release;
      const gate = new Promise(resolve => { release = resolve; });
      let started;
      const requested = new Promise(resolve => { started = resolve; });
      state.delay = async query => { if (query.selectedDate === "2026-10-02") { started(); await gate; } };
      await byDate(page, "2026-10-02").click(); await requested;
      assert.equal(await page.getByRole("heading", { name: "Recent participation" }).count(), 0);
      await page.getByLabel("Reporting timezone").selectOption("UTC");
      // Default-zone change drops selectedDate, even while a day request is in flight.
      await ready(page);
      release(); state.delay = null;
      await page.getByText("2026-09-14 through 2026-10-11", { exact: true }).waitFor();
      assert.equal(await byDate(page, "2026-10-06").getAttribute("aria-pressed"), "true");
      assert.equal(await page.getByLabel("Reporting timezone").inputValue(), "UTC");
    });
    await check("late continuation cannot append to a newly selected day", { eventCount: 205 }, async ({ page, state }) => {
      await page.goto(`${origin}/progress`); await ready(page);
      await byDate(page, "2026-10-02").click(); await ready(page, "2026-10-02");
      let release, started;
      const gate = new Promise(resolve => { release = resolve; });
      const requested = new Promise(resolve => { started = resolve; });
      state.pageDelay = async () => { started(); await gate; };
      await page.getByRole("button", { name: "Load more records", exact: true }).click(); await requested;
      await byDate(page, "2026-10-01").click(); await ready(page, "2026-10-01");
      release(); state.pageDelay = null;
      await page.getByText("Showing 1 of 1 recorded units.", { exact: true }).waitFor();
      assert.equal(await page.getByRole("list", { name: "Recorded completions in occurrence order" }).locator("li").count(), 1);
      assert.equal(await page.getByRole("button", { name: "Load more records", exact: true }).count(), 0);
    });
    await check("owner change while reading/paging removes old-owner state; focus refresh checks new context", { eventCount: 205 }, async ({ page, state }) => {
      let release, started;
      const gate = new Promise(resolve => { release = resolve; });
      const requested = new Promise(resolve => { started = resolve; });
      state.delay = async () => { started(); await gate; };
      await page.goto(`${origin}/progress`); await requested;
      state.owner = "synthetic-owner-b"; release();
      await page.getByText("Account changed while loading. Retry for the current account.", { exact: true }).waitFor();
      assert.equal(await page.locator("button[data-date]").count(), 0);
      state.delay = null;
      await page.getByRole("button", { name: "Retry Progress" }).click(); await ready(page);
      await byDate(page, "2026-10-02").click(); await ready(page, "2026-10-02");
      state.owner = "synthetic-owner-c";
      await page.getByRole("button", { name: "Load more records", exact: true }).click();
      await page.getByText("Account changed. Retry for the current account.", { exact: true }).waitFor();
      assert.equal(await page.getByRole("list", { name: "Recorded completions in occurrence order" }).count(), 0);
      await page.evaluate(() => window.dispatchEvent(new Event("focus"))); await ready(page);
    });
    await check("server-relative midnight refresh follows default week but preserves explicit historical selection", {}, async ({ page, state }) => {
      await page.clock.install({ time: new Date("2030-01-01T00:00:00Z") });
      // Browser clock/timezone intentionally unrelated. Advance exactly the server-provided delay.
      await page.clock.pauseAt(new Date("2030-01-01T00:00:01Z"));
      await page.goto(`${origin}/progress`); await ready(page);
      state.options = { ...state.options, today: "2026-10-12" };
      await page.clock.runFor((11 * 60 + 59) * 60 * 1000); await ready(page, "2026-10-12");
      assert.equal(await page.getByLabel("From (inclusive)").inputValue(), "2026-09-21");
      assert.equal(state.reports.at(-1).from, undefined);
      await page.goto(`${origin}/progress?from=2026-10-01&to=2026-10-02&selectedDate=2026-10-02`); await ready(page, "2026-10-02");
      state.options = { ...state.options, today: "2026-10-13" };
      await page.clock.runFor((11 * 60 + 59) * 60 * 1000); await ready(page, "2026-10-02");
      assert.equal(await page.getByLabel("From (inclusive)").inputValue(), "2026-10-01");
      assert.equal(state.reports.at(-1).selectedDate, "2026-10-02");
    });
    await check("explicit empty Tasks handoff initializes first request and same-page query never autojumps", { noPlan: true }, async ({ page, state }) => {
      await page.goto(`${origin}/progress?from=2027-06-09&to=2027-06-09`); await ready(page, "2027-06-09");
      await page.getByRole("link", { name: "Open this week in Tasks by Week", exact: true }).click();
      await page.getByLabel("Choose week", { exact: true }).waitFor();
      assert.equal(await page.getByLabel("Choose week", { exact: true }).inputValue(), "2027-06-07");
      await page.waitForFunction(() => !document.body.textContent.includes("Loading week…"));
      assert.deepEqual(state.requests.filter(r => r.startsWith("/api/tasks")), ["/api/tasks?weekStart=2027-06-07"]);
      await page.evaluate(() => window.history.pushState(null, "", "/tasks?weekStart=2027-06-14"));
      await page.waitForFunction(() => document.querySelector('input[aria-label="Choose week"]')?.value === "2027-06-14");
      await page.waitForFunction(() => !document.body.textContent.includes("Loading week…"));
      assert.ok(state.requests.includes("/api/tasks?weekStart=2027-06-14"));
      assert.ok(!state.requests.includes("/api/tasks"), "No unfiltered autojump lookup");
      await page.goto(`${origin}/tasks?weekStart=2027-06-15`);
      await page.getByText(/The requested week is invalid/).waitFor();
    });
    for (const theme of ["light", "dark"]) {
      await check(`320px touch, ${theme}, long names and 200% zoom`, { width: 320, theme, longNames: true }, async ({ page }) => {
        await page.goto(`${origin}/progress`); await ready(page);
        await noOverflow(page);
        for (const button of await page.locator("button[data-date]").all()) {
          const bounds = await button.boundingBox();
          assert.ok(bounds.width >= 24 && bounds.height >= 24, `Touch target ${JSON.stringify(bounds)}`);
        }
        await byDate(page, "2026-10-02").tap(); await ready(page, "2026-10-02");
        await noOverflow(page);
        await page.getByRole("button", { name: "Chronological date list" }).tap();
        await noOverflow(page);
        await page.setViewportSize({ width: 640, height: 1200 });
        await page.evaluate(() => { document.documentElement.style.zoom = "2"; });
        await noOverflow(page);
        await page.getByRole("button", { name: "Calendar", exact: true }).click();
        await noOverflow(page);
      });
    }
    console.log(`Progress fixture browser acceptance: ${passed} scenarios passed. Not live API acceptance.`);
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
