const assert = require("node:assert/strict");
const { test } = require("node:test");
const load = require("./progress-load.cjs");
const fixtures = require("./progress-fixtures.cjs");
const d = load("../src/app/progress/dates.ts");
const c = load("../src/app/progress/contracts.ts");
const { progressRedirect } = load("../src/app/lib/progress-redirect.ts");

test("pure date keys: defaults, navigation, inclusivity, selection, bounds", () => {
  assert.deepEqual(d.defaultRange("2026-10-06"), { from: "2026-09-14", to: "2026-10-11" });
  const previous = d.shiftRange(d.defaultRange("2026-10-06"), -1);
  assert.deepEqual(previous, { from: "2026-08-17", to: "2026-09-13" });
  assert.deepEqual(d.shiftRange(previous, 1), d.defaultRange("2026-10-06"));
  for (const size of [1, 28, 84]) {
    const range = { from: "2026-10-01", to: d.addDates("2026-10-01", size - 1) };
    assert.equal(d.rangeError(range.from, range.to), null);
    assert.equal(d.dateKeys(range).length, size);
    assert.equal(d.shiftRange(range, 1).from, d.addDates(range.to, 1));
  }
  for (const pair of [["2026-01-01", "2026-03-26"], ["2026-10-02", "2026-10-01"], [undefined, "2026-10-01"]]) {
    assert.ok(d.rangeError(...pair));
  }
  assert.equal(d.selection(previous, "2026-10-06", "2026-10-06"), previous.from);
  assert.equal(d.selection(d.defaultRange("2026-10-06"), previous.from, "2026-10-06"), "2026-10-06");
  assert.equal(d.selection(d.defaultRange("2026-10-06"), "2026-10-01", "2026-10-06"), "2026-10-01");
  assert.equal(d.addDates(d.MIN_DATE, -1), null);
  assert.equal(d.addDates(d.MAX_DATE, 1), null);
  assert.equal(d.shiftRange({ from: d.MIN_DATE, to: d.MIN_DATE }, -1), null);
  assert.equal(d.shiftRange({ from: d.MAX_DATE, to: d.MAX_DATE }, 1), null);
  assert.equal(d.defaultRange(d.MIN_DATE).from, d.MIN_DATE);
  assert.equal(d.defaultRange(d.MAX_DATE).to, d.MAX_DATE);
});

test("Gregorian dates do not depend on browser zone, DST, leap days or year 0099", () => {
  const before = process.env.TZ;
  try {
    for (const zone of ["UTC", "Asia/Kolkata", "America/New_York", "Pacific/Auckland"]) {
      process.env.TZ = zone;
      for (const [from, to] of [
        ["2026-03-08", "2026-03-09"], ["2026-11-01", "2026-11-02"],
        ["2028-02-28", "2028-02-29"], ["2028-02-29", "2028-03-01"],
        ["0099-12-31", "0100-01-01"], ["2026-12-31", "2027-01-01"]
      ]) {
        assert.equal(d.addDates(from, 1), to);
        assert.equal(d.addDates(to, -1), from);
      }
      assert.equal(d.dateLabel("2026-10-06"), "Tuesday, October 6, 2026");
    }
  } finally { if (before === undefined) delete process.env.TZ; else process.env.TZ = before; }
  for (const invalid of ["0000-01-01", "9999-12-27", "2026-02-30", "2026-02-29", "2026-2-02", "2026-10-01T00:00:00Z", " 2026-10-01", "", "0100-02-29"]) {
    assert.equal(d.isDate(invalid), false, invalid);
  }
});

test("strict web query parser, exact API queries, shared temporary redirect destinations", () => {
  assert.equal(progressRedirect({}), "/progress");
  for (const rangeType of [undefined, "Weekly", "Monthly", "Custom"]) {
    assert.equal(progressRedirect({ from: "2026-10-01", to: "2026-10-06", ...(rangeType ? { rangeType } : {}) }), "/progress?from=2026-10-01&to=2026-10-06");
  }
  for (const value of [
    { from: "2026-10-01" }, { rangeType: "Weekly" }, { from: ["2026-10-01"], to: "2026-10-06" },
    { from: "2026-10-01", to: "2026-10-06", rangeType: "weekly" },
    { from: "2026-10-01", to: "2026-10-06", owner: "private" },
    { from: "2026-01-01", to: "2026-03-26" }, { from: "2026-02-30", to: "2026-03-01" }
  ]) assert.equal(progressRedirect(value), "/progress?notice=legacyFiltersReset");
  for (const value of [
    { timeZone: "utc" }, { timeZone: "x".repeat(101) }, { timeZone: ["UTC", "UTC"] },
    { from: "" }, { selectedDate: "2026-02-30" }, { notice: "unsafe" }, { asOf: "anything" },
    { from: "2026-10-01", to: "2026-10-02", selectedDate: "2026-10-03" }
  ]) assert.ok(d.parseQuery(value).error);
  const valid = d.parseQuery({ notice: "legacyFiltersReset", timeZone: "America/New_York" });
  assert.equal(valid.error, null);
  assert.equal(valid.notice, true);
  assert.equal(d.queryString(valid.query), "timeZone=America%2FNew_York");
});

test("Tasks entry only accepts one valid Monday, including explicit empty weeks", () => {
  assert.deepEqual(d.entryWeek(undefined), { week: null, invalid: false });
  for (const week of ["2026-10-05", "0001-01-01", "9999-12-20"]) assert.deepEqual(d.entryWeek(week), { week, invalid: false });
  for (const invalid of ["2026-10-06", "", ["2026-10-05"], "2026-02-30", "9999-12-27"]) assert.deepEqual(d.entryWeek(invalid), { week: null, invalid: true });
});

test("complete normative design example is accepted without remapping or defaults", () => {
  const value = fixtures.example();
  assert.equal(c.validateReport(value, { from: "2026-10-06", to: "2026-10-06" }), value);
  assert.equal(value.weeklyPlan.progressPercent, 25);
});

test("independent fixtures: every date status, task status/basis, range, retained links and empty states", () => {
  const value = c.validateReport(fixtures.report({}, { allTaskStates: true }), {});
  assert.deepEqual(new Set(value.days.map(day => day.status)), new Set(["active", "inactive", "partial", "unknown", "todayPending", "future"]));
  assert.deepEqual(new Set(value.weeklyPlan.tasks.map(task => task.status)), new Set(["Todo", "InProgress", "Done", "Cancelled", "Skipped"]));
  assert.deepEqual(new Set(value.weeklyPlan.tasks.map(task => task.progressBasis)), new Set(["weightedSubsteps", "substepCount", "taskStatus"]));
  for (const timeZone of d.ZONES) {
    const query = { from: "2026-10-02", to: "2026-10-02", timeZone };
    const report = c.validateReport(fixtures.report(query), query);
    assert.equal(report.selectedDay.events.length, 10);
    assert.deepEqual(new Set(report.selectedDay.events.map(e => e.availability)), new Set(["available", "taskRemoved", "substepRemoved"]));
  }
  const empty = c.validateReport(fixtures.report({}, { empty: true, noPlan: true }), {});
  assert.equal(empty.snapshotSequence, "0");
  assert.equal(empty.lastActivity.event, null);
  assert.equal(empty.weeklyPlan.progressPercent, null);
  c.validateReport(fixtures.report({}, { activeToday: true }), {});
  for (const query of [{ from: "2026-08-01", to: "2026-08-28" }, { from: "2026-10-07", to: "2026-10-11" }, { from: "2026-08-01", to: "2026-10-23" }]) {
    c.validateReport(fixtures.report(query), query);
  }
});

test("gap highlight uses only exact server interval, never recomputes/tie-breaks in browser", () => {
  const report = c.validateReport(fixtures.report());
  assert.equal(report.longestGap.from, "2026-10-04");
  assert.deepEqual(d.gapHighlight(report.longestGap), { from: "2026-10-04", to: "2026-10-05" });
  assert.deepEqual(report.days.filter(day => d.inRange(day.date, d.gapHighlight(report.longestGap))).map(day => day.date), ["2026-10-04", "2026-10-05"]);
  for (const state of ["none", "insufficientCoverage"]) assert.equal(d.gapHighlight({ state, from: null, to: null }), null);
  const past = fixtures.report({ from: "2026-08-01", to: "2026-08-28" });
  assert.deepEqual(past.currentGap, report.currentGap);
  assert.deepEqual(past.lastActivity, report.lastActivity);
});

test("malformed/null/error data and missing required fields never become empty success", () => {
  for (const value of [null, undefined, [], {}, { code: "progressUnavailable", errors: {} }]) assert.throws(() => c.validateReport(value));
  const mutations = [
    r => { delete r.days; }, r => { r.coverage = null; }, r => { delete r.coverage.captureReliableNow; },
    r => { r.days[0].unitCount = "0"; }, r => { r.days[0].date = "2026-02-30"; },
    r => { r.days.reverse(); }, r => { r.participation.activeDays++; }, r => { r.days[0].status = "failed"; },
    r => { r.days[0].coverageReasons = ["unexpected"]; }, r => { r.selectedDay = null; },
    r => { r.selectedDay.snapshotSequence = "0"; }, r => { r.selectedDay.date = "2026-10-01"; },
    r => { r.nextMidnightUtc = r.asOfUtc; }, r => { r.asOfUtc = "2026-02-30T06:00:00.000000Z"; },
    r => { r.weeklyPlan.tasks = null; }, r => { r.weeklyPlan.tasks[0].taskHref = "https://invalid.test"; },
    r => { r.weeklyPlan.tasks[0].progressPercent = NaN; }, r => { r.weeklyPlan.tasks[0].isComplete = false; },
    r => { r.weeklyPlan.tasks[0].status = "done"; }, r => { delete r.weeklyPlan.progressPercent; },
    r => { r.currentGap.from = null; }, r => { r.longestGap.days = 400; },
    r => { r.lastActivity.event.taskHref = "/tasks/wrong"; },
    r => { r.lastActivity.event.unitKey = "task:other"; },
    r => { r.lastActivity.event.sequence = 1; }
  ];
  mutations.forEach((mutate, i) => { const value = fixtures.report(); mutate(value); assert.throws(() => c.validateReport(value), `mutation ${i}`); });
  assert.throws(() => c.validateReport(fixtures.report(), { timeZone: "UTC" }));
  assert.throws(() => c.validateReport(fixtures.report(), { from: "2026-10-01", to: "2026-10-02" }));
  // Every listed property at each object level is required, including explicit nullable fields.
  function removeEach(value, trail = []) {
    if (!value || typeof value !== "object") return;
    if (Array.isArray(value)) { value.forEach((item, index) => removeEach(item, [...trail, index])); return; }
    for (const [key, child] of Object.entries(value)) {
      const report = fixtures.example();
      let target = report;
      for (const step of trail) target = target[step];
      delete target[key];
      assert.throws(() => c.validateReport(report), `missing ${[...trail, key].join(".")}`);
      removeEach(child, [...trail, key]);
    }
  }
  removeEach(fixtures.example());
});

test("snapshot-matched pagination reaches all units, rejects duplicate/order/context/truncation", () => {
  const options = { eventCount: 205 }, query = { from: "2026-10-02", to: "2026-10-02" };
  let page = c.validateReport(fixtures.report(query, options), query).selectedDay;
  assert.equal(page.events.length, 100);
  const next = fixtures.continuation(page.nextCursor, options);
  for (const key of ["date", "timeZone", "asOfUtc", "snapshotSequence", "totalCount"]) {
    const bad = fixtures.clone(next);
    bad[key] = key === "totalCount" ? 999 : key === "date" ? "2026-10-01" : key === "timeZone" ? "UTC" : key === "asOfUtc" ? "2026-10-06T06:32:00.000000Z" : "999";
    assert.throws(() => c.appendPage(page, bad), key);
  }
  const duplicate = fixtures.clone(next); duplicate.events[0] = page.events[0];
  assert.throws(() => c.appendPage(page, duplicate));
  const unordered = fixtures.clone(next); unordered.events.reverse();
  assert.throws(() => c.appendPage(page, unordered));
  const truncated = fixtures.clone(next); truncated.nextCursor = null;
  assert.throws(() => c.appendPage(page, truncated));
  page = c.appendPage(page, next);
  assert.equal(page.events.length, 200);
  page = c.appendPage(page, fixtures.continuation(page.nextCursor, options));
  assert.equal(page.events.length, 205);
  assert.equal(page.nextCursor, null);
});

test("instant/date validation covers UTC boundary, DST repeated hour, partial active and unavailable gaps", () => {
  for (const [utc, local, date, zone] of [
    ["2026-10-05T18:29:59.000001Z", "2026-10-05T23:59:59.000001+05:30", "2026-10-05", "Asia/Kolkata"],
    ["2026-10-05T18:30:00.000000Z", "2026-10-06T00:00:00.000000+05:30", "2026-10-06", "Asia/Kolkata"],
    ["2026-10-05T18:30:00.000000Z", "2026-10-05T18:30:00.000000+00:00", "2026-10-05", "UTC"],
    ["2026-03-08T07:30:00.000000Z", "2026-03-08T03:30:00.000000-04:00", "2026-03-08", "America/New_York"],
    ["2026-11-01T05:30:00.000000Z", "2026-11-01T01:30:00.000000-04:00", "2026-11-01", "America/New_York"],
    ["2026-11-01T06:30:00.000000Z", "2026-11-01T01:30:00.000000-05:00", "2026-11-01", "America/New_York"]
  ]) {
    const event = fixtures.example().selectedDay.events[0];
    Object.assign(event, { occurredAtUtc: utc, occurredAtLocal: local, localDate: date });
    c.validateEventPage({ date, timeZone: zone, asOfUtc: "2026-12-01T12:00:00.000000Z", snapshotSequence: "1", totalCount: 1, events: [event], nextCursor: null });
  }
  const report = fixtures.report();
  report.currentGap = {
    scope: "allReliableHistory", state: "unavailable", days: null, from: null, to: null,
    throughDate: "2026-10-05", lowerBound: false, reason: "captureUnavailable"
  };
  report.coverage.captureReliableNow = false;
  c.validateReport(report);
  const bad = fixtures.example(); bad.lastActivity.event.occurredAtLocal = "2026-10-06T12:00:00.000001+05:30";
  assert.throws(() => c.validateReport(bad), "Submillisecond timestamp mismatch is not hidden by Date.parse");
});
