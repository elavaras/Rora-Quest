// Run against a built, locally served app. All API traffic is synthetic.
// Optional tooling: playwright-core plus installed Chrome (or WEEK_TEST_BROWSER=msedge).
// See docs/task-week-navigation.md. Kept separate from the dependency-free date tests.
const assert = require("node:assert/strict");
const { chromium } = require("playwright-core");

const origin = process.env.WEEK_TEST_ORIGIN || "http://127.0.0.1:3137";
const targetWeek = "2027-06-07";
const headers = {
  "access-control-allow-origin": origin,
  "access-control-allow-credentials": "true"
};

async function fixture(browser, { width = 1440, empty = false, failSelected = false } = {}) {
  const context = await browser.newContext({
    timezoneId: "UTC", locale: "en-US",
    viewport: { width, height: 1000 }, hasTouch: width < 720,
    serviceWorkers: "block"
  });
  const page = await context.newPage();
  page.setDefaultTimeout(8000);
  const pageErrors = [];
  page.on("pageerror", (error) => pageErrors.push(error.message));
  let releaseLookup;
  const lookupGate = new Promise((resolve) => { releaseLookup = resolve; });
  await context.route("**/*", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (!path.startsWith("/api/")) {
      if (url.origin === origin && request.method() === "GET") return route.continue();
      throw new Error(`Unexpected external request: ${request.method()} ${url}`);
    }
    const reply = (json) => route.fulfill({ json, headers });
    if (path === "/api/auth/me") {
      return reply({ userId: "synthetic", displayName: "Tester", email: "tester@example.invalid" });
    }
    if (path === "/api/categories") return reply([]);
    if (path === "/api/tasks" && !url.searchParams.has("weekStart")) {
      await lookupGate;
      return route.fulfill({ status: 500, body: "Canceled lookup failed", headers });
    }
    const week = url.searchParams.get("weekStart") || path.split("/").pop();
    if (path === "/api/tasks") {
      if (failSelected && week === targetWeek) {
        return route.fulfill({ status: 500, body: "Selected week failed", headers });
      }
      return reply(empty ? [] : [{
        id: `fixture-${week}`, title: `Task for ${week}`, status: "Todo",
        plannedWeekStart: week, plannedDate: week, dueDate: null,
        categoryId: null, subCategoryId: null, pattern: null, difficulty: null,
        subSteps: [], actualHours: 1
      }]);
    }
    if (path.startsWith("/api/week-plans/")) {
      return reply({ weekStartDate: week, workloadMode: week === targetWeek ? "Green" : "Red", notes: null });
    }
    if (path.startsWith("/api/week-confidence/")) {
      return reply([{ id: `confidence-${week}`, weekStart: week, label: "Fixture",
        text: `Confidence for ${week}`, isDone: false, orderIndex: 0 }]);
    }
    throw new Error(`Unexpected API request: ${request.method()} ${url}`);
  });
  return {
    page, releaseLookup, pageErrors,
    async close() { releaseLookup(); await context.close(); }
  };
}

async function loaded(page) {
  await page.waitForFunction(() => !document.body.textContent.includes("Loading week…"));
}

async function choose(page, value, touch = false) {
  await page.getByLabel("Pick week", { exact: true }).fill(value);
  const submit = page.getByRole("button", { name: "Show week", exact: true });
  if (touch) await submit.tap();
  else await submit.click();
  await page.waitForFunction((week) =>
    document.querySelector("#task-week-date").value === week, targetWeek);
}

(async () => {
  const browser = await chromium.launch({
    channel: process.env.WEEK_TEST_BROWSER || "chrome", headless: true
  });
  let passed = 0;
  async function check(name, options, run) {
    const test = await fixture(browser, options);
    try {
      await run(test);
      assert.deepEqual(test.pageErrors, []);
      console.log(`PASS ${name}`);
      passed += 1;
    } finally {
      await test.close();
    }
  }
  try {
    await check("failed week load does not relabel old data", { failSelected: true }, async ({ page }) => {
      await page.goto(`${origin}/tasks`);
      await page.locator(".t-title").first().waitFor();
      await choose(page, "2027-06-10");
      await page.getByText("Selected week failed", { exact: true }).waitFor();
      assert.equal(await page.locator(".t-title").count(), 0);
      assert.equal(await page.locator(".confidence-item").count(), 0);
      assert.equal(await page.locator(".mode-select select").inputValue(), "Yellow");
      assert.match(await page.locator(".week-title").textContent(), /Jun 7 – Jun 13, 2027/);
    });

    await check("same-week selection suppresses late auto-jump errors", { empty: true }, async ({ page, releaseLookup }) => {
      const lookup = page.waitForRequest((request) => new URL(request.url()).pathname === "/api/tasks"
        && !new URL(request.url()).searchParams.has("weekStart"));
      await page.goto(`${origin}/tasks`);
      await lookup;
      const input = page.getByLabel("Pick week", { exact: true });
      const monday = await input.inputValue();
      const thursday = new Date(`${monday}T12:00:00Z`);
      thursday.setUTCDate(thursday.getUTCDate() + 3);
      await input.fill(thursday.toISOString().slice(0, 10));
      await page.getByRole("button", { name: "Show week", exact: true }).click();
      await page.waitForFunction((week) => document.querySelector("#task-week-date").value === week, monday);
      const response = page.waitForResponse((result) => new URL(result.url()).pathname === "/api/tasks"
        && !new URL(result.url()).searchParams.has("weekStart"));
      releaseLookup();
      await (await response).finished();
      await loaded(page);
      assert.equal(await input.inputValue(), monday);
      assert.equal(await page.locator(".error-text").count(), 0);
    });

    for (const width of [320, 375, 390]) {
      await check(`week controls fit and work at ${width}px`, { width }, async ({ page }) => {
        await page.goto(`${origin}/tasks`);
        await page.locator(".t-title").first().waitFor();
        await page.locator(".week-toolbar").scrollIntoViewIfNeeded();
        const geometry = await page.evaluate(() => ({
          width: window.innerWidth,
          documentWidth: document.documentElement.scrollWidth,
          controls: [...document.querySelectorAll(".week-nav button, .week-picker input")].map((element) => {
            const rect = element.getBoundingClientRect();
            return { left: rect.left, right: rect.right };
          })
        }));
        assert.ok(geometry.documentWidth <= width, JSON.stringify(geometry));
        for (const control of geometry.controls) {
          assert.ok(control.left >= 0 && control.right <= geometry.width, JSON.stringify(control));
        }
        await choose(page, "2027-06-10", true);
        await page.getByRole("link", { name: `Task for ${targetWeek}`, exact: true }).waitFor();
        await page.getByRole("button", { name: "Next ›", exact: true }).tap();
        await page.waitForFunction(() => document.querySelector("#task-week-date").value === "2027-06-14");
      });
    }
    console.log(`${passed}/5 browser regressions passed`);
  } finally {
    await browser.close();
  }
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
