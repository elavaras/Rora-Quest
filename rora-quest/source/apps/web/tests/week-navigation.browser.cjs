// Run against a built, locally served app. All API traffic is synthetic.
// Optional tooling: playwright-core plus installed Chrome (or WEEK_TEST_BROWSER=msedge).
// See docs/task-week-navigation.md. Kept separate from the dependency-free date tests.
const assert = require("node:assert/strict");
const { chromium } = require("playwright-core");

const origin = process.env.WEEK_TEST_ORIGIN || "http://127.0.0.1:3137";
const initialWeek = "2026-09-28";
const targetWeek = "2027-06-07";
const headers = {
  "access-control-allow-origin": origin,
  "access-control-allow-credentials": "true"
};

async function fixture(browser, { width = 1440, empty = false, failSelected = false, autoJumpWeek = null } = {}) {
  const context = await browser.newContext({
    timezoneId: "UTC", locale: "en-US",
    viewport: { width, height: 1000 }, hasTouch: width < 720,
    serviceWorkers: "block"
  });
  const page = await context.newPage();
  page.setDefaultTimeout(8000);
  await page.clock.setFixedTime(new Date("2026-10-04T12:00:00Z"));
  const pageErrors = [];
  const apiRequests = [];
  const documentRequests = [];
  page.on("pageerror", (error) => pageErrors.push(error.message));
  page.on("request", (request) => {
    if (request.isNavigationRequest()) documentRequests.push(request.url());
  });
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
    assert.equal(request.method(), "GET", `Unexpected API write: ${path}`);
    apiRequests.push(`${path}${url.search}`);
    const reply = (json) => route.fulfill({ json, headers });
    if (path === "/api/auth/me") {
      return reply({ userId: "synthetic", displayName: "Tester", email: "tester@example.invalid" });
    }
    if (path === "/api/categories") return reply([]);
    if (path === "/api/tasks" && !url.searchParams.has("weekStart")) {
      await lookupGate;
      if (autoJumpWeek) return reply([{ plannedWeekStart: autoJumpWeek }]);
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
    page, releaseLookup, pageErrors, apiRequests, documentRequests,
    async close() { releaseLookup(); await context.close(); }
  };
}

async function loaded(page) {
  await page.waitForFunction(() => !document.body.textContent.includes("Loading week…"));
}

async function choose(page, value, touch = false) {
  const input = page.getByLabel("Choose week", { exact: true });
  if (touch) {
    await input.tap();
    await input.press("Escape");
  }
  await input.fill(value);
  assert.equal(await input.inputValue(), value);
}

async function assertWeek(page, week, pickerDate = week) {
  await page.getByRole("link", { name: `Task for ${week}`, exact: true }).waitFor();
  assert.equal(await page.getByLabel("Choose week", { exact: true }).inputValue(), pickerDate);
  assert.equal(await page.locator(".t-title").count(), 1);
  await page.getByText(`Confidence for ${week}`, { exact: true }).waitFor();
}

function weekRequests(week) {
  return [
    `/api/tasks?weekStart=${week}`,
    `/api/week-confidence/${week}`,
    `/api/week-plans/${week}`
  ].sort();
}

async function focusDateSegment(input, segment) {
  // en-US Chromium has month/day/year segments. Left twice reaches month
  // regardless of the segment left selected by fill() or the previous edit.
  await input.focus();
  await input.press("ArrowLeft");
  await input.press("ArrowLeft");
  for (let i = 0; i < ["month", "day", "year"].indexOf(segment); i++) {
    await input.press("ArrowRight");
  }
  await input.evaluate((element) => {
    window.nativeDateEvents = [];
    element.addEventListener("input", (event) => window.nativeDateEvents.push({
      value: event.target.value, isTrusted: event.isTrusted
    }));
  });
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
    await check("week picker refreshes immediately with two accessible symbol-only controls", {}, async ({
      page, apiRequests, documentRequests
    }) => {
      await page.goto(`${origin}/tasks`);
      await assertWeek(page, initialWeek);
      const input = page.getByLabel("Choose week", { exact: true });
      assert.equal(await input.getAttribute("type"), "date");
      assert.equal(await input.getAttribute("aria-describedby"), null);
      assert.equal(await page.locator("#task-week-hint").count(), 0);
      assert.equal(await page.getByText("Choose any date. Weeks run Monday–Sunday.", { exact: true }).count(), 0);
      assert.equal(await input.getAttribute("min"), "0001-01-01");
      assert.equal(await input.getAttribute("max"), "9999-12-26");
      assert.equal(await page.locator(".week-picker label, form.week-picker, .week-picker button").count(), 0);
      assert.equal(await page.getByText("Pick week", { exact: true }).count(), 0);
      assert.equal(await page.getByRole("button", { name: /^(Show week|This Week)$/i }).count(), 0);
      assert.equal(await page.locator(".week-nav button").count(), 2);
      for (const [name, symbol] of [["Previous week", "‹"], ["Next week", "›"]]) {
        const button = page.getByRole("button", { name, exact: true });
        assert.equal((await button.innerText()).trim(), symbol);
        assert.equal(await button.getAttribute("title"), name);
        assert.equal(await button.getAttribute("type"), "button");
        assert.equal(await button.locator('[aria-hidden="true"]').innerText(), symbol);
      }
      // Navigation retains the selected view, but resets bulk task selection.
      await page.getByRole("button", { name: "List", exact: true }).click();
      await page.getByRole("button", { name: "Select", exact: true }).click();
      await page.locator(".bulk-selectall input").check();
      assert.ok(await page.getByRole("button", { name: "Delete selected (1)", exact: true }).isEnabled());
      const requestOffset = apiRequests.length;
      await choose(page, "2027-06-10");
      await assertWeek(page, targetWeek, "2027-06-10");
      assert.deepEqual(apiRequests.slice(requestOffset).sort(), [
        `/api/tasks?weekStart=${targetWeek}`,
        `/api/week-confidence/${targetWeek}`,
        `/api/week-plans/${targetWeek}`
      ]);
      assert.equal(await page.locator(".mode-select select").inputValue(), "Green");
      assert.match(await page.locator(".week-title").textContent(), /Jun 7 – Jun 13, 2027/);
      assert.match(await page.getByRole("button", { name: "List", exact: true }).getAttribute("class"), /active/);
      assert.equal(await page.locator(".bulk-toolbar").count(), 0);
      assert.equal(await page.getByRole("button", { name: "Select", exact: true }).count(), 1);
      assert.equal(page.url(), `${origin}/tasks`);
      assert.deepEqual(documentRequests, [`${origin}/tasks`], "Selection must refresh data, not the document");
    });

    await check("empty, invalid and same-week dates do not reload or navigate", {}, async ({
      page, apiRequests, documentRequests
    }) => {
      await page.goto(`${origin}/tasks`);
      await assertWeek(page, initialWeek);
      const input = page.getByLabel("Choose week", { exact: true });
      const requestsBefore = [...apiRequests];
      for (const value of ["", "9999-12-27"]) {
        await input.fill(value);
        await page.waitForTimeout(150);
        assert.deepEqual(apiRequests, requestsBefore, `No requests for ${JSON.stringify(value)}`);
        assert.equal(await input.inputValue(), value);
        await page.getByRole("link", { name: `Task for ${initialWeek}`, exact: true }).waitFor();
      }
      // Native date inputs sanitize malformed/impossible dates to an empty value.
      for (const value of ["2026-02-30", "not-a-date"]) {
        await input.evaluate((element, invalid) => {
          const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set;
          setter.call(element, invalid);
          element.dispatchEvent(new Event("input", { bubbles: true }));
          element.dispatchEvent(new Event("change", { bubbles: true }));
        }, value);
        await page.waitForTimeout(150);
        assert.equal(await input.inputValue(), "");
        assert.deepEqual(apiRequests, requestsBefore, `No requests for ${value}`);
      }
      await choose(page, "2026-10-01");
      await input.press("Enter");
      await assertWeek(page, initialWeek, "2026-10-01");
      await choose(page, initialWeek);
      await page.waitForTimeout(150);
      await assertWeek(page, initialWeek);
      assert.deepEqual(apiRequests, requestsBefore, "Same-week selection must not refetch");
      assert.equal(await page.locator(".error-text").count(), 0);
      assert.deepEqual(documentRequests, [`${origin}/tasks`]);
    });

    await check("trusted native day editing crosses a week boundary without resetting or blurring", {}, async ({
      page, apiRequests, documentRequests
    }) => {
      await page.goto(`${origin}/tasks`);
      await assertWeek(page, initialWeek);
      await choose(page, targetWeek);
      await assertWeek(page, targetWeek);
      const input = page.getByLabel("Choose week", { exact: true });
      const requestOffset = apiRequests.length;
      await focusDateSegment(input, "day");
      const dates = [];
      for (let day = 8; day <= 15; day++) {
        const date = `2027-06-${String(day).padStart(2, "0")}`;
        dates.push(date);
        await input.press("ArrowUp");
        // Allow React and the request effects to settle after each native edit.
        await page.waitForTimeout(50);
        await assertWeek(page, day < 14 ? targetWeek : "2027-06-14", date);
        assert.ok(await input.evaluate((element) => document.activeElement === element));
        assert.deepEqual(apiRequests.slice(requestOffset).sort(),
          day < 14 ? [] : weekRequests("2027-06-14"));
      }
      assert.deepEqual(await page.evaluate(() => window.nativeDateEvents),
        dates.map((value) => ({ value, isTrusted: true })));
      assert.deepEqual(documentRequests, [`${origin}/tasks`]);
      await page.getByRole("button", { name: "Previous week", exact: true }).click();
      await assertWeek(page, targetWeek);
    });

    for (const scenario of [
      { segment: "month", start: "2027-01-10", week: "2027-01-04",
        steps: [["2027-02-10", "2027-02-08"], ["2027-03-10", "2027-03-08"]],
        nextWeek: "2027-03-15" },
      { segment: "year", start: "2026-12-31", week: "2026-12-28",
        steps: [["2027-12-31", "2027-12-27"], ["2028-12-31", "2028-12-25"]],
        nextWeek: "2029-01-01" }
    ]) {
      await check(`trusted native ${scenario.segment} edits retain non-Monday dates through week loads`, {}, async ({
        page, apiRequests
      }) => {
        await page.goto(`${origin}/tasks`);
        await assertWeek(page, initialWeek);
        await choose(page, scenario.start);
        await assertWeek(page, scenario.week, scenario.start);
        const input = page.getByLabel("Choose week", { exact: true });
        await focusDateSegment(input, scenario.segment);
        for (const [date, week] of scenario.steps) {
          const requestOffset = apiRequests.length;
          await input.press("ArrowUp");
          await assertWeek(page, week, date);
          assert.ok(await input.evaluate((element) => document.activeElement === element));
          assert.deepEqual(apiRequests.slice(requestOffset).sort(), weekRequests(week));
        }
        assert.deepEqual(await page.evaluate(() => window.nativeDateEvents),
          scenario.steps.map(([value]) => ({ value, isTrusted: true })));
        await page.getByRole("button", { name: "Next week", exact: true }).click();
        await assertWeek(page, scenario.nextWeek);
      });
    }

    await check("previous and next cross the ISO week-year boundary by exactly one week", {}, async ({ page }) => {
      await page.goto(`${origin}/tasks`);
      await assertWeek(page, initialWeek);
      await choose(page, "2027-01-01");
      await assertWeek(page, "2026-12-28", "2027-01-01");
      assert.match(await page.locator(".week-title").textContent(), /Dec 28, 2026 – Jan 3, 2027/);
      await page.getByRole("button", { name: "Next week", exact: true }).click();
      await assertWeek(page, "2027-01-04");
      await page.getByRole("button", { name: "Previous week", exact: true }).focus();
      await page.keyboard.press("Enter");
      await assertWeek(page, "2026-12-28");
      await page.keyboard.press("Space");
      await assertWeek(page, "2026-12-21");
    });

    await check("navigation stops at supported complete-week boundaries", {}, async ({ page }) => {
      await page.goto(`${origin}/tasks`);
      await assertWeek(page, initialWeek);
      const previous = page.getByRole("button", { name: "Previous week", exact: true });
      const next = page.getByRole("button", { name: "Next week", exact: true });
      await choose(page, "0001-01-01");
      await assertWeek(page, "0001-01-01");
      assert.ok(await previous.isDisabled());
      assert.ok(await next.isEnabled());
      await next.click();
      await assertWeek(page, "0001-01-08");
      await choose(page, "9999-12-26");
      await assertWeek(page, "9999-12-20", "9999-12-26");
      assert.ok(await previous.isEnabled());
      assert.ok(await next.isDisabled());
      await previous.click();
      await assertWeek(page, "9999-12-13");
    });

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

    await check("same-week selection suppresses late auto-jump errors", { empty: true }, async ({
      page, releaseLookup, apiRequests
    }) => {
      const lookup = page.waitForRequest((request) => new URL(request.url()).pathname === "/api/tasks"
        && !new URL(request.url()).searchParams.has("weekStart"));
      await page.goto(`${origin}/tasks`);
      await lookup;
      const input = page.getByLabel("Choose week", { exact: true });
      const monday = await input.inputValue();
      const requestsBefore = [...apiRequests];
      const thursday = new Date(`${monday}T12:00:00Z`);
      thursday.setUTCDate(thursday.getUTCDate() + 3);
      const chosenDate = thursday.toISOString().slice(0, 10);
      await choose(page, chosenDate);
      const response = page.waitForResponse((result) => new URL(result.url()).pathname === "/api/tasks"
        && !new URL(result.url()).searchParams.has("weekStart"));
      releaseLookup();
      await (await response).finished();
      await loaded(page);
      assert.equal(await input.inputValue(), chosenDate);
      assert.equal(await page.locator(".error-text").count(), 0);
      assert.deepEqual(apiRequests, requestsBefore, "Canceling auto-jump must not refetch the same week");
    });

    await check("programmatic auto-jump synchronizes an empty picker draft", {
      empty: true, autoJumpWeek: targetWeek
    }, async ({ page, releaseLookup, apiRequests }) => {
      const lookup = page.waitForRequest((request) => new URL(request.url()).pathname === "/api/tasks"
        && !new URL(request.url()).searchParams.has("weekStart"));
      await page.goto(`${origin}/tasks`);
      await lookup;
      const input = page.getByLabel("Choose week", { exact: true });
      await choose(page, "");
      const requestOffset = apiRequests.length;
      releaseLookup();
      await page.getByText(`Confidence for ${targetWeek}`, { exact: true }).waitFor();
      await loaded(page);
      assert.equal(await input.inputValue(), targetWeek);
      assert.match(await page.locator(".week-title").textContent(), /Jun 7 – Jun 13, 2027/);
      assert.deepEqual(apiRequests.slice(requestOffset).sort(), weekRequests(targetWeek));
    });

    for (const width of [320, 375, 390, 720, 1440]) {
      await check(`week controls fit and work at ${width}px`, { width }, async ({ page }) => {
        await page.goto(`${origin}/tasks`);
        await page.locator(".t-title").first().waitFor();
        await page.locator(".week-toolbar").scrollIntoViewIfNeeded();
        const geometry = await page.evaluate(() => ({
          width: window.innerWidth,
          height: window.innerHeight,
          documentWidth: document.documentElement.scrollWidth,
          controls: [...document.querySelectorAll(".week-nav button, .week-picker input")].map((element) => {
            const rect = element.getBoundingClientRect();
            return { left: rect.left, right: rect.right, width: rect.width, height: rect.height,
              top: rect.top, bottom: rect.bottom, centerY: rect.top + rect.height / 2,
              button: element.tagName === "BUTTON" };
          })
        }));
        assert.ok(geometry.documentWidth <= width, JSON.stringify(geometry));
        assert.equal(geometry.controls.length, 3);
        const centers = geometry.controls.map((control) => control.centerY);
        assert.ok(Math.max(...centers) - Math.min(...centers) <= 1,
          `Controls must share one centered row: ${JSON.stringify(geometry)}`);
        for (const control of geometry.controls) {
          assert.ok(control.left >= 0 && control.right <= geometry.width, JSON.stringify(control));
          assert.ok(control.top >= 0 && control.bottom <= geometry.height, JSON.stringify(control));
          assert.ok(control.height >= 44, JSON.stringify(control));
          if (control.button) {
            assert.ok(control.width >= 44, JSON.stringify(control));
          }
        }
        await choose(page, "2027-06-10", width < 720);
        await page.getByRole("link", { name: `Task for ${targetWeek}`, exact: true }).waitFor();
        const next = page.getByRole("button", { name: "Next week", exact: true });
        if (width < 720) await next.tap();
        else await next.click();
        await assertWeek(page, "2027-06-14");
        const previous = page.getByRole("button", { name: "Previous week", exact: true });
        if (width < 720) await previous.tap();
        else await previous.click();
        await assertWeek(page, targetWeek);
      });
    }
    console.log(`${passed}/15 browser regressions passed`);
  } finally {
    await browser.close();
  }
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
