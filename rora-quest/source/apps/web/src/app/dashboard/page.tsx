"use client";

import { useEffect, useMemo, useState } from "react";
import { getApiAuthHeaders, getApiBaseUrl } from "../lib/user-session";

type RangeType = "Weekly" | "Monthly" | "Custom";
type ProgressReport = { plannedTasks: number; avgProgressPercent: number };
type Scorecard = { completionRatePercent: number };
type TimelineItem = {
  plannedWeekStart: string;
  progressPercent?: number;
  ProgressPercent?: number;
};
type TimelineReport = { items: TimelineItem[] };
type WeekSummary = { weekStart: string; totalActualHours: number };

function ymd(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, "0");
  const d = String(date.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

function mondayOf(date: Date): Date {
  const d = new Date(date);
  const day = d.getDay();
  const diff = day === 0 ? -6 : 1 - day;
  d.setDate(d.getDate() + diff);
  d.setHours(0, 0, 0, 0);
  return d;
}

function monthRange(date: Date): { from: string; to: string } {
  const y = date.getFullYear();
  const m = date.getMonth();
  const from = new Date(y, m, 1);
  const to = new Date(y, m + 1, 0);
  return { from: ymd(from), to: ymd(to) };
}

function addDaysYmd(value: string, delta: number): string {
  const [y, m, d] = value.split("-").map(Number);
  const date = new Date(y, (m ?? 1) - 1, d ?? 1);
  date.setDate(date.getDate() + delta);
  return ymd(date);
}

async function apiCall<T>(path: string): Promise<T> {
  const response = await fetch(`${getApiBaseUrl()}${path}`, {
    headers: {
      ...getApiAuthHeaders()
    },
    credentials: "include",
    cache: "no-store"
  });
  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `Request failed with status ${response.status}`);
  }
  return (await response.json()) as T;
}

function pct(value: number): number {
  return Math.max(0, Math.min(100, Number.isFinite(value) ? value : 0));
}

function DonutChart({ value, color }: { value: number; color: string }) {
  const clamped = pct(value);
  const radius = 34;
  const circumference = 2 * Math.PI * radius;
  const offset = circumference * (1 - clamped / 100);
  return (
    <div className="donut-wrap" aria-label={`Chart ${clamped.toFixed(1)} percent`}>
      <svg viewBox="0 0 90 90" className="donut-svg" role="img">
        <circle cx="45" cy="45" r={radius} className="donut-track" />
        <circle
          cx="45"
          cy="45"
          r={radius}
          className="donut-value"
          style={{
            stroke: color,
            strokeDasharray: `${circumference} ${circumference}`,
            strokeDashoffset: offset
          }}
        />
      </svg>
      <span>{clamped.toFixed(1)}%</span>
    </div>
  );
}

export default function DashboardPage() {
  const now = useMemo(() => new Date(), []);
  const weeklyFrom = useMemo(() => ymd(mondayOf(now)), [now]);
  const weeklyTo = useMemo(() => ymd(new Date(new Date(weeklyFrom).getTime() + 6 * 86_400_000)), [weeklyFrom]);
  const monthly = useMemo(() => monthRange(now), [now]);

  const [rangeType, setRangeType] = useState<RangeType>("Weekly");
  const [from, setFrom] = useState(weeklyFrom);
  const [to, setTo] = useState(weeklyTo);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState<ProgressReport | null>(null);
  const [scorecard, setScorecard] = useState<Scorecard | null>(null);
  const [timeline, setTimeline] = useState<TimelineItem[]>([]);
  const [weekSummary, setWeekSummary] = useState<WeekSummary | null>(null);

  const handleFromChange = (value: string) => {
    if (rangeType === "Weekly") {
      setFrom(value);
      setTo(addDaysYmd(value, 6));
      return;
    }
    setFrom(value);
  };

  const handleToChange = (value: string) => {
    if (rangeType === "Weekly") {
      setTo(value);
      setFrom(addDaysYmd(value, -6));
      return;
    }
    setTo(value);
  };

  useEffect(() => {
    if (rangeType === "Weekly") {
      setFrom(weeklyFrom);
      setTo(weeklyTo);
    } else if (rangeType === "Monthly") {
      setFrom(monthly.from);
      setTo(monthly.to);
    }
  }, [monthly.from, monthly.to, rangeType, weeklyFrom, weeklyTo]);

  useEffect(() => {
    let alive = true;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const qs = new URLSearchParams({ from, to }).toString();
        const [progressResult, scoreResult, timelineResult, weekSummaryResult] = await Promise.allSettled([
          apiCall<ProgressReport>(`/api/reports/progress?${qs}`),
          apiCall<Scorecard>(`/api/scorecard?${qs}`),
          apiCall<TimelineReport>(`/api/reports/timeline?${qs}`),
          apiCall<WeekSummary>(`/api/tasks/week-summary?weekStart=${weeklyFrom}`)
        ]);
        if (!alive) return;
        if (progressResult.status === "fulfilled") setProgress(progressResult.value);
        if (scoreResult.status === "fulfilled") setScorecard(scoreResult.value);
        if (timelineResult.status === "fulfilled") setTimeline(timelineResult.value.items ?? []);
        if (weekSummaryResult.status === "fulfilled") setWeekSummary(weekSummaryResult.value);
        // Surface the first error from required calls (progress/score/timeline); ignore weekSummary failures
        const firstError = [progressResult, scoreResult, timelineResult].find(
          (r) => r.status === "rejected"
        ) as PromiseRejectedResult | undefined;
        if (firstError) {
          setError(firstError.reason instanceof Error ? firstError.reason.message : "Failed to load dashboard metrics.");
        }
      } catch (err) {
        if (!alive) return;
        setError(err instanceof Error ? err.message : "Failed to load dashboard metrics.");
      } finally {
        if (alive) setLoading(false);
      }
    };
    void load();
    return () => {
      alive = false;
    };
  }, [from, to, weeklyFrom]);

  const completionRate = pct(scorecard?.completionRatePercent ?? 0);
  const avgProgress = pct(progress?.avgProgressPercent ?? 0);
  const timelineByWeek = useMemo(() => {
    const map = new Map<string, { count: number; total: number }>();
    for (const item of timeline) {
      const week = item.plannedWeekStart;
      const raw = item.progressPercent ?? item.ProgressPercent ?? 0;
      const current = map.get(week) ?? { count: 0, total: 0 };
      current.count += 1;
      current.total += pct(raw);
      map.set(week, current);
    }
    return Array.from(map.entries())
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([week, v]) => ({
        week,
        avg: v.count > 0 ? v.total / v.count : 0,
        count: v.count
      }));
  }, [timeline]);

  return (
    <section className="page">
      <div className="card">
        <h2>Dashboard</h2>
        <p className="muted">Weekly / Monthly / Custom timeline filters.</p>
        <div className="grid-3">
          <div>
            <label>Range Type</label>
            <select value={rangeType} onChange={(e) => setRangeType(e.target.value as RangeType)}>
              <option>Weekly</option>
              <option>Monthly</option>
              <option>Custom</option>
            </select>
          </div>
          <div>
            <label>From</label>
            <input type="date" value={from} onChange={(e) => handleFromChange(e.target.value)} />
          </div>
          <div>
            <label>To</label>
            <input type="date" value={to} onChange={(e) => handleToChange(e.target.value)} />
          </div>
        </div>
        {rangeType === "Weekly" && (
          <p className="muted" style={{ marginTop: "0.5rem" }}>
            Weekly mode uses a fixed 7-day window.
          </p>
        )}
      </div>

      {error && <div className="card error-text">{error}</div>}
      {loading && <div className="card">Loading dashboard metrics…</div>}

      <div className="grid-3" style={{ gridTemplateColumns: "repeat(4, minmax(0, 1fr))" }}>
        <div className="card">
          <h3>Planned Tasks</h3>
          <p>{progress?.plannedTasks ?? 0}</p>
        </div>
        <div className="card dashboard-metric-card">
          <h3>Completion Rate</h3>
          <DonutChart value={completionRate} color="#2563eb" />
        </div>
        <div className="card dashboard-metric-card">
          <h3>Avg Progress</h3>
          <DonutChart value={avgProgress} color="#059669" />
        </div>
        <div className="card">
          <h3>Actual Hours (This Week)</h3>
          <p style={{ fontSize: "1.5rem", fontWeight: 600 }}>
            {weekSummary !== null ? `${weekSummary.totalActualHours.toFixed(1)}h` : "—"}
          </p>
        </div>
      </div>

      <div className="card">
        <h3>Weekly Progress Trend</h3>
        {timelineByWeek.length === 0 ? (
          <div className="trend-chart">
            <div className="trend-row">
              <div className="trend-label">
                <strong>Selected Range</strong>
                <span className="muted">{progress?.plannedTasks ?? 0} task(s)</span>
              </div>
              <div className="trend-bar-wrap">
                <div className="trend-bar" style={{ width: `${avgProgress}%` }} />
              </div>
              <div className="trend-value">{avgProgress.toFixed(1)}%</div>
            </div>
          </div>
        ) : (
          <div className="trend-chart">
            {timelineByWeek.map((row) => (
              <div key={row.week} className="trend-row">
                <div className="trend-label">
                  <strong>{row.week}</strong>
                  <span className="muted">{row.count} task(s)</span>
                </div>
                <div className="trend-bar-wrap">
                  <div className="trend-bar" style={{ width: `${pct(row.avg)}%` }} />
                </div>
                <div className="trend-value">{pct(row.avg).toFixed(1)}%</div>
              </div>
            ))}
          </div>
        )}
      </div>

      <div className="card">
        <h3>Visual Progress Bars</h3>
        <div style={{ display: "grid", gap: "0.65rem" }}>
          <div>
            <div className="row" style={{ justifyContent: "space-between" }}>
              <span>Completion</span>
              <strong>{completionRate.toFixed(1)}%</strong>
            </div>
            <div className="trend-bar-wrap">
              <div className="trend-bar" style={{ width: `${completionRate}%` }} />
            </div>
          </div>
          <div>
            <div className="row" style={{ justifyContent: "space-between" }}>
              <span>Average Progress</span>
              <strong>{avgProgress.toFixed(1)}%</strong>
            </div>
            <div className="trend-bar-wrap">
              <div className="trend-bar" style={{ width: `${avgProgress}%` }} />
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
