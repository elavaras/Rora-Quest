// Synthetic, deterministic wire fixtures, independent of production projection/date helpers.
// These are not live API/persistence acceptance evidence.
const example = require("./progress-example.json");
const clone = value => structuredClone(value);
const id = n => `00000000-0000-0000-0000-${String(n).padStart(12, "0")}`;
function utcDate(key) {
  const [y, m, d] = key.split("-").map(Number);
  const value = new Date(0);
  value.setUTCFullYear(y, m - 1, d); value.setUTCHours(0, 0, 0, 0);
  return value;
}
function key(value) {
  return `${String(value.getUTCFullYear()).padStart(4, "0")}-${String(value.getUTCMonth() + 1).padStart(2, "0")}-${String(value.getUTCDate()).padStart(2, "0")}`;
}
function add(date, count) { const value = utcDate(date); value.setUTCDate(value.getUTCDate() + count); return key(value); }
function monday(date) { const value = utcDate(date); return add(date, -((value.getUTCDay() + 6) % 7)); }
function dates(from, to) { const result = []; for (let d = from; d <= to; d = add(d, 1)) result.push(d); return result; }
function event(n, date, timeZone, options = {}) {
  const taskId = id(n), substepId = n % 2 ? id(n + 10000) : null;
  const localTime = timeZone === "UTC" ? "06:30:00.000000+00:00" : timeZone === "America/New_York" ? "02:30:00.000000-04:00" : "12:00:00.000000+05:30";
  const availability = n % 7 === 0 ? "taskRemoved" : substepId && n % 3 === 0 ? "substepRemoved" : "available";
  return {
    unitKey: `${substepId ? "substep" : "task"}:${taskId}${substepId ? `:${substepId}` : ""}`,
    sequence: String(n), kind: substepId ? "substep" : "task",
    occurredAtUtc: `${date}T06:30:00.000000Z`, occurredAtLocal: `${date}T${localTime}`, localDate: date,
    taskId, substepId, taskTitle: `Retained completion name ${n}${options.longNames ? ` ${"long-name-".repeat(30)}` : ""}`,
    substepTitle: substepId ? `Original substep ${n}` : null,
    availability, taskHref: availability === "available" ? `/tasks/${taskId}` : null
  };
}
function events(options, timeZone) {
  if (options.empty) return [];
  const result = [event(1, "2026-10-01", timeZone, options)];
  for (let i = 0; i < (options.eventCount ?? 10); i++) result.push(event(i + 2, "2026-10-02", timeZone, options));
  if (options.activeToday) result.push(event(result.length + 1, options.today ?? "2026-10-06", timeZone, options));
  return result.sort((a, b) => a.occurredAtUtc < b.occurredAtUtc ? -1 : a.occurredAtUtc > b.occurredAtUtc ? 1 : a.unitKey < b.unitKey ? -1 : 1);
}
function page(date, timeZone, asOfUtc, all, offset = 0) {
  const matching = all.filter(e => e.localDate === date);
  return {
    date, timeZone, asOfUtc, snapshotSequence: String(all.length),
    totalCount: matching.length, events: matching.slice(offset, offset + 100),
    nextCursor: offset + 100 < matching.length ? `fixture|${date}|${timeZone}|${offset + 100}` : null
  };
}
function task(n, week, percent, status = "Todo", basis = "weightedSubsteps", plannedDate = null) {
  return {
    taskId: id(n), title: `Current task ${n}`, status, plannedWeekStart: week, plannedDate,
    taskHref: `/tasks/${id(n)}`, progressPercent: percent, isComplete: percent >= 100,
    progressBasis: basis, doneWeight: basis === "weightedSubsteps" ? percent / 25 : 0,
    totalWeight: basis === "weightedSubsteps" ? 4 : 0,
    doneSubsteps: basis === "taskStatus" ? 0 : percent === 100 ? 4 : 1, totalSubsteps: basis === "taskStatus" ? 0 : 4
  };
}
function report(query = {}, options = {}) {
  const today = options.today ?? "2026-10-06", timeZone = query.timeZone ?? "Asia/Kolkata";
  const from = query.from ?? add(monday(today), -21), to = query.to ?? add(monday(today), 6);
  const selectedDate = query.selectedDate ?? (today >= from && today <= to ? today : from);
  const asOfUtc = `${today}T06:31:00.000000Z`;
  const nextMidnightUtc = timeZone === "UTC" ? `${add(today, 1)}T00:00:00.000000Z`
    : timeZone === "America/New_York" ? `${add(today, 1)}T04:00:00.000000Z` : `${today}T18:30:00.000000Z`;
  const all = events(options, timeZone);
  const days = dates(from, to).map(date => {
    const position = date < today ? "elapsed" : date === today ? "today" : "future";
    const coverage = position === "future" ? "notApplicable" : date < "2026-09-27" ? "none"
      : ["2026-09-27", "2026-09-30", "2026-10-01", "2026-10-03"].includes(date) ? "partial"
        : position === "today" ? "reliableSoFar" : "full";
    const matching = all.filter(e => e.localDate === date);
    const taskCount = matching.filter(e => e.kind === "task").length;
    return {
      date, position, status: position === "future" ? "future" : matching.length ? "active"
        : position === "today" ? "todayPending" : coverage === "none" ? "unknown" : coverage === "partial" ? "partial" : "inactive",
      coverage, coverageReasons: position === "future" ? ["futureDate"] : coverage === "none" ? ["beforeTracking"]
        : date === "2026-09-27" ? ["trackingStartedDuringDay"] : coverage === "partial" ? ["captureInterruption"]
          : position === "today" ? ["dayInProgress"] : [],
      unitCount: matching.length, taskCount, substepCount: matching.length - taskCount,
      substepTaskCount: matching.length - taskCount
    };
  });
  let best = [], run = [];
  for (const d of days) {
    if (d.status === "inactive") { run.push(d.date); if (run.length >= best.length) best = [...run]; }
    else run = [];
  }
  const longestGap = {
    scope: "selectedRange", state: best.length ? "gap" : days.some(d => d.coverage === "full") ? "none" : "insufficientCoverage",
    days: best.length || (days.some(d => d.coverage === "full") ? 0 : null),
    from: best[0] ?? null, to: best.at(-1) ?? null, boundaries: []
  };
  if (best.length) {
    if (best[0] === from) longestGap.boundaries.push("rangeStart");
    if (best.at(-1) === to) longestGap.boundaries.push("rangeEnd");
    if (days.find(d => d.date === add(best[0], -1))?.coverage === "partial") longestGap.boundaries.push("coverageStart");
    if (days.find(d => d.date === add(best.at(-1), 1))?.coverage === "partial") longestGap.boundaries.push("coverageEnd");
  }
  const weekStart = monday(selectedDate), weekEnd = add(weekStart, 6);
  const tasks = options.noPlan ? [] : [
    task(1001, weekStart, 100, "Done", "taskStatus", weekStart),
    task(1002, weekStart, 100, "Cancelled"),
    task(1003, weekStart, 25, "Done")
  ];
  if (options.allTaskStates && !options.noPlan) tasks.push(
    task(1004, weekStart, 25, "InProgress", "substepCount"),
    task(1005, weekStart, 99.99, "Todo"),
    task(1006, weekStart, 0, "Skipped", "taskStatus"),
    task(1007, weekStart, 100, "Skipped")
  );
  if (options.taskCount && !options.noPlan) {
    while (tasks.length < options.taskCount) tasks.push(task(1001 + tasks.length, weekStart, 25));
  }
  return {
    asOfUtc, snapshotSequence: String(all.length), timeZone, today, nextMidnightUtc,
    from, to, selectedDate, isDefaultRange: query.from === undefined,
    coverage: { trackingStartedAtUtc: "2026-09-27T04:30:00.000000Z", captureReliableNow: true, hasInterruptions: true },
    days,
    participation: {
      activeDays: days.filter(d => d.status === "active").length,
      fullyTrackedElapsedDays: days.filter(d => d.position === "elapsed" && d.coverage === "full").length,
      unknownOrPartialElapsedDays: days.filter(d => d.position === "elapsed" && d.coverage !== "full").length,
      includesToday: today >= from && today <= to, futureDays: days.filter(d => d.position === "future").length
    },
    longestGap,
    currentGap: options.activeToday
      ? { scope: "allReliableHistory", state: "none", days: 0, from: null, to: null, throughDate: add(today, -1), lowerBound: false, reason: "activityToday" }
      : { scope: "allReliableHistory", state: "gap", days: dates("2026-10-04", add(today, -1)).length, from: "2026-10-04", to: add(today, -1), throughDate: add(today, -1), lowerBound: true, reason: "coverageBoundary" },
    lastActivity: { scope: "allRecordedHistory", state: all.length ? "recorded" : "none", event: all.at(-1) ?? null },
    selectedDay: page(selectedDate, timeZone, asOfUtc, all),
    weeklyPlan: {
      weekStart, weekEnd, asOfUtc, extendsOutsideRange: weekStart < from || weekEnd > to,
      totalTasks: tasks.length, completeTasks: tasks.filter(t => t.isComplete).length,
      progressPercent: tasks.length ? Math.round(tasks.reduce((sum, t) => sum + t.progressPercent, 0) / tasks.length * 100) / 100 : null,
      tasksHref: `/tasks?weekStart=${weekStart}`, tasks
    }
  };
}
function continuation(cursor, options = {}) {
  const [prefix, date, timeZone, offset] = cursor.split("|");
  if (prefix !== "fixture") throw new Error("Unexpected synthetic cursor");
  return page(date, timeZone, `${options.today ?? "2026-10-06"}T06:31:00.000000Z`, events(options, timeZone), Number(offset));
}
module.exports = { example: () => clone(example), report, continuation, event, clone, add, monday, dates };
