"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState, type KeyboardEvent } from "react";
import type { Completion, Day } from "./contracts";
import {
  dateLabel, formatInstant, gapHighlight, inRange, MAX_DATE, MIN_DATE, rangeError, selection,
  shiftRange, weekday, ZONES, type Query, type Range, type Zone
} from "./dates";
import { useProgress } from "./use-progress";
import styles from "./progress.module.css";

const states: Record<Day["status"], { symbol: string; label: string }> = {
  active: { symbol: "●", label: "Recorded progress" },
  inactive: { symbol: "○", label: "No recorded progress" },
  partial: { symbol: "◐", label: "Partially tracked" },
  unknown: { symbol: "?", label: "Unknown history" },
  todayPending: { symbol: "◷", label: "No recorded progress yet today" },
  future: { symbol: "→", label: "Future date" }
};
const coverageNames: Record<Day["coverage"], string> = {
  full: "Fully tracked elapsed day", partial: "Partially tracked", none: "Tracking unavailable",
  reliableSoFar: "Reliable so far; today is in progress", notApplicable: "Future; not inactivity"
};
const reasons: Record<Day["coverageReasons"][number], string> = {
  beforeTracking: "Before reliable tracking began", trackingStartedDuringDay: "Tracking began during this day",
  captureInterruption: "Recording coverage was interrupted", dayInProgress: "Day still in progress", futureDate: "Date has not begun"
};
const currentReasons = {
  sinceLastActivity: "Since the last recorded activity.",
  activityToday: "Recorded activity today; zero full inactive days.",
  activityYesterday: "Recorded activity yesterday; zero full inactive days.",
  trackingStart: "Since reliable tracking began; no earlier activity recorded.",
  coverageBoundary: "Earlier history is unknown beyond a coverage boundary.",
  yesterdayUnknown: "Yesterday was unknown or only partially tracked.",
  captureUnavailable: "Current recording completeness cannot be established.",
  noElapsedDay: "No fully tracked elapsed day is available yet."
};
function EventDescription({ event, zone }: { event: Completion; zone: string }) {
  const description = <>{event.taskTitle}{event.kind === "substep" && <> — {event.substepTitle}</>}</>;
  return <>
    <div>{event.taskHref ? <Link href={event.taskHref}>{description}</Link> : description}</div>
    <small>{event.kind === "task" ? "Task without substeps" : "Substep"} · {formatInstant(event.occurredAtUtc, zone)}
      {event.availability !== "available" && <> · Historical record — {event.availability === "taskRemoved" ? "task removed" : "substep removed"}</>}
    </small>
  </>;
}

export default function ProgressClient({ entry }: {
  entry: { query: Query; notice: boolean; error: string | null };
}) {
  const [query, setQuery] = useState(entry.query);
  const [inputError, setInputError] = useState(entry.error);
  const [entryInvalid, setEntryInvalid] = useState(Boolean(entry.error));
  const [from, setFrom] = useState(entry.query.from ?? "");
  const [to, setTo] = useState(entry.query.to ?? "");
  const [highlight, setHighlight] = useState<Range | null>(null);
  const [announcement, setAnnouncement] = useState("");
  const [view, setView] = useState<"calendar" | "list">("calendar");
  const [focusDate, setFocusDate] = useState<string | null>(null);
  const refs = useRef(new Map<string, HTMLButtonElement>());
  const data = useProgress(query, !entryInvalid);
  const { report, page, error, pageError, paging, loadMore, invalidate, refresh } = data;

  const change = (next: Query, keepHighlight = false) => {
    invalidate(); setInputError(null); setEntryInvalid(false);
    if (!keepHighlight) { setHighlight(null); setFocusDate(null); }
    setQuery(next);
    // A same-context action (for example Current four weeks) is still a refresh.
    refresh();
  };
  const refreshCurrent = useCallback(() => {
    setHighlight(null); setFocusDate(null);
    // Default mode follows the reporting-zone week/today after a midnight.
    if (!query.from) setQuery(q => ({ timeZone: q.timeZone }));
    refresh();
  }, [query.from, refresh]);

  useEffect(() => {
    const visible = () => { if (!document.hidden) refreshCurrent(); else invalidate(); };
    const focus = () => { if (!document.hidden) refreshCurrent(); };
    const storage = () => refreshCurrent();
    window.addEventListener("focus", focus);
    window.addEventListener("storage", storage);
    window.addEventListener("pagehide", invalidate);
    document.addEventListener("visibilitychange", visible);
    return () => {
      window.removeEventListener("focus", focus); window.removeEventListener("storage", storage);
      window.removeEventListener("pagehide", invalidate); document.removeEventListener("visibilitychange", visible);
    };
  }, [refreshCurrent, invalidate]);
  useEffect(() => {
    if (!report) return;
    const delay = Math.max(1, Date.parse(report.nextMidnightUtc) - Date.parse(report.asOfUtc));
    const timer = window.setTimeout(refreshCurrent, delay);
    return () => window.clearTimeout(timer);
  }, [report, refreshCurrent]);
  useEffect(() => {
    if (!report) return;
    setFrom(report.from); setTo(report.to);
    // A selection read is a fresh snapshot too. Never keep a superseded gap.
    const currentGap = gapHighlight(report.longestGap);
    setHighlight(previous => previous && currentGap?.from === previous.from && currentGap.to === previous.to ? previous : null);
    setAnnouncement(`Selected ${dateLabel(report.selectedDate)}. ${states[report.days.find(d => d.date === report.selectedDate)!.status].label}.`);
    if (focusDate) { refs.current.get(focusDate)?.focus(); setFocusDate(null); }
  }, [report, focusDate]);

  const choose = (date: string, keepHighlight = true) => {
    setFocusDate(date);
    change({ ...query, selectedDate: date }, keepHighlight);
  };
  const navigate = (direction: -1 | 1) => {
    if (!report) return;
    const range = shiftRange(report, direction);
    if (range) change({ ...range, timeZone: query.timeZone, selectedDate: selection(range, report.selectedDate, report.today) });
  };
  const keyNavigate = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    if (!report) return;
    const moves: Record<string, number> = { ArrowLeft: index - 1, ArrowRight: index + 1, ArrowUp: index - 7, ArrowDown: index + 7, Home: 0, End: report.days.length - 1 };
    const target = moves[event.key];
    if (target === undefined) return;
    event.preventDefault();
    refs.current.get(report.days[Math.max(0, Math.min(report.days.length - 1, target))].date)?.focus();
  };
  const dayButton = (day: Day, index: number, list: boolean) => {
    const gap = highlight && inRange(day.date, highlight);
    const selected = report!.selectedDate === day.date;
    return <button key={day.date} type="button" data-date={day.date} data-status={day.status} data-gap={gap ? "true" : "false"}
      className={`${styles.day} ${day.status === "active" ? styles.active : ""} ${selected ? styles.selected : ""} ${gap ? styles.gap : ""}`}
      aria-pressed={selected} aria-current={day.position === "today" ? "date" : undefined}
      aria-label={`${dateLabel(day.date)}. ${states[day.status].label}. ${day.unitCount} qualifying units. ${coverageNames[day.coverage]}.${gap ? " Highlighted gap." : ""}`}
      ref={node => { if (node) refs.current.set(day.date, node); else refs.current.delete(day.date); }}
      onKeyDown={event => keyNavigate(event, index)} onClick={() => choose(day.date)}
      style={!list && index === 0 ? { gridColumnStart: weekday(day.date) + 1 } : undefined}>
      <span>{list ? dateLabel(day.date) : Number(day.date.slice(8))}</span>
      <span aria-hidden="true">{states[day.status].symbol}{gap ? " ▧" : ""}{selected ? " ✓" : ""}</span>
      {!list && <span className={styles.dayMeta} aria-hidden="true">
        {day.position === "today" ? "Today" : day.status === "active" && day.coverage !== "full" ? (day.coverage === "none" ? "?" : "◐") : "\u00a0"}
      </span>}
      {list && <span>{states[day.status].label} · {day.unitCount} units · {coverageNames[day.coverage]}{gap ? " · Highlighted gap" : ""}</span>}
    </button>;
  };
  const selected = report?.days.find(day => day.date === report.selectedDate);
  const longest = report?.longestGap;
  const current = report?.currentGap;

  return <section className={`page ${styles.progress}`}>
    <header className="card">
      <h2>Progress</h2>
      <p>First recorded completions; reopening and rechecking do not add activity.</p>
      <p>Completion-time names are retained after renaming or removal. Historical records are not the current schedule.</p>
      {entry.notice && <p role="status">The old filters could not be applied; showing the current four weeks.</p>}
      <div className={styles.controls}>
        <label>Reporting timezone
          <select value={query.timeZone ?? "Asia/Kolkata"} onChange={e => {
            change({ ...query, timeZone: e.target.value as Zone, selectedDate: query.from ? query.selectedDate : undefined });
          }}>{ZONES.map(zone => <option key={zone}>{zone}</option>)}</select>
        </label>
        <button type="button" onClick={() => change({ timeZone: query.timeZone })}>Current four weeks</button>
        <button type="button" onClick={refreshCurrent} disabled={entryInvalid}>Refresh</button>
      </div>
      <form onSubmit={event => {
        event.preventDefault();
        const problem = rangeError(from, to);
        if (problem) { setInputError(problem); return; }
        const range = { from, to };
        change({ ...range, timeZone: query.timeZone, selectedDate: report ? selection(range, report.selectedDate, report.today) : undefined });
      }} className={styles.controls}>
        <label>From (inclusive)<input type="date" min={MIN_DATE} max={MAX_DATE} required value={from} onChange={e => setFrom(e.target.value)} /></label>
        <label>To (inclusive)<input type="date" min={MIN_DATE} max={MAX_DATE} required value={to} onChange={e => setTo(e.target.value)} /></label>
        <button type="submit">Apply dates</button>
        <span>1–84 inclusive dates</span>
      </form>
      {inputError && <p role="alert">{inputError} Choose valid dates or Current four weeks.</p>}
      <div className={styles.controls}>
        <button type="button" disabled={!report || !shiftRange(report, -1)} onClick={() => navigate(-1)}>Previous range</button>
        <button type="button" disabled={!report || !shiftRange(report, 1)} onClick={() => navigate(1)}>Next range</button>
      </div>
    </header>
    <p className={styles.srOnly} role="status" aria-live="polite">{announcement}</p>
    {!entryInvalid && !report && !error && <div className="card" role="status">Loading Progress; current metrics are unavailable while refreshing…</div>}
    {error && <div className="card" role="alert"><h3>Progress unavailable</h3><p>{error}</p><button onClick={refreshCurrent}>Retry Progress</button></div>}
    {report && selected && page && longest && current && <>
      <section className="card" aria-labelledby="activity-heading">
        <h3 id="activity-heading">Recorded activity</h3>
        <p><strong>{report.from} through {report.to}</strong> · {report.timeZone} · {report.isDefaultRange ? "Current four weeks" : "Explicit date range"}</p>
        <p>As of {formatInstant(report.asOfUtc, report.timeZone)}. Tracking began {formatInstant(report.coverage.trackingStartedAtUtc, report.timeZone)}; earlier dates are unknown.</p>
        {!report.coverage.captureReliableNow && <p>Recording coverage is currently interrupted. Positive records remain evidence; absence is not proof of inactivity.</p>}
        {report.coverage.hasInterruptions && <p>History includes coverage interruptions; unknown or partial dates break gaps.</p>}
        <p>One filled circle means recorded activity, whether there was one completion or many. It does not measure effort or mastery.</p>
        <ul className={styles.legend} aria-label="Activity legend">
          {Object.values(states).map(state => <li key={state.label}><span aria-hidden="true">{state.symbol}</span> {state.label}</li>)}
          <li>▧ Dashed border: highlighted gap</li><li>✓ Solid outline: selected day</li>
        </ul>
        <div className={styles.controls} aria-label="Activity presentation">
          <button aria-pressed={view === "calendar"} onClick={() => setView("calendar")}>Calendar</button>
          <button aria-pressed={view === "list"} onClick={() => setView("list")}>Chronological date list</button>
        </div>
        {view === "calendar" ? <div aria-label="Activity calendar">
          <p>Arrow keys move focus; Home/End reach the bounds. Enter or Space selects a day.</p>
          <div className={styles.calendar} role="group" aria-label="Select an activity date">
            {["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"].map(day => <span className={styles.weekday} key={day} aria-hidden="true">{day}</span>)}
            {report.days.map((day, index) => dayButton(day, index, false))}
          </div>
        </div> : <ol className={styles.dateList} aria-label="Dates in chronological order">
          {report.days.map((day, index) => <li key={day.date}>{dayButton(day, index, true)}</li>)}
        </ol>}
      </section>
      <section className="card" aria-labelledby="participation-heading">
        <h3 id="participation-heading">Recent participation</h3>
        <p><strong>Recorded progress on {report.participation.activeDays} days in {report.from} through {report.to}.</strong></p>
        <p>{report.participation.fullyTrackedElapsedDays} fully tracked elapsed days; {report.participation.unknownOrPartialElapsedDays} unknown or partially tracked elapsed days;
          {" "}{report.participation.includesToday ? "today included (still in progress)" : "today not included"}; {report.participation.futureDays} future dates.</p>
        {report.days.every(day => day.status === "unknown") && <p>Historical coverage is unavailable for this period. This is not a count of days worked.</p>}
        <p>Unknown and partial dates are not assumed to be empty. No daily attendance target is implied.</p>
      </section>
      <section className="card" aria-labelledby="detail-heading">
        <h3 id="detail-heading">Day details — {dateLabel(selected.date)}</h3>
        <p>{report.timeZone} · {states[selected.status].label} · {coverageNames[selected.coverage]}</p>
        {selected.coverageReasons.length > 0 && <p>{selected.coverageReasons.map(reason => reasons[reason]).join("; ")}.</p>}
        <p>{selected.unitCount} qualifying units: {selected.taskCount} tasks without substeps and {selected.substepCount} substeps across {selected.substepTaskCount} parent tasks. Parent headings are not extra completions.</p>
        {selected.unitCount === 0 && <p>{selected.status === "unknown" || selected.status === "partial"
          ? "No completions recorded here; incomplete coverage cannot establish an inactive day."
          : states[selected.status].label}.</p>}
        <ol className={styles.events} aria-label="Recorded completions in occurrence order">
          {page.events.map(event => <li key={event.unitKey}><EventDescription event={event} zone={report.timeZone} /></li>)}
        </ol>
        <p>Showing {page.events.length} of {page.totalCount} recorded units.</p>
        {pageError && <p role="alert">{pageError} Previously loaded records remain visible; this list is not complete.</p>}
        {page.nextCursor && <button onClick={() => void loadMore()} disabled={paging}>{paging ? "Loading more records…" : pageError ? "Retry more records" : "Load more records"}</button>}
        <span className={styles.srOnly} role="status">{paging ? "Loading next event page" : `${page.events.length} records loaded`}</span>
      </section>
      <section className="card" aria-labelledby="weekly-heading">
        <h3 id="weekly-heading">Weekly plan review</h3>
        <p><strong>{report.weeklyPlan.weekStart} through {report.weeklyPlan.weekEnd}</strong></p>
        <p>Current schedule, as of {formatInstant(report.weeklyPlan.asOfUtc, report.timeZone)} — not historical adherence.</p>
        {report.weeklyPlan.extendsOutsideRange && <p>This reviews the full Monday–Sunday week, including dates outside the activity range.</p>}
        <p><strong>{report.weeklyPlan.completeTasks} of {report.weeklyPlan.totalTasks} currently scheduled tasks complete</strong></p>
        {report.weeklyPlan.progressPercent !== null && <p>Current plan progress: {report.weeklyPlan.progressPercent}% (equal-task mean).</p>}
        {!report.weeklyPlan.tasks.length && <p>No currently scheduled tasks in this week.</p>}
        <ul className={styles.tasks}>
          {report.weeklyPlan.tasks.map(task => <li key={task.taskId}>
            <Link href={task.taskHref}>{task.title}</Link>
            <p>{task.plannedDate ?? "No day selected"} · Current status: {task.status} · {task.progressPercent}% progress · {task.isComplete ? "Progress complete" : "Progress incomplete"}</p>
            <p>{task.progressBasis === "weightedSubsteps" ? `${task.doneWeight} of ${task.totalWeight} substep weight complete`
              : task.progressBasis === "substepCount" ? `${task.doneSubsteps} of ${task.totalSubsteps} substeps complete (count-based fallback)`
                : "No substeps; progress follows task status"}.</p>
            {!task.isComplete && <Link href={task.taskHref}>Open unfinished task: {task.title}</Link>}
          </li>)}
        </ul>
        <Link className={styles.weekLink} href={report.weeklyPlan.tasksHref}>Open this week in Tasks by Week</Link>
      </section>
      <section className="card" aria-labelledby="gaps-heading">
        <h3 id="gaps-heading">Activity gaps</h3>
        <p>Gaps are absence of recorded completions, not missed commitments. Weekends count; today is never a full inactive day.</p>
        <h4>Longest gap — selected range only</h4>
        {longest.state === "gap" ? <>
          <p>{longest.days} days: {longest.from} through {longest.to}.</p>
          {longest.boundaries.some(b => b === "rangeStart" || b === "rangeEnd") && <p>Within selected range; the gap may extend outside it.</p>}
          {longest.boundaries.some(b => b === "coverageStart" || b === "coverageEnd") && <p>At a coverage boundary; earlier or later completeness is unknown.</p>}
          <button onClick={() => {
            const range = gapHighlight(longest);
            if (!range) return;
            setHighlight(range); setAnnouncement(`Highlighted ${longest.days} days, ${range.from} through ${range.to}.`);
            choose(range.from, true);
          }}>Highlight longest gap</button>
        </> : <p>{longest.state === "insufficientCoverage" ? "Not enough fully tracked days." : "No full inactive-day gap in this range."}</p>}
        {highlight && <p role="status">Highlighted gap: {highlight.from} through {highlight.to}. <button onClick={() => setHighlight(null)}>Clear highlight</button></p>}
        <h4>Current gap — all reliable recorded history, not selected range</h4>
        {current.state === "unavailable" ? <p>Current gap unavailable.</p>
          : current.state === "none" ? <p>0 full inactive days.</p>
            : <p>{current.lowerBound ? `At least ${current.days} fully tracked days; earlier history unknown` : `${current.days} full inactive days`}: {current.from} through {current.to}.</p>}
        <p>{currentReasons[current.reason]}</p>
        <p>Through yesterday{current.throughDate ? ` (${current.throughDate})` : ""}; today is in progress.</p>
        <h4>Last recorded activity — all recorded history</h4>
        <p>Since reliable tracking began; not limited to selected range.</p>
        {report.lastActivity.event ? <>
          <p><strong>{report.lastActivity.event.localDate}</strong> · {report.timeZone}</p>
          <EventDescription event={report.lastActivity.event} zone={report.timeZone} />
          <button onClick={() => {
            const date = report.lastActivity.event!.localDate;
            change({ from: date, to: date, selectedDate: date, timeZone: query.timeZone });
          }}>View date</button>
        </> : <p>No recorded progress since tracking began {formatInstant(report.coverage.trackingStartedAtUtc, report.timeZone)}.</p>}
      </section>
      <details className="card"><summary>About these records</summary>
        <p>Only the first reliably recorded completion of each task without substeps or individual substep counts. Reopening, rechecking, edits, imports and schedule changes do not add activity.</p>
        <p>Old history is unknown, not reconstructed. Retained completion-time names can differ from current task names. Removed items have no task link. With the default in-memory task store, tasks can disappear on restart while durable Progress records remain.</p>
      </details>
    </>}
  </section>;
}
