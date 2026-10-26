// Run against the built app: npm run start -- --hostname 127.0.0.1 --port 3137.
// Uses optional playwright-core and installed Chrome. All API traffic is synthetic.
const assert = require("node:assert/strict");
const { chromium } = require("playwright-core");
const origin = process.env.TASK_SEARCH_TEST_ORIGIN || "http://127.0.0.1:3137";
const headers = {
  "access-control-allow-origin": origin,
  "access-control-allow-credentials": "true",
  "access-control-allow-headers": "content-type"
};
const categories = [
  { id: "dsa", name: "DSA", parentCategoryId: null },
  { id: "arrays", name: "Arrays", parentCategoryId: "dsa" },
  { id: "baking", name: "Baking", parentCategoryId: null },
  { id: "cakes", name: "Cakes", parentCategoryId: "baking" }
];
const task = (id, title, overrides = {}) => ({
  id, title, categoryId: null, subCategoryId: null, status: "Todo",
  plannedWeekStart: "2026-10-05", plannedDate: null,
  subSteps: [], actualHours: null, ...overrides
});
const cakes = task("00000000-0000-4000-8000-000000000001", "Bake Cakes",
  { categoryId: "baking", subCategoryId: "cakes", plannedWeekStart: "2024-01-01", status: "Done" });
const two = task("00000000-0000-4000-8000-000000000002", "Two Sum",
  { categoryId: "dsa", subCategoryId: "arrays", plannedDate: "2026-10-05" });
const three = task("00000000-0000-4000-8000-000000000003", "Three Sum",
  { categoryId: "arrays", plannedWeekStart: "2027-01-04", status: "InProgress" });
const bulk = Array.from({ length: 137 }, (_, i) => task(`bulk-${i}`, `Catalog match ${i}`, {
  categoryId: "dsa", subCategoryId: "arrays",
  status: ["Todo", "InProgress", "Done", "Cancelled", "Skipped"][i % 5],
  plannedWeekStart: i % 2 ? "2024-01-01" : "2027-01-04"
}));
const responses = {
  cakes: [cakes], "two sum": [two], "three sum": [three],
  dsa: [two, three], arrays: [two, three], baking: [cakes], catalog: bulk,
  literal: [task("literal", "<script>alert('task')</script> & Cakes")],
  long: [task("long", "Long task ".repeat(45), { categoryId: "arrays" })]
};

async function fixture(browser, { width = 1280, dark = false, ignoreAbort = false } = {}) {
  const context = await browser.newContext({
    viewport: { width, height: 900 }, hasTouch: width < 720,
    colorScheme: dark ? "dark" : "light", serviceWorkers: "block"
  });
  const page = await context.newPage();
  page.setDefaultTimeout(8000);
  // In race scenarios emulate a transport that finishes even after cancellation.
  // This tests the component's signal guards, not only the browser's fetch abort.
  await context.addInitScript(({ ignoreAbort }) => {
    const originalFetch = window.fetch.bind(window);
    window.searchTestRequests = [];
    window.fetch = async (input, init) => {
      const url = new URL(typeof input === "string" ? input : input.url, location.href);
      if (!init?.signal || !["/api/tasks", "/api/categories"].includes(url.pathname)) {
        return originalFetch(input, init);
      }
      const record = { id: window.searchTestRequests.length, path: url.pathname, state: "pending" };
      window.searchTestRequests.push(record);
      try {
        const response = await originalFetch(input, ignoreAbort ? { ...init, signal: undefined } : init);
        await response.clone().text();
        record.state = "resolved";
        return response;
      } catch (error) {
        record.state = "rejected";
        throw error;
      }
    };
  }, { ignoreAbort });
  const errors = [];
  const requests = [];
  let categoryFailure = false;
  let failure = false;
  let nextHold = null;
  const holds = [];
  page.on("pageerror", error => errors.push(error.message));
  await context.route("**/*", async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (!url.pathname.startsWith("/api/")) {
      assert.equal(url.origin, origin, "No external traffic");
      return route.continue();
    }
    if (request.method() === "OPTIONS") return route.fulfill({ status: 204, headers });
    assert.equal(request.method(), "GET", "Search must not issue mutations");
    requests.push(url);
    const reply = json => route.fulfill({ json, headers });
    if (nextHold?.path === url.pathname) {
      const gate = nextHold;
      nextHold = null;
      gate.arrive();
      await gate.released;
      if (gate.fail) return route.fulfill({ status: 503, body: "Old request failed", headers });
    }
    if (url.pathname === "/api/auth/me") return reply({ userId: "synthetic", displayName: "Tester", email: null });
    if (url.pathname === "/api/categories") {
      return categoryFailure ? route.fulfill({ status: 503, body: "private details", headers }) : reply(categories);
    }
    if (url.pathname === "/api/tasks") {
      if (url.searchParams.has("weekStart")) return reply([two]);
      assert.deepEqual([...url.searchParams.keys()], ["search"], "No week/status/result cap in search");
      const query = url.searchParams.get("search");
      if (failure) return route.fulfill({ status: 503, body: "private details", headers });
      return reply(responses[query.toLowerCase()] || []);
    }
    if (url.pathname.startsWith("/api/week-plans/")) return reply({ workloadMode: "Yellow", notes: null });
    if (url.pathname.startsWith("/api/week-confidence/")) return reply([]);
    throw new Error(`Unexpected API request: ${url}`);
  });
  return {
    page, errors, requests,
    setFailure(value) { failure = value; },
    setCategoryFailure(value) { categoryFailure = value; },
    holdNext(path = "/api/tasks", fail = false) {
      let arrive, release;
      const arrived = new Promise(resolve => { arrive = resolve; });
      const released = new Promise(resolve => { release = resolve; });
      const gate = { path, fail, arrive, released, release };
      holds.push(gate);
      nextHold = gate;
      let requestId;
      return {
        async waitForArrival() {
          let timeout;
          try {
            await Promise.race([
              arrived,
              new Promise((_, reject) => {
                timeout = setTimeout(() => reject(new Error(`No held request reached ${path}`)), 8000);
              })
            ]);
          } finally { clearTimeout(timeout); }
          requestId = await page.evaluate(path => window.searchTestRequests
            .findLast(request => request.path === path && request.state === "pending").id, path);
        },
        async releaseAndSettle() {
          assert.notEqual(requestId, undefined, "Latch the held request before releasing");
          release();
          await page.waitForFunction(id => window.searchTestRequests
            .some(request => request.id === id && request.state !== "pending"), requestId);
          // Let promise continuations and React's resulting paint finish.
          await page.evaluate(() => new Promise(resolve =>
            requestAnimationFrame(() => requestAnimationFrame(resolve))));
        }
      };
    },
    async close() { for (const gate of holds) gate.release(); await context.close(); }
  };
}

const input = page => page.getByRole("searchbox", { name: "Task, category, or subcategory name" });
const list = page => page.getByRole("list", { name: "Matching tasks" });
async function search(page, text) {
  await input(page).fill(text);
  await input(page).press("Enter");
}
async function count(page, expected) {
  await page.getByRole("status").filter({ hasText: `${expected} task${expected === 1 ? "" : "s"} found` }).waitFor();
  assert.equal(await list(page).getByRole("listitem").count(), expected);
}

(async () => {
  const browser = await chromium.launch({ channel: process.env.TASK_SEARCH_TEST_BROWSER || "chrome", headless: true });
  let passed = 0;
  async function check(name, options, run) {
    const f = await fixture(browser, options);
    try {
      await run(f);
      assert.deepEqual(f.errors, []);
      console.log(`PASS ${name}`);
      passed++;
    } finally { await f.close(); }
  }
  try {
    await check("both integrated entry points, names, context, and retained task/week links", {}, async ({ page, requests }) => {
      await page.goto(`${origin}/tasks?weekStart=2026-10-05`);
      await page.getByRole("link", { name: "Search tasks across all weeks" }).click();
      await input(page).waitFor();
      assert.ok(page.url().endsWith("/tasks/search"));
      assert.equal(await page.getByRole("navigation").getByRole("link", { name: "Search Tasks", exact: true }).getAttribute("href"), "/tasks/search");
      assert.equal(requests.filter(url => url.searchParams.has("search")).length, 0);
      for (const [query, match, context] of [
        ["  CAKES  ", cakes, "Baking / Cakes"], ["two sum", two, "DSA / Arrays"], ["three sum", three, "DSA / Arrays"]
      ]) {
        await search(page, query);
        await count(page, 1);
        assert.equal(await list(page).getByRole("link", { name: match.title, exact: true }).getAttribute("href"), `/tasks/${match.id}`);
        assert.equal(await list(page).getByText(context, { exact: true }).count(), 1);
        assert.equal(await list(page).getByRole("link", { name: `Week of ${match.plannedWeekStart}` }).getAttribute("href"),
          `/tasks?weekStart=${match.plannedWeekStart}`);
      }
      assert.equal(new URL(page.url()).search, "", "Do not persist search text in URL");
      await page.getByRole("link", { name: "Back to Tasks by Week" }).click();
      await page.getByRole("navigation").getByRole("link", { name: "Search Tasks", exact: true }).click();
      await input(page).waitFor();
      assert.equal(await input(page).inputValue(), "");
    });

    await check("category/subcategory results and uncapped complete multi-week/status list", {}, async ({ page }) => {
      await page.goto(`${origin}/tasks/search`);
      for (const query of ["dsa", "arrays"]) {
        await search(page, query);
        await count(page, 2);
        assert.deepEqual(await list(page).getByRole("listitem").locator("a:first-child").allTextContents(), ["Two Sum", "Three Sum"]);
      }
      await search(page, "catalog");
      await count(page, 137);
      assert.equal(await list(page).getByRole("link", { name: "Catalog match 136", exact: true }).count(), 1);
      for (const status of ["Todo", "InProgress", "Done", "Cancelled", "Skipped"]) {
        assert.ok(await list(page).getByText(status, { exact: true }).count() > 0);
      }
      assert.equal(await list(page).getByText("No planned day", { exact: true }).count(), 137);
    });

    await check("blank, no matches, loading, task/category errors, retry and clear", {}, async f => {
      const { page, requests } = f;
      await page.goto(`${origin}/tasks/search`);
      await input(page).fill(" \t ");
      assert.ok(await page.getByRole("button", { name: "Search", exact: true }).isDisabled());
      assert.equal(requests.filter(url => url.pathname === "/api/tasks").length, 0);
      await search(page, "missing");
      await page.getByRole("status").filter({ hasText: "No tasks found" }).waitFor();
      assert.equal(await list(page).count(), 0);
      for (const kind of ["tasks", "categories"]) {
        f.setFailure(kind === "tasks");
        f.setCategoryFailure(kind === "categories");
        await search(page, "cakes");
        await page.getByRole("alert").waitFor();
        assert.match(await page.getByRole("alert").textContent(), /status 503/);
        assert.equal(await list(page).count(), 0);
        assert.equal(await page.getByText("private details", { exact: true }).count(), 0);
      }
      f.setFailure(false);
      f.setCategoryFailure(false);
      const gate = f.holdNext();
      await search(page, "cakes");
      await gate.waitForArrival();
      await page.getByRole("status").filter({ hasText: "Searching all weeks" }).waitFor();
      await gate.releaseAndSettle();
      await count(page, 1);
      await page.getByRole("button", { name: "Clear", exact: true }).click();
      assert.equal(await input(page).inputValue(), "");
      assert.ok(await input(page).evaluate(element => element === document.activeElement));
      assert.equal(await list(page).count(), 0);
      await page.getByRole("status").filter({ hasText: "Enter a task" }).waitFor();
    });

    for (const [path, fail, action] of [
      ["/api/tasks", false, "resubmit"], ["/api/tasks", true, "resubmit"],
      ["/api/categories", false, "resubmit"], ["/api/categories", true, "resubmit"],
      ["/api/tasks", false, "clear"], ["/api/categories", false, "edit"],
      ["/api/tasks", true, "leave"]
    ]) {
      await check(`settled stale ${path} ${fail ? "failure" : "success"} after ${action}`, { ignoreAbort: true }, async f => {
        const { page } = f;
        await page.goto(`${origin}/tasks/search`);
        const gate = f.holdNext(path, fail);
        await search(page, "cakes");
        await gate.waitForArrival();
        if (action === "resubmit") {
          await search(page, "two sum");
          await count(page, 1);
        } else if (action === "clear") {
          await page.getByRole("button", { name: "Clear", exact: true }).click();
        } else if (action === "edit") {
          await input(page).fill("three");
        } else {
          await page.getByRole("link", { name: "Back to Tasks by Week" }).click();
          await page.getByRole("navigation").getByRole("link", { name: "Search Tasks", exact: true }).click();
          await input(page).waitFor();
        }
        // Do not issue another search to mask an obsolete completion.
        await gate.releaseAndSettle();
        assert.equal(await page.getByRole("alert").count(), 0);
        if (action === "resubmit") {
          assert.equal(await input(page).inputValue(), "two sum");
          assert.equal(await list(page).getByRole("listitem").count(), 1);
          assert.equal(await list(page).getByRole("link", { name: "Two Sum", exact: true }).count(), 1);
          assert.match(await page.getByRole("status").filter({ hasText: "found" }).textContent(), /“two sum”/);
        } else {
          assert.equal(await list(page).count(), 0);
          assert.equal(await input(page).inputValue(), action === "edit" ? "three" : "");
          assert.equal(await page.getByRole("status").filter({ hasText: "Enter a task" }).count(), 1);
        }
      });
    }

    await check("literal result and query text is escaped", {}, async f => {
      const { page } = f;
      await page.goto(`${origin}/tasks/search`);
      await search(page, "literal");
      await count(page, 1);
      assert.equal(await list(page).getByRole("link", { name: "<script>alert('task')</script> & Cakes", exact: true }).count(), 1);
      assert.equal(await list(page).locator("script").count(), 0);
      await search(page, "<img src=x>&%+");
      await page.getByRole("status").filter({ hasText: "No tasks found" }).waitFor();
      assert.equal(await page.getByRole("status").locator("img").count(), 0);
      assert.equal(f.requests.filter(url => url.pathname === "/api/tasks").at(-1).searchParams.get("search"), "<img src=x>&%+");
    });

    for (const width of [320, 1280]) for (const dark of [false, true]) {
      await check(`keyboard, error contrast and wrapped results at ${width}px in ${dark ? "dark" : "light"}`, { width, dark }, async f => {
        const { page } = f;
        await page.goto(`${origin}/tasks/search`);
        await search(page, "long");
        await count(page, 1);
        assert.equal(await page.locator("html").getAttribute("data-theme"), dark ? "dark" : "light");
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth));
        for (const locator of [input(page), page.getByRole("button", { name: "Search", exact: true }), list(page)]) {
          const bounds = await locator.boundingBox();
          assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= width + 1);
        }
        await input(page).focus();
        await page.keyboard.press("Tab");
        assert.equal(await page.evaluate(() => document.activeElement.textContent), "Search");
        await page.keyboard.press("Tab");
        assert.equal(await page.evaluate(() => document.activeElement.textContent), "Clear");
        await page.keyboard.press("Enter");
        assert.equal(await input(page).inputValue(), "");
        f.setFailure(true);
        await search(page, "cakes");
        const alert = page.getByRole("alert");
        await alert.waitFor();
        const ratio = await alert.evaluate(element => {
          const luminance = color => {
            const rgb = color.match(/[\d.]+/g).slice(0, 3).map(Number).map(c => c / 255);
            const linear = rgb.map(c => c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4);
            return linear[0] * 0.2126 + linear[1] * 0.7152 + linear[2] * 0.0722;
          };
          const text = luminance(getComputedStyle(element).color);
          const background = luminance(getComputedStyle(element.closest(".card")).backgroundColor);
          return (Math.max(text, background) + 0.05) / (Math.min(text, background) + 0.05);
        });
        assert.ok(ratio >= 4.5, `Error contrast was ${ratio.toFixed(2)}:1`);
      });
    }
    console.log(`Task search browser checks: ${passed} passed.`);
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
