"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { getApiAuthHeaders } from "../lib/user-session";

type Category = {
  id: string;
  userId: string;
  name: string;
  parentCategoryId: string | null;
  createdAt: string;
};

type DraftItem = {
  id: string;
  order: number;
  text: string;
  weekNumber: number | null;
  subCategoryName: string | null;
  monthLabel: string | null;
};

type ImportPreview = {
  id: string;
  draftItems: DraftItem[];
  confidenceItems: DraftItem[];
};

type CommitResult = {
  createdCount: number;
  confidenceCount: number;
};

const API_BASE = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

async function apiCall<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers: {
      ...getApiAuthHeaders(),
      ...(init?.headers ?? {})
    },
    credentials: "include",
    cache: "no-store"
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `Request failed with status ${response.status}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

// Mirror of the backend commit scheduler (ApiEndpoints.cs): 3 problems/week on
// Monday/Wednesday/Friday, with overflow beyond 3 cascading into later weeks. This
// lets the preview show the SAME week/day placement the commit will actually create.
const SCHEDULE_DAY_OFFSETS = [0, 2, 4]; // Monday, Wednesday, Friday
const WEEK_CAPACITY = SCHEDULE_DAY_OFFSETS.length;

type ScheduledProblem = DraftItem & { plannedDate: string };

type ConfidenceGroup = {
  subCategory: string | null;
  items: DraftItem[];
};

type ScheduledWeek = {
  weekStart: string;
  problems: ScheduledProblem[];
  confidence: ConfidenceGroup[];
};

function parseIso(iso: string): Date {
  const [y, m, d] = iso.split("-").map(Number);
  return new Date(y, m - 1, d);
}

function toIso(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, "0");
  const d = String(date.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

function addDaysIso(iso: string, days: number): string {
  const date = parseIso(iso);
  date.setDate(date.getDate() + days);
  return toIso(date);
}

// Matches backend ResolveWeekStart: week N maps to baseline + (N-1) weeks.
function resolveWeekStartIso(weekNumber: number | null, baselineIso: string): string {
  if (weekNumber !== null && weekNumber > 1) {
    return addDaysIso(baselineIso, (weekNumber - 1) * 7);
  }
  return baselineIso;
}

function shortDayLabel(iso: string): string {
  return parseIso(iso).toLocaleDateString(undefined, {
    weekday: "short",
    month: "short",
    day: "numeric"
  });
}

function buildSchedule(preview: ImportPreview, baselineIso: string): ScheduledWeek[] {
  const fill = new Map<string, number>();
  const weeks = new Map<string, ScheduledWeek>();

  const ensureWeek = (weekStart: string): ScheduledWeek => {
    let week = weeks.get(weekStart);
    if (!week) {
      week = { weekStart, problems: [], confidence: [] };
      weeks.set(weekStart, week);
    }
    return week;
  };

  // Problems: same ordering + cascade as the backend commit.
  const orderedProblems = [...preview.draftItems].sort((a, b) => {
    const wa = a.weekNumber ?? Number.MAX_SAFE_INTEGER;
    const wb = b.weekNumber ?? Number.MAX_SAFE_INTEGER;
    if (wa !== wb) return wa - wb;
    return a.order - b.order;
  });

  for (const problem of orderedProblems) {
    const target = resolveWeekStartIso(problem.weekNumber, baselineIso);
    let weekStart = target;
    while ((fill.get(weekStart) ?? 0) >= WEEK_CAPACITY) {
      weekStart = addDaysIso(weekStart, 7);
    }
    const slot = fill.get(weekStart) ?? 0;
    fill.set(weekStart, slot + 1);
    const plannedDate = addDaysIso(weekStart, SCHEDULE_DAY_OFFSETS[slot]);
    ensureWeek(weekStart).problems.push({ ...problem, plannedDate });
  }

  // Confidence items stay on their parsed target week (not day-scheduled), grouped by sub-category.
  for (const conf of preview.confidenceItems) {
    const weekStart = resolveWeekStartIso(conf.weekNumber, baselineIso);
    const week = ensureWeek(weekStart);
    const sub = conf.subCategoryName;
    let group = week.confidence.find((g) => (g.subCategory ?? "") === (sub ?? ""));
    if (!group) {
      group = { subCategory: sub, items: [] };
      week.confidence.push(group);
    }
    group.items.push(conf);
  }

  for (const week of weeks.values()) {
    week.problems.sort((a, b) => a.plannedDate.localeCompare(b.plannedDate));
  }

  return [...weeks.values()].sort((a, b) => a.weekStart.localeCompare(b.weekStart));
}

function mondayOfIso(date: Date): string {
  const d = new Date(date);
  const day = d.getDay(); // 0=Sun..6=Sat
  const diff = (day + 6) % 7; // days since Monday
  d.setDate(d.getDate() - diff);
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const dd = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${dd}`;
}

function formatLongDate(iso: string): string {
  const [y, m, d] = iso.split("-").map(Number);
  return new Date(y, m - 1, d).toLocaleDateString(undefined, {
    weekday: "short",
    month: "short",
    day: "numeric",
    year: "numeric"
  });
}

export default function ChecklistPage() {
  const [categories, setCategories] = useState<Category[]>([]);
  const [categoryId, setCategoryId] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [startWeek, setStartWeek] = useState<string>(() => mondayOfIso(new Date()));
  const [rawText, setRawText] = useState("");

  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [selectedDrafts, setSelectedDrafts] = useState<Set<string>>(new Set());
  const [selectedConfidence, setSelectedConfidence] = useState<Set<string>>(new Set());
  const [parsing, setParsing] = useState(false);
  const [parseError, setParseError] = useState<string | null>(null);

  const [committing, setCommitting] = useState(false);
  const [commitResult, setCommitResult] = useState<CommitResult | null>(null);

  useEffect(() => {
    let cancelled = false;
    async function loadCategories() {
      try {
        setLoading(true);
        setError(null);
        const data = await apiCall<Category[]>("/api/categories");
        if (cancelled) return;
        const topLevel = data
          .filter((item) => item.parentCategoryId === null)
          .sort((a, b) => a.name.localeCompare(b.name));
        setCategories(topLevel);
        setCategoryId((current) => current || topLevel[0]?.id || "");
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Failed to load categories");
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }
    loadCategories();
    return () => {
      cancelled = true;
    };
  }, []);

  const hasCategories = categories.length > 0;
  const categoryName = categories.find((c) => c.id === categoryId)?.name ?? "";
  const schedule = useMemo(
    () => (preview ? buildSchedule(preview, startWeek) : []),
    [preview, startWeek]
  );

  function toggleDraft(id: string) {
    setSelectedDrafts((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  function toggleConfidence(id: string) {
    setSelectedConfidence((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  async function handleParse() {
    if (!categoryName || !rawText.trim()) return;
    try {
      setParsing(true);
      setParseError(null);
      setCommitResult(null);
      const result = await apiCall<ImportPreview>("/api/checklists/imports/bulk-text", {
        method: "POST",
        body: JSON.stringify({
          rawText,
          categoryName
        })
      });
      setPreview(result);
      setSelectedDrafts(new Set(result.draftItems.map((d) => d.id)));
      setSelectedConfidence(new Set(result.confidenceItems.map((c) => c.id)));
    } catch (err) {
      setParseError(err instanceof Error ? err.message : "Failed to parse checklist");
    } finally {
      setParsing(false);
    }
  }

  async function handleCommit() {
    if (!preview) return;
    try {
      setCommitting(true);
      setParseError(null);
      const result = await apiCall<CommitResult>(`/api/checklists/imports/${preview.id}/commit`, {
        method: "POST",
        body: JSON.stringify({
          selectedDraftIds: Array.from(selectedDrafts),
          selectedConfidenceIds: Array.from(selectedConfidence),
          startWeekDate: startWeek
        })
      });
      setCommitResult(result);
      setPreview(null);
    } catch (err) {
      setParseError(err instanceof Error ? err.message : "Failed to commit checklist");
    } finally {
      setCommitting(false);
    }
  }

  function handleStartOver() {
    setPreview(null);
    setCommitResult(null);
    setParseError(null);
  }

  const totalSelected = selectedDrafts.size + selectedConfidence.size;

  return (
    <section className="page">
      <div className="card">
        <h2>Bulk Text Checklist Intake</h2>
        <p className="muted">
          Select a category + study days, paste your checklist, then preview and commit. Sub-category is
          parsed from <code> Week &lt;number&gt;: &lt;SubCategory&gt; </code> headings;{" "}
          <code>Month &lt;n&gt;: …</code> lines are visual only, and a <code>Pattern confidence:</code>{" "}
          block becomes a per-week confidence checklist.
        </p>
      </div>

      {commitResult && (
        <div className="card commit-result">
          <p>
            ✅ Created <strong>{commitResult.createdCount}</strong> task
            {commitResult.createdCount === 1 ? "" : "s"} (each with 9 preparation sub-steps) and{" "}
            <strong>{commitResult.confidenceCount}</strong> confidence item
            {commitResult.confidenceCount === 1 ? "" : "s"}.
          </p>
          <Link href="/tasks">Go to Tasks by Week →</Link>
        </div>
      )}

      {!preview ? (
        <div className="grid-2">
          <div className="card">
            <label htmlFor="category-select">Category</label>
            <select
              id="category-select"
              value={categoryId}
              onChange={(event) => setCategoryId(event.target.value)}
              disabled={loading || !hasCategories}
            >
              {loading ? (
                <option value="">Loading categories…</option>
              ) : !hasCategories ? (
                <option value="">No categories yet</option>
              ) : (
                categories.map((category) => (
                  <option key={category.id} value={category.id}>
                    {category.name}
                  </option>
                ))
              )}
            </select>
            {error && <p className="error-text">{error}</p>}
            {!loading && !error && !hasCategories && (
              <p className="muted">
                No categories found. <Link href="/categories">Create one on the Categories screen</Link> first.
              </p>
            )}
            <br />
            <label htmlFor="start-week">Start week (Week 1 begins)</label>
            <input
              id="start-week"
              type="date"
              value={startWeek}
              onChange={(event) => {
                if (event.target.value) {
                  setStartWeek(mondayOfIso(new Date(`${event.target.value}T00:00:00`)));
                }
              }}
            />
            <p className="muted">
              Week 1 → <strong>{formatLongDate(startWeek)}</strong> (Monday). Later weeks follow in 7-day steps.
              Problems are scheduled <strong>Mon / Wed / Fri</strong> (3 per week); adjust individual days later on
              the Task by Week screen.
            </p>
            <p className="muted">Use the Categories screen to create/manage categories and sub-categories.</p>
          </div>

          <div className="card">
            <label>Checklist (bulk text)</label>
            <textarea
              rows={12}
              value={rawText}
              onChange={(event) => setRawText(event.target.value)}
              placeholder={
                "Month 1: Arrays, HashMap\nWeek 1: Arrays + HashMap\n Two Sum\n Contains Duplicate\n\nPattern confidence:\n I can identify HashMap problems."
              }
            />
            <br />
            <br />
            <button onClick={handleParse} disabled={!categoryId || !rawText.trim() || parsing}>
              {parsing ? "Parsing…" : "Parse Checklist"}
            </button>
            {parseError && <p className="error-text">{parseError}</p>}
          </div>
        </div>
      ) : (
        <div className="card">
          <div className="preview-header">
            <div>
              <h3>Preview &amp; Commit</h3>
              <p className="muted">
                {selectedDrafts.size} problem{selectedDrafts.size === 1 ? "" : "s"} and{" "}
                {selectedConfidence.size} confidence item{selectedConfidence.size === 1 ? "" : "s"} selected.
              </p>
            </div>
            <div className="preview-actions">
              <button className="secondary" onClick={handleStartOver} disabled={committing}>
                ← Start over
              </button>
              <button onClick={handleCommit} disabled={committing || totalSelected === 0}>
                {committing ? "Committing…" : `Commit ${totalSelected} item${totalSelected === 1 ? "" : "s"}`}
              </button>
            </div>
          </div>
          {parseError && <p className="error-text">{parseError}</p>}

          <p className="muted preview-schedule-note">
            Scheduled <strong>3 per week</strong> on Mon / Wed / Fri. Weeks with more than 3 problems
            spill over into the next week(s). Adjust individual days later on the Task by Week screen.
          </p>

          {schedule.map((week) => (
            <div key={week.weekStart} className="preview-week">
              <h5 className="preview-week-label">
                Week of {formatLongDate(week.weekStart)}
                <span className="preview-week-count">
                  {" "}· {week.problems.length} / {WEEK_CAPACITY} day{WEEK_CAPACITY === 1 ? "" : "s"} used
                </span>
              </h5>
              {week.problems.length > 0 && (
                <ul className="preview-list">
                  {week.problems.map((problem) => (
                    <li key={problem.id}>
                      <label className="preview-item">
                        <input
                          type="checkbox"
                          checked={selectedDrafts.has(problem.id)}
                          onChange={() => toggleDraft(problem.id)}
                        />
                        <span className="preview-problem-text">{problem.text}</span>
                        <span className="preview-problem-meta">
                          {shortDayLabel(problem.plannedDate)}
                          {problem.subCategoryName ? ` · ${problem.subCategoryName}` : ""}
                        </span>
                      </label>
                    </li>
                  ))}
                </ul>
              )}
              {week.confidence.map((group) => (
                <div key={group.subCategory ?? "__none__"} className="preview-confidence">
                  <span className="preview-confidence-label">
                    Pattern confidence{group.subCategory ? ` · ${group.subCategory}` : ""}
                  </span>
                  <ul className="preview-list">
                    {group.items.map((conf) => (
                      <li key={conf.id}>
                        <label className="preview-item">
                          <input
                            type="checkbox"
                            checked={selectedConfidence.has(conf.id)}
                            onChange={() => toggleConfidence(conf.id)}
                          />
                          <span>{conf.text}</span>
                        </label>
                      </li>
                    ))}
                  </ul>
                </div>
              ))}
            </div>
          ))}
        </div>
      )}
    </section>
  );
}
