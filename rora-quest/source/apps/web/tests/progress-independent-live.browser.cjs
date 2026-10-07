// Run only against your own disposable test host and production Next server.
// No route interception / fixtures: this proxy forwards unchanged bodies to real API routes.
// The test host deliberately lacks CORS middleware; this loopback-only test transport adds CORS.
const assert = require("node:assert/strict");
const http = require("node:http");
const os = require("node:os");
const path = require("node:path");
const { chromium } = require("playwright-core");
const { validateReport } = require("./progress-load.cjs")("../src/app/progress/contracts.ts");
const origin = process.env.PROGRESS_TEST_ORIGIN || "http://127.0.0.1:3149";
const backend = process.env.PROGRESS_LIVE_API || "http://127.0.0.1:5147";
assert.ok(["localhost", "127.0.0.1"].includes(new URL(backend).hostname));
assert.ok(["localhost", "127.0.0.1"].includes(new URL(origin).hostname));
const owner = `independent-live-${Date.now()}`;
const headers = { "content-type": "application/json", "X-User-Id": owner };
async function request(url, method = "GET", body) {
  const response = await fetch(`${backend}${url}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await response.text();
  assert.ok(response.ok, `${method} ${url}: ${response.status} ${text}`);
  return text ? JSON.parse(text) : null;
}
async function report() { return validateReport(await request("/api/progress")); }
const proxy = http.createServer((req, res) => {
  const cors = { "access-control-allow-origin": origin, "access-control-allow-credentials": "true",
    "access-control-allow-headers": "content-type,x-user-id", "access-control-allow-methods": "GET,POST,PATCH,PUT,DELETE,OPTIONS" };
  if (req.method === "OPTIONS") { res.writeHead(204, cors); res.end(); return; }
  const upstream = http.request(new URL(req.url, backend), { method: req.method, headers: { ...req.headers, host: new URL(backend).host } }, response => {
    res.writeHead(response.statusCode, { ...response.headers, ...cors });
    response.pipe(res);
  });
  upstream.on("error", error => { res.writeHead(502, cors); res.end(error.message); });
  req.pipe(upstream);
});
(async () => {
  await new Promise((resolve, reject) => { proxy.once("error", reject); proxy.listen(5000, "localhost", resolve); });
  console.log(`OWNED_LOOPBACK_PROXY_PID=${process.pid}; real API=${backend}; Next=${origin}`);
  let browser;
  try {
    assert.equal((await fetch(backend + "/health")).status, 200);
    assert.equal((await fetch(origin + "/progress")).status, 200);
    const empty = await report();
    assert.equal(empty.selectedDay.totalCount, 0);
    assert.equal(empty.lastActivity.state, "none");
    assert.equal(empty.days.find(d => d.date === empty.today).status, "todayPending");
    const week = empty.weeklyPlan.weekStart;
    const makeTask = title => request("/api/tasks", "POST", { title, plannedWeekStart: week, plannedDate: empty.today });
    const plain = await makeTask("Live first task");
    const parent = await makeTask("Live automatic parent");
    const partial = await makeTask("Live weighted partial");
    const only = await request(`/api/tasks/${parent.id}/substeps`, "POST", { title: "Live final substep", weight: 1 });
    const small = await request(`/api/tasks/${partial.id}/substeps`, "POST", { title: "Live completed quarter", weight: 1 });
    await request(`/api/tasks/${partial.id}/substeps`, "POST", { title: "Live remaining three quarters", weight: 3 });
    await request(`/api/tasks/${plain.id}/status`, "PATCH", { status: "Done" });
    await request(`/api/tasks/${parent.id}/substeps/${only.id}`, "PATCH", { isDone: true });
    await request(`/api/tasks/${partial.id}/substeps/${small.id}`, "PATCH", { isDone: true });
    const before = await report();
    assert.equal(before.selectedDay.totalCount, 3);
    assert.equal(before.participation.activeDays, 1);
    assert.equal(before.weeklyPlan.completeTasks, 2);
    assert.equal(before.weeklyPlan.progressPercent, 75);
    assert.equal(before.currentGap.reason, "activityToday");
    assert.equal(before.days.find(d => d.date === before.today).coverage, "partial");
    assert.equal((await request(`/api/tasks/${parent.id}`)).status, "Done");
    browser = await chromium.launch({ channel: process.env.PROGRESS_TEST_BROWSER || "chrome", headless: true });
    const context = await browser.newContext({ viewport: { width: 1280, height: 1000 }, timezoneId: "America/Los_Angeles",
      extraHTTPHeaders: { "X-User-Id": owner }, serviceWorkers: "block" });
    const page = await context.newPage();
    page.setDefaultTimeout(15000);
    const errors = [], accepted = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("response", async response => {
      if (new URL(response.url()).pathname === "/api/progress" && response.ok()) {
        try { accepted.push(validateReport(await response.json())); } catch (error) { errors.push(error.message); }
      }
    });
    async function ready() {
      await page.getByRole("heading", { name: /^Day details/ }).waitFor();
      await page.getByText("2 of 3 currently scheduled tasks complete", { exact: true }).waitFor();
    }
    await page.goto(origin + "/progress"); await ready();
    assert.equal(await page.locator('nav a[href="/progress"]').count(), 1);
    assert.equal(await page.locator("button[data-date]").count(), 28);
    await page.getByLabel("Activity calendar", { exact: true }).waitFor();
    for (const name of ["Recent participation", "Weekly plan review", "Activity gaps"])
      await page.getByRole("heading", { name, exact: true }).waitFor();
    await page.getByText("Current plan progress: 75% (equal-task mean).", { exact: true }).waitFor();
    assert.ok((await page.locator("main").innerText()).includes("Live completed quarter"));
    assert.ok(accepted.length > 0, "Browser consumed a real, contract-accepted response");
    console.log(`PASS live five-area contract; today=${before.today}; 3 units/1 day; 2 of 3 complete; mean=75%; partial current coverage`);
    await page.screenshot({ path: path.join(os.tmpdir(), "rora-progress-acceptance-live-desktop.png"), fullPage: true });
    const refresh = page.waitForResponse(r => new URL(r.url()).pathname === "/api/progress" && r.ok());
    await page.getByRole("button", { name: "Refresh", exact: true }).click();
    const refreshed = validateReport(await (await refresh).json()); await ready();
    assert.equal(refreshed.today, before.today);
    assert.ok(refreshed.asOfUtc >= before.asOfUtc);
    assert.equal(refreshed.selectedDay.totalCount, 3);
    console.log(`PASS same live view refresh; asOf=${refreshed.asOfUtc}; today unchanged and all areas accepted`);
    await request(`/api/tasks/${plain.id}/status`, "PATCH", { status: "Done" });
    await request(`/api/tasks/${plain.id}/status`, "PATCH", { status: "Todo" });
    await request(`/api/tasks/${plain.id}/status`, "PATCH", { status: "Done" });
    const retry = await report();
    assert.equal(retry.snapshotSequence, before.snapshotSequence);
    assert.equal(retry.lastActivity.event.occurredAtUtc, before.lastActivity.event.occurredAtUtc);
    console.log("PASS live no-op/reopen/recomplete preserve ledger and last activity");
    await context.setOffline(true);
    await page.getByRole("button", { name: "Refresh", exact: true }).click();
    await page.getByRole("heading", { name: "Progress unavailable" }).waitFor();
    assert.equal(await page.getByRole("heading", { name: /^Day details/ }).count(), 0);
    await context.setOffline(false);
    await page.getByRole("button", { name: "Retry Progress", exact: true }).click(); await ready();
    console.log("PASS actual browser transport outage produces unavailable, not empty; retry recovers");
    const zone = page.waitForResponse(r => new URL(r.url()).pathname === "/api/progress" && r.url().includes("timeZone=UTC") && r.ok());
    await page.getByLabel("Reporting timezone").selectOption("UTC");
    const utc = validateReport(await (await zone).json()); await ready();
    assert.equal(utc.timeZone, "UTC");
    assert.ok(utc.weeklyPlan.tasks.every(t => t.plannedDate === before.today));
    for (const old of ["scorecard", "tracking"]) {
      const redirect = await fetch(`${origin}/${old}?from=${before.today}&to=${before.today}`, { redirect: "manual" });
      assert.equal(redirect.status, 307);
      assert.equal(redirect.headers.get("location"), `/progress?from=${before.today}&to=${before.today}`);
    }
    await page.goto(origin + "/tracking?unsupported=1"); await ready();
    await page.getByText(/The old filters could not be applied/).waitFor();
    assert.ok(page.url().includes("notice=legacyFiltersReset"));
    console.log("PASS live reporting-zone refresh, unchanged schedule dates, both 307s and invalid legacy explanation");
    await page.setViewportSize({ width: 320, height: 900 });
    await page.screenshot({ path: path.join(os.tmpdir(), "rora-progress-acceptance-live-mobile.png"), fullPage: true });
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
    await page.getByRole("button", { name: "Chronological date list" }).click();
    assert.equal(await page.locator("button[data-date]").count(), 28);
    const selected = page.locator(`button[data-date="${before.today}"]`);
    await selected.focus(); await page.keyboard.press("ArrowLeft"); await page.keyboard.press("Enter");
    await page.getByRole("heading", { name: /^Day details/ }).waitFor();
    assert.equal(await page.locator("button[data-date][aria-pressed=true]").count(), 1);
    assert.deepEqual(errors, []);
    console.log("PASS live 320px no overflow, chronological alternative and keyboard selection; no runtime/contract errors");
    console.log(`Screenshots: ${path.join(os.tmpdir(), "rora-progress-acceptance-live-desktop.png")} ; ${path.join(os.tmpdir(), "rora-progress-acceptance-live-mobile.png")}`);
    console.log(`REAL LIVE PASS; ${accepted.length} browser Progress responses contract-accepted; no Progress endpoint mocks`);
  } finally {
    if (browser) await browser.close();
    proxy.closeAllConnections();
    await new Promise(resolve => proxy.close(resolve));
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
