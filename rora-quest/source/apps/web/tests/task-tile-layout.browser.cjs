// Uses the same optional browser tooling as week-navigation.browser.cjs.
// All API traffic is synthetic; no real tasks are read or changed.
const assert = require("node:assert/strict");
const { mkdirSync } = require("node:fs");
const path = require("node:path");
const { chromium } = require("playwright-core");

const origin = process.env.WEEK_TEST_ORIGIN || "http://127.0.0.1:3137";
const week = "2026-09-28";
const longTitle = "MinimumWindowSubstringWithoutAnyWhitespace".repeat(3);
const longSubcategory = "SlidingWindowAndTwoPointerTechniquesWithoutSpaces";
const headers = {
  "access-control-allow-origin": origin,
  "access-control-allow-credentials": "true"
};

async function fixture(browser, width, theme) {
  const context = await browser.newContext({
    timezoneId: "UTC", locale: "en-US", colorScheme: theme,
    viewport: { width, height: 1000 }, hasTouch: width < 720,
    serviceWorkers: "block"
  });
  const page = await context.newPage();
  page.setDefaultTimeout(8000);
  await page.clock.setFixedTime(new Date("2026-10-04T12:00:00Z"));
  const pageErrors = [];
  const moves = [];
  page.on("pageerror", (error) => pageErrors.push(error.message));
  const tasks = [
    { id: "todo", title: "Short task", status: "Todo", plannedDate: week, expectedProgress: 0 },
    { id: "done", title: "Minimum Window Substring", status: "Done", plannedDate: "2026-09-29", expectedProgress: 100 },
    { id: "partial", title: longTitle, status: "InProgress", plannedDate: "2026-10-01", expectedProgress: 50,
      subSteps: [{ id: "a", isDone: true, weight: 1 }, { id: "b", isDone: false, weight: 1 }] },
    { id: "words", title: "A longer task title with several words that must remain readable",
      status: "Cancelled", plannedDate: "2026-10-04", expectedProgress: 0 },
    { id: "unscheduled", title: longTitle, status: "Done", plannedDate: null, expectedProgress: 100 }
  ].map((task) => ({
    plannedWeekStart: week, dueDate: null, categoryId: "category", subCategoryId: "subcategory",
    pattern: null, difficulty: null, subSteps: [], actualHours: 0, ...task
  }));
  await context.route("**/*", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const pathname = url.pathname;
    if (!pathname.startsWith("/api/")) {
      if (url.origin === origin && request.method() === "GET") return route.continue();
      throw new Error(`Unexpected external request: ${request.method()} ${url}`);
    }
    const reply = (json) => route.fulfill({ json, headers });
    if (request.method() === "PATCH" && pathname === "/api/tasks/done") {
      const body = request.postDataJSON();
      moves.push(body);
      Object.assign(tasks.find((task) => task.id === "done"), body);
      return route.fulfill({ status: 204, headers });
    }
    assert.equal(request.method(), "GET", `Unexpected API write: ${pathname}`);
    if (pathname === "/api/auth/me") {
      return reply({ userId: "synthetic", displayName: "Tester", email: "tester@example.invalid" });
    }
    if (pathname === "/api/categories") {
      return reply([
        { id: "category", name: "Algorithms", parentCategoryId: null },
        { id: "subcategory", name: longSubcategory, parentCategoryId: "category" }
      ]);
    }
    if (pathname === "/api/tasks") return reply(tasks);
    if (pathname.startsWith("/api/week-plans/")) {
      return reply({ weekStartDate: week, workloadMode: "Yellow", notes: null });
    }
    if (pathname.startsWith("/api/week-confidence/")) return reply([]);
    throw new Error(`Unexpected API request: ${request.method()} ${url}`);
  });
  await page.goto(`${origin}/tasks`);
  await page.locator(".task-card .t-subcat").first().waitFor();
  await page.evaluate((value) => document.documentElement.dataset.theme = value, theme);
  return { context, page, pageErrors, moves, tasks };
}

async function assertLayout(page, tasks) {
  const geometry = await page.evaluate(() => {
    function bounds(element, content = false) {
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      return {
        left: rect.left + (content ? parseFloat(style.borderLeftWidth) + parseFloat(style.paddingLeft) : 0),
        right: rect.right - (content ? parseFloat(style.borderRightWidth) + parseFloat(style.paddingRight) : 0),
        top: rect.top + (content ? parseFloat(style.borderTopWidth) + parseFloat(style.paddingTop) : 0),
        bottom: rect.bottom - (content ? parseFloat(style.borderBottomWidth) + parseFloat(style.paddingBottom) : 0)
      };
    }
    return {
      viewportWidth: innerWidth,
      documentWidth: document.documentElement.scrollWidth,
      gridWidth: document.querySelector(".week-grid").getBoundingClientRect().width,
      scrollWidth: document.querySelector(".week-grid-wrap").scrollWidth,
      scrollViewport: document.querySelector(".week-grid-wrap").clientWidth,
      cards: [...document.querySelectorAll(".task-card")].map((card) => ({
        id: card.querySelector(".t-title").getAttribute("href").split("/").pop(),
        bounds: bounds(card),
        parent: bounds(card.closest(".day-col") || card.parentElement, true),
        content: bounds(card, true),
        overflow: getComputedStyle(card).overflowX,
        progress: card.querySelector(".t-meta").textContent,
        trackWidth: card.querySelector(".progress-bar").getBoundingClientRect().width,
        fillWidth: card.querySelector(".progress-fill").getBoundingClientRect().width,
        children: [...card.querySelectorAll(".t-title, .t-subcat, .t-meta, .progress-bar, .card-actions, select")]
          .map((element) => ({
            selector: element.className || element.tagName,
            bounds: bounds(element),
            clientWidth: element.clientWidth,
            scrollWidth: element.scrollWidth
          }))
      }))
    };
  });
  const tolerance = 1;
  function contains(parent, child, name) {
    assert.ok(child.left >= parent.left - tolerance && child.right <= parent.right + tolerance
      && child.top >= parent.top - tolerance && child.bottom <= parent.bottom + tolerance,
    `${name}: ${JSON.stringify({ parent, child })}`);
  }
  assert.equal(geometry.cards.length, tasks.length);
  assert.ok(geometry.gridWidth >= 980, "The seven-day grid must retain its minimum width");
  if (geometry.viewportWidth < 720) {
    assert.ok(geometry.documentWidth <= geometry.viewportWidth, "Only the grid should scroll horizontally");
    assert.ok(geometry.scrollWidth > geometry.scrollViewport, "Narrow screens must retain grid scrolling");
    const wrapper = page.locator(".week-grid-wrap");
    await wrapper.evaluate((element) => { element.scrollLeft = element.scrollWidth; });
    assert.ok(await wrapper.evaluate((element) => element.scrollLeft > 0), "The grid must be scrollable");
    await wrapper.evaluate((element) => { element.scrollLeft = 0; });
  }
  for (const card of geometry.cards) {
    contains(card.parent, card.bounds, `${card.id} card inside its container`);
    assert.ok(!["hidden", "clip"].includes(card.overflow), "Do not fix the layout by clipping the card");
    for (const child of card.children) {
      contains(card.content, child.bounds, `${card.id} ${child.selector} inside card padding`);
      assert.ok(child.scrollWidth <= child.clientWidth + tolerance,
        `${card.id} ${child.selector} must wrap rather than overflow`);
    }
    const expected = tasks.find((task) => task.id === card.id).expectedProgress;
    assert.match(card.progress, new RegExp(`\\b${expected}%`));
    assert.ok(card.trackWidth > 0, `${card.id} progress track must remain visible`);
    assert.ok(Math.abs(card.fillWidth - card.trackWidth * expected / 100) <= tolerance,
      `${card.id} progress fill must represent ${expected}%`);
  }
}

(async () => {
  const browser = await chromium.launch({
    channel: process.env.WEEK_TEST_BROWSER || "chrome", headless: true
  });
  let passed = 0;
  try {
    for (const theme of ["dark", "light"]) {
      for (const width of [1920, 1280, 768, 390, 320]) {
        const { context, page, pageErrors, moves, tasks } = await fixture(browser, width, theme);
        try {
          if (process.env.TASK_TILE_SCREENSHOT_DIR && width === 1920 && theme === "dark") {
            mkdirSync(process.env.TASK_TILE_SCREENSHOT_DIR, { recursive: true });
            await page.locator(".week-grid").screenshot({
              path: path.join(process.env.TASK_TILE_SCREENSHOT_DIR, "task-tiles-dark.png")
            });
          }
          await assertLayout(page, tasks);
          await page.getByRole("button", { name: "Select", exact: true }).click();
          await assertLayout(page, tasks);
          await page.locator(".select-box").first().check();
          assert.ok(await page.getByRole("button", { name: "Delete selected (1)", exact: true }).isEnabled());
          await page.getByRole("button", { name: "Cancel", exact: true }).click();

          if (width === 1920) {
            const doneLink = page.getByRole("link", { name: "Minimum Window Substring", exact: true });
            assert.equal(await doneLink.getAttribute("href"), "/tasks/done");
            await page.locator(".task-card").filter({ has: doneLink })
              .getByRole("combobox", { name: "Move task" }).selectOption("2026-09-30");
            await page.locator(".day-col").nth(2).getByRole("link", { name: "Minimum Window Substring", exact: true }).waitFor();
            assert.deepEqual(moves, [{ plannedDate: "2026-09-30" }]);
            const dropdown = page.locator(".task-card").filter({ has: doneLink })
              .getByRole("combobox", { name: "Move task" });
            assert.equal(await dropdown.inputValue(), "");
            for (const [direction, target] of [["prev", "2026-09-21"], ["next", "2026-10-05"]]) {
              await dropdown.selectOption(direction);
              const modal = page.locator(".modal");
              await modal.getByRole("heading", { name: "Move to another week" }).waitFor();
              assert.ok((await modal.textContent()).includes(target));
              assert.ok(await modal.getByRole("button", { name: "Confirm move", exact: true }).isEnabled());
              await modal.getByRole("button", { name: "Cancel", exact: true }).click();
            }
            assert.equal(moves.length, 1, "Canceling cross-week moves must not write data");
            await page.getByRole("button", { name: "List", exact: true }).click();
            assert.equal(await page.locator(".task-row").count(), tasks.length);
            assert.equal(await page.locator(".task-card").count(), 0);
            await page.getByRole("button", { name: "Grid", exact: true }).click();
            await assertLayout(page, tasks);
          }
          assert.deepEqual(pageErrors, []);
          console.log(`PASS task tiles at ${width}px in ${theme} theme, normal and bulk selection`);
          passed += 1;
        } finally {
          await context.close();
        }
      }
    }
    console.log(`${passed}/10 task tile browser regressions passed`);
  } finally {
    await browser.close();
  }
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
