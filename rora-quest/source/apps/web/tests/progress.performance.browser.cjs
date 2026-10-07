// Frontend-only fixture baseline, not the API's 20-request p95 release gate.
const assert = require("node:assert/strict");
const os = require("node:os");
const { chromium } = require("playwright-core");
const fixtures = require("./progress-fixtures.cjs");
const origin = process.env.PROGRESS_TEST_ORIGIN || "http://127.0.0.1:3137";

(async () => {
  const browser = await chromium.launch({ channel: process.env.PROGRESS_TEST_BROWSER || "chrome", headless: true });
  try {
    const context = await browser.newContext({ viewport: { width: 1280, height: 1000 }, serviceWorkers: "block" });
    const page = await context.newPage();
    page.setDefaultTimeout(15000);
    const payloads = [];
    const options = { taskCount: 1000, eventCount: 9999 }; // 10,000 units including Oct 1.
    await context.route("**/*", async route => {
      const url = new URL(route.request().url());
      if (!url.pathname.startsWith("/api/")) {
        assert.equal(url.origin, origin);
        return route.continue();
      }
      const headers = { "access-control-allow-origin": origin, "access-control-allow-credentials": "true" };
      if (url.pathname === "/api/auth/me") return route.fulfill({ headers, json: { userId: "synthetic-perf" } });
      assert.equal(url.pathname, "/api/progress");
      const report = fixtures.report(Object.fromEntries(url.searchParams), options);
      assert.equal(report.weeklyPlan.totalTasks, 1000);
      assert.equal(report.snapshotSequence, "10000");
      assert.ok(report.selectedDay.events.length <= 100);
      const body = JSON.stringify(report);
      payloads.push(Buffer.byteLength(body));
      return route.fulfill({ headers, body, contentType: "application/json" });
    });
    console.log(`Environment: ${os.platform()} ${os.release()}, ${os.cpus()[0].model}, Node ${process.version}, browser ${browser.version()}, production Next web, 1280x1000. API intercepted locally.`);
    for (const [name, query] of [
      ["default", "?selectedDate=2026-10-02"],
      ["84 days", "?from=2026-08-01&to=2026-10-23&selectedDate=2026-10-02"]
    ]) {
      await page.goto(`${origin}/progress${query}`);
      await page.getByRole("heading", { name: /^Day details/ }).waitFor();
      // Explicit selectedDate ensures 100 event rows are rendered in the baseline.
      const samples = [];
      for (let i = 0; i < 20; i++) {
        const start = performance.now();
        // A page reload preserves the query; all module caches are warm.
        await page.reload();
        await page.getByRole("heading", { name: /^Day details/ }).waitFor();
        samples.push(performance.now() - start);
      }
      const p95 = [...samples].sort((a, b) => a - b)[18];
      console.log(`${name}: 20 warm reload-to-usable samples; p95=${p95.toFixed(1)}ms, min=${Math.min(...samples).toFixed(1)}ms, max=${Math.max(...samples).toFixed(1)}ms, report=${payloads.at(-1)} bytes, 100/9999 selected-day records transferred.`);
      assert.ok(p95 <= 2000, `${name} frontend fixture p95 exceeds 2 seconds`);
    }
    await context.close();
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
