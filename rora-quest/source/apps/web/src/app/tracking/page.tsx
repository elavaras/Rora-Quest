"use client";

import { useEffect, useState } from "react";
import { getApiAuthHeaders, getApiBaseUrl } from "../lib/user-session";

type Streaks = { currentStreakDays: number; totalCompletedDays: number };
type Consistency = { avgProgressPercent: number; taskCount: number };
type Recommendation = { profile: string; suggestedMode: string | number; completionRatePercent: number };

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

function modeLabel(mode: string | number): string {
  if (typeof mode === "string") return mode;
  if (mode === 0) return "Green";
  if (mode === 1) return "Yellow";
  if (mode === 2) return "Red";
  return String(mode);
}

export default function TrackingPage() {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [streaks, setStreaks] = useState<Streaks | null>(null);
  const [consistency, setConsistency] = useState<Consistency | null>(null);
  const [recommendation, setRecommendation] = useState<Recommendation | null>(null);

  useEffect(() => {
    let alive = true;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const [streakData, consistencyData, recommendationData] = await Promise.all([
          apiCall<Streaks>("/api/tracking/streaks"),
          apiCall<Consistency>("/api/tracking/consistency"),
          apiCall<Recommendation>("/api/planning/recommendation")
        ]);
        if (!alive) return;
        setStreaks(streakData);
        setConsistency(consistencyData);
        setRecommendation(recommendationData);
      } catch (err) {
        if (!alive) return;
        setError(err instanceof Error ? err.message : "Failed to load tracking metrics.");
      } finally {
        if (alive) setLoading(false);
      }
    };
    void load();
    return () => {
      alive = false;
    };
  }, []);

  return (
    <section className="page">
      <div className="card">
        <h2>Streak & Consistency</h2>
        <p className="muted">Track consistency and adaptive recommendation baseline.</p>
      </div>

      {error && <div className="card error-text">{error}</div>}
      {loading && <div className="card">Loading streak and consistency…</div>}

      {!loading && !error && (
        <div className="grid-3">
          <div className="card">
            <h3>Current Streak</h3>
            <p>{streaks?.currentStreakDays ?? 0} day(s)</p>
            <p className="muted">Completed days tracked: {streaks?.totalCompletedDays ?? 0}</p>
          </div>
          <div className="card">
            <h3>Consistency</h3>
            <p>{(consistency?.avgProgressPercent ?? 0).toFixed(2)}%</p>
            <p className="muted">Across {consistency?.taskCount ?? 0} task(s)</p>
          </div>
          <div className="card">
            <h3>Adaptive Suggestion</h3>
            <p>
              {recommendation?.profile ?? "Balanced"} · {modeLabel(recommendation?.suggestedMode ?? "Yellow")} week
            </p>
            <p className="muted">
              Completion rate: {(recommendation?.completionRatePercent ?? 0).toFixed(2)}%
            </p>
          </div>
        </div>
      )}
    </section>
  );
}
