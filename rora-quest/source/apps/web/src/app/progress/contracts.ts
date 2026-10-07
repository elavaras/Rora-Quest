import { addDates, dateKeys, defaultRange, inRange, isDate, monday, ordinal, selection, ZONES, type Query } from "./dates";

export type Day = {
  date: string; position: "elapsed" | "today" | "future";
  status: "active" | "inactive" | "partial" | "unknown" | "todayPending" | "future";
  coverage: "full" | "partial" | "none" | "reliableSoFar" | "notApplicable";
  coverageReasons: ("beforeTracking" | "trackingStartedDuringDay" | "captureInterruption" | "dayInProgress" | "futureDate")[];
  unitCount: number; taskCount: number; substepCount: number; substepTaskCount: number;
};
export type Completion = {
  unitKey: string; sequence: string; kind: "task" | "substep";
  occurredAtUtc: string; occurredAtLocal: string; localDate: string;
  taskId: string; substepId: string | null; taskTitle: string; substepTitle: string | null;
  availability: "available" | "taskRemoved" | "substepRemoved"; taskHref: string | null;
};
export type EventPage = {
  date: string; timeZone: string; asOfUtc: string; snapshotSequence: string;
  totalCount: number; events: Completion[]; nextCursor: string | null;
};
export type LongestGap = {
  scope: "selectedRange"; state: "gap" | "none" | "insufficientCoverage";
  days: number | null; from: string | null; to: string | null;
  boundaries: ("rangeStart" | "rangeEnd" | "coverageStart" | "coverageEnd")[];
};
export type CurrentGap = {
  scope: "allReliableHistory"; state: "gap" | "none" | "unavailable";
  days: number | null; from: string | null; to: string | null; throughDate: string | null;
  lowerBound: boolean;
  reason: "sinceLastActivity" | "activityToday" | "activityYesterday" | "trackingStart" | "coverageBoundary" | "yesterdayUnknown" | "captureUnavailable" | "noElapsedDay";
};
export type WeeklyTask = {
  taskId: string; title: string; status: "Todo" | "InProgress" | "Done" | "Cancelled" | "Skipped";
  plannedWeekStart: string; plannedDate: string | null; taskHref: string;
  progressPercent: number; isComplete: boolean; progressBasis: "weightedSubsteps" | "substepCount" | "taskStatus";
  doneWeight: number; totalWeight: number; doneSubsteps: number; totalSubsteps: number;
};
export type ProgressReport = {
  asOfUtc: string; snapshotSequence: string; timeZone: string; today: string; nextMidnightUtc: string;
  from: string; to: string; selectedDate: string; isDefaultRange: boolean;
  coverage: { trackingStartedAtUtc: string; captureReliableNow: boolean; hasInterruptions: boolean };
  days: Day[];
  participation: { activeDays: number; fullyTrackedElapsedDays: number; unknownOrPartialElapsedDays: number; includesToday: boolean; futureDays: number };
  longestGap: LongestGap; currentGap: CurrentGap;
  lastActivity: { scope: "allRecordedHistory"; state: "recorded" | "none"; event: Completion | null };
  selectedDay: EventPage;
  weeklyPlan: {
    weekStart: string; weekEnd: string; asOfUtc: string; extendsOutsideRange: boolean;
    totalTasks: number; completeTasks: number; progressPercent: number | null; tasksHref: string; tasks: WeeklyTask[];
  };
};

// No coercion/defaults at the trust boundary. Unknown/missing data is unavailable.
function check(condition: unknown, field: string): asserts condition {
  if (!condition) throw new Error(`Invalid Progress response (${field}). Refresh to retry.`);
}
function object(value: unknown, field: string): Record<string, unknown> {
  check(value !== null && typeof value === "object" && !Array.isArray(value), field);
  return value as Record<string, unknown>;
}
function text(value: unknown, field: string): asserts value is string { check(typeof value === "string", field); }
function bool(value: unknown, field: string) { check(typeof value === "boolean", field); }
function number(value: unknown, field: string) { check(typeof value === "number" && Number.isFinite(value), field); }
function count(value: unknown, field: string) { number(value, field); check(Number.isSafeInteger(value) && (value as number) >= 0, field); }
function oneOf(value: unknown, allowed: readonly string[], field: string) { check(typeof value === "string" && allowed.includes(value), field); }
function date(value: unknown, field: string) { check(isDate(value), field); }
function nullableDate(value: unknown, field: string) { if (value !== null) date(value, field); }
const utcPattern = /^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d\.\d{6}Z$/;
const localPattern = /^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d\.\d{6}[+-](?:0\d|1[0-4]):[0-5]\d$/;
function instant(value: unknown, field: string, local = false) {
  check(typeof value === "string" && (local ? localPattern : utcPattern).test(value) && Number.isFinite(Date.parse(value)), field);
  // Reporting dates are bounded separately. Instants can reach the remainder of year 9999.
  const [y, m, d] = value.slice(0, 10).split("-").map(Number);
  const days = [31, y % 4 === 0 && (y % 100 !== 0 || y % 400 === 0) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  check(y >= 1 && m >= 1 && m <= 12 && d >= 1 && d <= days[m - 1], field);
}
function projectedDate(instant: string, zone: string): string {
  const parts = new Intl.DateTimeFormat("en-US", { timeZone: zone, year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(new Date(instant));
  const part = (name: string) => parts.find(p => p.type === name)!.value;
  return `${part("year").padStart(4, "0")}-${part("month")}-${part("day")}`;
}
function sequence(value: unknown, field: string) { check(typeof value === "string" && /^(0|[1-9]\d*)$/.test(value) && value.length <= 19, field); }
function guid(value: unknown, field: string) { check(typeof value === "string" && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(value), field); }
function array(value: unknown, field: string): unknown[] { check(Array.isArray(value), field); return value; }
function orderedEnum(value: unknown, allowed: readonly string[], field: string) {
  const values = array(value, field);
  let previous = -1;
  for (const item of values) {
    oneOf(item, allowed, field);
    const index = allowed.indexOf(item as string);
    check(index > previous, field);
    previous = index;
  }
}
export function compareEvents(a: Completion, b: Completion): number {
  return a.occurredAtUtc < b.occurredAtUtc ? -1 : a.occurredAtUtc > b.occurredAtUtc ? 1
    : a.unitKey < b.unitKey ? -1 : a.unitKey > b.unitKey ? 1 : 0;
}
function completion(value: unknown, asOf: string, snapshot: string, zone: string): Completion {
  const v = object(value, "event");
  guid(v.taskId, "event.taskId"); sequence(v.sequence, "event.sequence"); text(v.unitKey, "event.unitKey");
  oneOf(v.kind, ["task", "substep"], "event.kind");
  instant(v.occurredAtUtc, "event.occurredAtUtc"); instant(v.occurredAtLocal, "event.occurredAtLocal", true);
  date(v.localDate, "event.localDate"); text(v.taskTitle, "event.taskTitle");
  if (v.kind === "substep") { guid(v.substepId, "event.substepId"); text(v.substepTitle, "event.substepTitle"); }
  else check(v.substepId === null && v.substepTitle === null, "task event nullability");
  check(v.unitKey === `${v.kind}:${v.taskId}${v.kind === "substep" ? `:${v.substepId}` : ""}`, "event identity");
  check(BigInt(v.sequence as string) > 0n && BigInt(v.sequence as string) <= BigInt(snapshot), "event snapshot sequence");
  check((v.occurredAtUtc as string) <= asOf, "event snapshot time");
  check((v.occurredAtLocal as string).slice(0, 10) === v.localDate
    && Date.parse(v.occurredAtUtc as string) === Date.parse(v.occurredAtLocal as string)
    && (v.occurredAtUtc as string).slice(20, 26) === (v.occurredAtLocal as string).slice(20, 26)
    && projectedDate(v.occurredAtUtc as string, zone) === v.localDate, "event local time");
  oneOf(v.availability, ["available", "taskRemoved", "substepRemoved"], "event.availability");
  check(v.availability === "available" ? v.taskHref === `/tasks/${v.taskId}` : v.taskHref === null, "event link");
  check(v.kind === "substep" || v.availability !== "substepRemoved", "event removal");
  return v as unknown as Completion;
}
export function validateEventPage(value: unknown): EventPage {
  const v = object(value, "event page");
  date(v.date, "page.date"); oneOf(v.timeZone, ZONES, "page.timeZone"); instant(v.asOfUtc, "page.asOfUtc");
  sequence(v.snapshotSequence, "page.snapshotSequence"); count(v.totalCount, "page.totalCount");
  const events = array(v.events, "page.events").map(e => completion(e, v.asOfUtc as string, v.snapshotSequence as string, v.timeZone as string));
  check(events.length <= 100 && events.length <= (v.totalCount as number), "page count");
  const keys = new Set<string>();
  events.forEach((e, i) => {
    check(e.localDate === v.date && !keys.has(e.unitKey) && (!i || compareEvents(events[i - 1], e) < 0), "page order/date/identity");
    keys.add(e.unitKey);
  });
  check(v.nextCursor === null || (typeof v.nextCursor === "string" && v.nextCursor.length > 0 && v.nextCursor.length <= 2048 && events.length === 100), "page.nextCursor");
  return v as unknown as EventPage;
}
export function appendPage(previous: EventPage, value: unknown): EventPage {
  const next = validateEventPage(value);
  check(["date", "timeZone", "asOfUtc", "snapshotSequence", "totalCount"].every(key =>
    previous[key as keyof EventPage] === next[key as keyof EventPage]), "continuation snapshot");
  check(previous.nextCursor !== null && next.nextCursor !== previous.nextCursor, "continuation cursor");
  const events = [...previous.events, ...next.events];
  check(new Set(events.map(e => e.unitKey)).size === events.length
    && (!previous.events.length || !next.events.length || compareEvents(previous.events[previous.events.length - 1], next.events[0]) < 0), "continuation order");
  check(next.nextCursor === null ? events.length === next.totalCount : events.length < next.totalCount, "continuation total");
  return { ...next, events };
}
function validateDay(value: unknown, today: string): Day {
  const v = object(value, "day");
  date(v.date, "day.date");
  oneOf(v.position, ["elapsed", "today", "future"], "day.position");
  oneOf(v.status, ["active", "inactive", "partial", "unknown", "todayPending", "future"], "day.status");
  oneOf(v.coverage, ["full", "partial", "none", "reliableSoFar", "notApplicable"], "day.coverage");
  orderedEnum(v.coverageReasons, ["beforeTracking", "trackingStartedDuringDay", "captureInterruption", "dayInProgress", "futureDate"], "day.coverageReasons");
  for (const key of ["unitCount", "taskCount", "substepCount", "substepTaskCount"]) count(v[key], `day.${key}`);
  const d = v as unknown as Day;
  check(d.position === (d.date < today ? "elapsed" : d.date === today ? "today" : "future"), "day position/date");
  check(d.unitCount === d.taskCount + d.substepCount && d.substepTaskCount <= d.substepCount
    && (d.substepCount === 0 ? d.substepTaskCount === 0 : d.substepTaskCount > 0), "day counts");
  const expectedStatus = d.position === "future" ? "future" : d.unitCount > 0 ? "active"
    : d.position === "today" ? "todayPending" : d.coverage === "full" ? "inactive" : d.coverage === "partial" ? "partial" : "unknown";
  check(d.status === expectedStatus, "day status");
  if (d.position === "future") check(d.unitCount === 0 && d.coverage === "notApplicable" && d.coverageReasons.join() === "futureDate", "future day");
  else if (d.position === "today") check(["reliableSoFar", "partial", "none"].includes(d.coverage) && d.coverageReasons.includes("dayInProgress"), "today coverage");
  else check(["full", "partial", "none"].includes(d.coverage) && (d.coverage !== "full" || d.coverageReasons.length === 0), "elapsed coverage");
  return d;
}
function gap(value: unknown, current: boolean) {
  const v = object(value, "gap");
  oneOf(v.scope, [current ? "allReliableHistory" : "selectedRange"], "gap.scope");
  oneOf(v.state, ["gap", "none", current ? "unavailable" : "insufficientCoverage"], "gap.state");
  nullableDate(v.from, "gap.from"); nullableDate(v.to, "gap.to");
  if (v.state === "gap") {
    count(v.days, "gap.days"); date(v.from, "gap.from"); date(v.to, "gap.to");
    check((v.days as number) > 0 && v.days === ordinal(v.to as string) - ordinal(v.from as string) + 1, "gap length");
  } else check(v.days === (v.state === "none" ? 0 : null) && v.from === null && v.to === null, "gap nullability");
  if (current) {
    nullableDate(v.throughDate, "gap.throughDate"); bool(v.lowerBound, "gap.lowerBound");
    oneOf(v.reason, ["sinceLastActivity", "activityToday", "activityYesterday", "trackingStart", "coverageBoundary", "yesterdayUnknown", "captureUnavailable", "noElapsedDay"], "gap.reason");
    check(v.state === "gap" || v.lowerBound === false, "gap lower bound");
    if (v.state === "gap") check(v.lowerBound
      ? ["trackingStart", "coverageBoundary"].includes(v.reason as string) : v.reason === "sinceLastActivity", "current gap reason");
    else check((v.state === "none" ? ["activityToday", "activityYesterday"] : ["yesterdayUnknown", "captureUnavailable", "noElapsedDay"]).includes(v.reason as string), "current gap reason");
  } else {
    orderedEnum(v.boundaries, ["rangeStart", "rangeEnd", "coverageStart", "coverageEnd"], "gap.boundaries");
    check(v.state === "gap" || (v.boundaries as unknown[]).length === 0, "gap boundaries");
  }
}
export function validateReport(value: unknown, query?: Query): ProgressReport {
  const v = object(value, "report");
  instant(v.asOfUtc, "asOfUtc"); instant(v.nextMidnightUtc, "nextMidnightUtc"); sequence(v.snapshotSequence, "snapshotSequence");
  check((v.nextMidnightUtc as string) > (v.asOfUtc as string), "next midnight");
  oneOf(v.timeZone, ZONES, "timeZone"); date(v.today, "today"); date(v.selectedDate, "selectedDate"); bool(v.isDefaultRange, "isDefaultRange");
  check(projectedDate(v.asOfUtc as string, v.timeZone as string) === v.today, "reporting today");
  date(v.from, "from"); date(v.to, "to");
  const keys = dateKeys({ from: v.from as string, to: v.to as string });
  check(inRange(v.selectedDate as string, v as unknown as ProgressReport), "selectedDate range");
  const coverage = object(v.coverage, "coverage");
  instant(coverage.trackingStartedAtUtc, "coverage.trackingStartedAtUtc");
  bool(coverage.captureReliableNow, "coverage.captureReliableNow"); bool(coverage.hasInterruptions, "coverage.hasInterruptions");
  check((coverage.trackingStartedAtUtc as string) <= (v.asOfUtc as string), "tracking start");
  const days = array(v.days, "days").map(d => validateDay(d, v.today as string));
  check(days.length === keys.length && days.every((d, i) => d.date === keys[i]), "complete ordered date range");
  const p = object(v.participation, "participation");
  for (const key of ["activeDays", "fullyTrackedElapsedDays", "unknownOrPartialElapsedDays", "futureDays"]) count(p[key], `participation.${key}`);
  bool(p.includesToday, "participation.includesToday");
  check(p.activeDays === days.filter(d => d.status === "active").length
    && p.fullyTrackedElapsedDays === days.filter(d => d.position === "elapsed" && d.coverage === "full").length
    && p.unknownOrPartialElapsedDays === days.filter(d => d.position === "elapsed" && d.coverage !== "full").length
    && p.futureDays === days.filter(d => d.position === "future").length
    && p.includesToday === keys.includes(v.today as string), "participation totals");
  gap(v.longestGap, false); gap(v.currentGap, true);
  const longest = v.longestGap as LongestGap, current = v.currentGap as CurrentGap;
  if (longest.state === "gap") {
    check(inRange(longest.from!, v as unknown as ProgressReport) && inRange(longest.to!, v as unknown as ProgressReport)
      && days.filter(d => d.date >= longest.from! && d.date <= longest.to!).every(d => d.status === "inactive"), "longest gap dates");
  } else check(longest.state === "none"
    ? (p.fullyTrackedElapsedDays as number) > 0 && !days.some(d => d.status === "inactive")
    : p.fullyTrackedElapsedDays === 0, "longest gap availability");
  check(current.throughDate === addDates(v.today as string, -1), "current gap through date");
  if (current.state === "gap") check(current.to === current.throughDate, "current gap end");
  const last = object(v.lastActivity, "lastActivity");
  oneOf(last.scope, ["allRecordedHistory"], "lastActivity.scope"); oneOf(last.state, ["recorded", "none"], "lastActivity.state");
  if (last.state === "none") check(last.event === null && v.snapshotSequence === "0" && p.activeDays === 0, "lastActivity.event");
  else completion(last.event, v.asOfUtc as string, v.snapshotSequence as string, v.timeZone as string);
  const page = validateEventPage(v.selectedDay);
  const selected = days.find(d => d.date === v.selectedDate)!;
  check(page.date === v.selectedDate && page.timeZone === v.timeZone && page.asOfUtc === v.asOfUtc
    && page.snapshotSequence === v.snapshotSequence && page.totalCount === selected.unitCount, "selected day snapshot");
  check(page.nextCursor === null ? page.events.length === page.totalCount : page.events.length < page.totalCount, "selected day total");
  const w = object(v.weeklyPlan, "weeklyPlan");
  date(w.weekStart, "weeklyPlan.weekStart"); date(w.weekEnd, "weeklyPlan.weekEnd"); instant(w.asOfUtc, "weeklyPlan.asOfUtc");
  check(w.weekStart === monday(v.selectedDate as string) && w.weekEnd === addDates(w.weekStart as string, 6)
    && w.asOfUtc === v.asOfUtc, "weekly snapshot/bounds");
  bool(w.extendsOutsideRange, "weeklyPlan.extendsOutsideRange");
  check(w.extendsOutsideRange === ((w.weekStart as string) < (v.from as string) || (w.weekEnd as string) > (v.to as string)), "weekly clipping");
  count(w.totalTasks, "weeklyPlan.totalTasks"); count(w.completeTasks, "weeklyPlan.completeTasks");
  check(w.tasksHref === `/tasks?weekStart=${w.weekStart}`, "weeklyPlan.tasksHref");
  const tasks = array(w.tasks, "weeklyPlan.tasks");
  const ids = new Set<string>();
  for (const item of tasks) {
    const t = object(item, "weekly task");
    guid(t.taskId, "task.taskId"); text(t.title, "task.title");
    oneOf(t.status, ["Todo", "InProgress", "Done", "Cancelled", "Skipped"], "task.status");
    date(t.plannedWeekStart, "task.plannedWeekStart"); nullableDate(t.plannedDate, "task.plannedDate");
    check(t.taskHref === `/tasks/${t.taskId}` && !ids.has(t.taskId as string), "weekly task identity/link");
    ids.add(t.taskId as string);
    check(t.plannedWeekStart === w.weekStart || (t.plannedDate !== null && t.plannedDate! >= w.weekStart! && t.plannedDate! <= w.weekEnd!), "weekly membership");
    number(t.progressPercent, "task.progressPercent"); bool(t.isComplete, "task.isComplete");
    check(t.isComplete === ((t.progressPercent as number) >= 100), "task completion");
    oneOf(t.progressBasis, ["weightedSubsteps", "substepCount", "taskStatus"], "task.progressBasis");
    number(t.doneWeight, "task.doneWeight"); number(t.totalWeight, "task.totalWeight");
    count(t.doneSubsteps, "task.doneSubsteps"); count(t.totalSubsteps, "task.totalSubsteps");
    check((t.doneSubsteps as number) <= (t.totalSubsteps as number), "task substep counts");
  }
  check(w.totalTasks === tasks.length && w.completeTasks === (tasks as WeeklyTask[]).filter(t => t.isComplete).length, "weekly counts");
  if (tasks.length === 0) check(w.progressPercent === null, "empty plan progress");
  else number(w.progressPercent, "weeklyPlan.progressPercent");
  const report = v as unknown as ProgressReport;
  if (query) {
    const range = query.from && query.to ? { from: query.from, to: query.to } : defaultRange(report.today);
    check(report.timeZone === (query.timeZone ?? "Asia/Kolkata") && report.from === range.from && report.to === range.to
      && report.isDefaultRange === (query.from === undefined) && report.selectedDate === (query.selectedDate ?? selection(range, undefined, report.today)), "requested context");
  }
  return report;
}
