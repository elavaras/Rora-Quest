"use client";

import { useEffect, useMemo, useState } from "react";
import { getApiAuthHeaders } from "../lib/user-session";

const API_BASE = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

type RangeType = "Weekly" | "Monthly" | "Custom";
type Scorecard = {
  plannedTasks: number;
  completedTasks: number;
  completionRatePercent: number;
  carryOverMoved: number;
  carryOverPending: number;
};

function ymd(date: Date): string {
  return date.toISOString().slice(0, 10);
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

async function apiCall<T>(path: string): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
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

export default function ScorecardPage() {
  const now = useMemo(() => new Date(), []);
  const weeklyFrom = useMemo(() => ymd(mondayOf(now)), [now]);
  const weeklyTo = useMemo(() => ymd(new Date(new Date(weeklyFrom).getTime() + 6 * 86_400_000)), [weeklyFrom]);
  const monthly = useMemo(() => monthRange(now), [now]);

  const [rangeType, setRangeType] = useState<RangeType>("Weekly");
  const [from, setFrom] = useState(weeklyFrom);
  const [to, setTo] = useState(weeklyTo);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [scorecard, setScorecard] = useState<Scorecard | null>(null);

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
        const data = await apiCall<Scorecard>(`/api/scorecard?${qs}`);
        if (!alive) return;
        setScorecard(data);
      } catch (err) {
        if (!alive) return;
        setError(err instanceof Error ? err.message : "Failed to load scorecard.");
      } finally {
        if (alive) setLoading(false);
      }
    };
    void load();
    return () => {
      alive = false;
    };
  }, [from, to]);

  return (
    <section className="page">
      <div className="card">
        <h2>Simple Scorecard</h2>
        <p className="muted">Binary completion metrics and carry-over tracking.</p>
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
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </div>
          <div>
            <label>To</label>
            <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </div>
        </div>
      </div>

      {error && <div className="card error-text">{error}</div>}
      {loading && <div className="card">Loading scorecard…</div>}

      {!loading && !error && (
        <div className="grid-2">
          <div className="card">
            <h3>Selected Window</h3>
            <p>Planned: {scorecard?.plannedTasks ?? 0}</p>
            <p>Completed: {scorecard?.completedTasks ?? 0}</p>
            <p>Carry-over moved: {scorecard?.carryOverMoved ?? 0}</p>
            <p>Carry-over pending: {scorecard?.carryOverPending ?? 0}</p>
            <p>
              <strong>Completion Rate: {(scorecard?.completionRatePercent ?? 0).toFixed(2)}%</strong>
            </p>
          </div>
          <div className="card">
            <h3>Notes</h3>
            <p className="muted">This page now uses live scorecard metrics from your tracked tasks.</p>
          </div>
        </div>
      )}
    </section>
  );
}
