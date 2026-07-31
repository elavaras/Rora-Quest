"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { getApiAuthHeaders, getApiBaseUrl } from "../lib/user-session";

const DAY_LABELS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
const WORKLOAD_MODES = ["Green", "Yellow", "Red"] as const;
const DIFFICULTIES = ["Easy", "Medium", "Hard"] as const;
const DSA_CATEGORY_NAME = "DSA";
const SPILLOVER_REASONS = [
  "DRI interruption",
  "High Priority Work",
  "Underestimated effort",
  "Personal unavailability",
  "Blocked on a problem"
];

type SubStep = { id: string; isDone: boolean; weight: number };

type TaskItem = {
  id: string;
  title: string;
  status: "Todo" | "InProgress" | "Done" | "Cancelled" | "Skipped";
  categoryId: string | null;
  subCategoryId: string | null;
  plannedWeekStart: string;
  plannedDate: string | null;
  dueDate: string | null;
  pattern: string | null;
  difficulty: (typeof DIFFICULTIES)[number] | null;
  subSteps: SubStep[];
  actualHours: number | null;
};

type Category = { id: string; name: string; parentCategoryId: string | null };
type WeekPlan = { weekStartDate: string; workloadMode: (typeof WORKLOAD_MODES)[number]; notes: string | null };
type WeekConfidenceItem = {
  id: string;
  weekStart: string;
  label: string;
  text: string;
  isDone: boolean;
  orderIndex: number;
};
type ViewMode = "grid" | "list";

type PendingMove = { taskId: string; taskTitle: string; toWeekStart: string; toPlannedDate: string };

function isDsaCategoryId(categoryId: string | null, categoryById: Map<string, Category>): boolean {
  if (!categoryId) {
    return false;
  }

  let currentId: string | null = categoryId;
  const visited = new Set<string>();
  while (currentId && !visited.has(currentId)) {
    visited.add(currentId);
    const category = categoryById.get(currentId);
    if (!category) {
      return false;
    }
    if (category.name.trim().toUpperCase() === DSA_CATEGORY_NAME) {
      return true;
    }
    currentId = category.parentCategoryId;
  }

  return false;
}

async function apiCall<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${getApiBaseUrl()}${path}`, {
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

// --- date helpers (local, timezone-safe YYYY-MM-DD) ---
function ymd(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}
function parseYmd(s: string): Date {
  const [y, m, d] = s.split("-").map(Number);
  return new Date(y, m - 1, d);
}
function addDays(d: Date, n: number): Date {
  const x = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  x.setDate(x.getDate() + n);
  return x;
}
function mondayOf(d: Date): Date {
  const x = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const dow = (x.getDay() + 6) % 7; // Mon = 0
  x.setDate(x.getDate() - dow);
  return x;
}
function longDate(d: Date): string {
  return d.toLocaleDateString(undefined, { month: "short", day: "numeric" });
}

function taskProgress(t: TaskItem): number {
  if (t.subSteps && t.subSteps.length > 0) {
    const totalWeight = t.subSteps.reduce((sum, s) => sum + (s.weight ?? 0), 0);
    if (totalWeight > 0) {
      const doneWeight = t.subSteps
        .filter((s) => s.isDone)
        .reduce((sum, s) => sum + (s.weight ?? 0), 0);
      return Math.round((doneWeight / totalWeight) * 100);
    }
    const done = t.subSteps.filter((s) => s.isDone).length;
    return Math.round((done / t.subSteps.length) * 100);
  }
  if (t.status === "Done") return 100;
  return 0;
}

function TaskCard({
  task,
  weekStart,
  subCategoryName,
  onMoveSameWeek,
  onMoveCrossWeek
}: {
  task: TaskItem;
  weekStart: Date;
  subCategoryName?: string | null;
  onMoveSameWeek: (taskId: string, plannedDate: string) => void;
  onMoveCrossWeek: (task: TaskItem, direction: -1 | 1) => void;
}) {
  const progress = taskProgress(task);
  const statusClass = task.status.toLowerCase();
  return (
    <div className="task-card">
      <Link className="t-title" href={`/tasks/${task.id}`}>
        {task.title}
      </Link>
      {subCategoryName && <div className="t-subcat">{subCategoryName}</div>}
      <div className="t-meta">
        <span className={`chip ${statusClass}`}>{task.status}</span> · {progress}%
      </div>
      <div className="progress-bar">
        <div className="progress-fill" style={{ width: `${progress}%` }} />
      </div>
      <div className="card-actions">
        <select
          value=""
          aria-label="Move task"
          onChange={(event) => {
            const v = event.target.value;
            event.target.value = "";
            if (!v) return;
            if (v === "prev") onMoveCrossWeek(task, -1);
            else if (v === "next") onMoveCrossWeek(task, 1);
            else onMoveSameWeek(task.id, v);
          }}
        >
          <option value="">Move to…</option>
          <optgroup label="This week">
            {DAY_LABELS.map((label, i) => {
              const date = ymd(addDays(weekStart, i));
              return (
                <option key={date} value={date}>
                  {label} {longDate(addDays(weekStart, i))}
                </option>
              );
            })}
          </optgroup>
          <optgroup label="Other week">
            <option value="prev">← Previous week (same day)</option>
            <option value="next">→ Next week (same day)</option>
          </optgroup>
        </select>
      </div>
    </div>
  );
}

function AddTaskForm({
  categories,
  isDsaCategoryId,
  onAdd,
  onCancel
}: {
  categories: Category[];
  isDsaCategoryId: (categoryId: string | null) => boolean;
  onAdd: (
    title: string,
    categoryId: string | null,
    pattern: string | null,
    difficulty: string | null
  ) => void;
  onCancel: () => void;
}) {
  const [title, setTitle] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [pattern, setPattern] = useState("");
  const [difficulty, setDifficulty] = useState("");
  const selectedCategoryIsDsa = isDsaCategoryId(categoryId || null);
  const submit = () =>
    onAdd(title.trim(), categoryId || null, pattern.trim() || null, difficulty || null);
  return (
    <div className="mini-form">
      <input
        autoFocus
        placeholder="Task title"
        value={title}
        onChange={(e) => setTitle(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter" && title.trim()) submit();
          if (e.key === "Escape") onCancel();
        }}
      />
      <select value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
        <option value="">No category</option>
        {categories.map((c) => (
          <option key={c.id} value={c.id} disabled={isDsaCategoryId(c.id)}>
            {c.parentCategoryId ? "— " : ""}
            {c.name}
            {isDsaCategoryId(c.id) ? " (locked)" : ""}
          </option>
        ))}
      </select>
      <input
        placeholder="Pattern (e.g., Sliding Window)"
        value={pattern}
        onChange={(e) => setPattern(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter" && title.trim()) submit();
          if (e.key === "Escape") onCancel();
        }}
      />
      <select value={difficulty} onChange={(e) => setDifficulty(e.target.value)}>
        <option value="">No difficulty</option>
        {DIFFICULTIES.map((d) => (
          <option key={d} value={d}>
            {d}
          </option>
        ))}
      </select>
      <div className="row">
        <button disabled={!title.trim() || selectedCategoryIsDsa} onClick={submit}>
          Add
        </button>
        <button className="secondary" onClick={onCancel}>
          Cancel
        </button>
      </div>
      {selectedCategoryIsDsa && (
        <p className="muted" style={{ margin: "0.5rem 0 0 0" }}>
          Manual task creation is disabled for DSA categories.
        </p>
      )}
    </div>
  );
}

export default function TasksPage() {
  const [weekStart, setWeekStart] = useState<Date>(() => mondayOf(new Date()));
  const [view, setView] = useState<ViewMode>("grid");
  const [tasks, setTasks] = useState<TaskItem[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [mode, setMode] = useState<(typeof WORKLOAD_MODES)[number]>("Yellow");
  const [confidence, setConfidence] = useState<WeekConfidenceItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [addFor, setAddFor] = useState<string | null>(null); // a YYYY-MM-DD date, or "unscheduled"
  const [pendingMove, setPendingMove] = useState<PendingMove | null>(null);
  const [moveReason, setMoveReason] = useState(SPILLOVER_REASONS[0]);
  const [selectMode, setSelectMode] = useState(false);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [bulkDeleting, setBulkDeleting] = useState(false);
  const didAutoJumpToPlannedWeek = useRef(false);
  const initialWeekStartYmd = useRef(ymd(mondayOf(new Date())));

  const weekStartYmd = ymd(weekStart);
  const todayYmd = ymd(new Date());

  const subCategoryName = useMemo(() => {
    const map = new Map<string, string>();
    for (const c of categories) map.set(c.id, c.name);
    return (id: string | null) => (id ? map.get(id) ?? null : null);
  }, [categories]);
  const categoryById = useMemo(() => {
    const map = new Map<string, Category>();
    for (const category of categories) {
      map.set(category.id, category);
    }
    return map;
  }, [categories]);
  const isDsaCategory = useCallback(
    (categoryId: string | null) => isDsaCategoryId(categoryId, categoryById),
    [categoryById]
  );

  const loadWeek = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [taskList, plan, confidenceList] = await Promise.all([
        apiCall<TaskItem[]>(`/api/tasks?weekStart=${weekStartYmd}`),
        apiCall<WeekPlan>(`/api/week-plans/${weekStartYmd}`).catch(() => null),
        apiCall<WeekConfidenceItem[]>(`/api/week-confidence/${weekStartYmd}`).catch(() => [])
      ]);
      setTasks(taskList);
      setMode(plan?.workloadMode ?? "Yellow");
      setConfidence(confidenceList ?? []);
      if (
        !didAutoJumpToPlannedWeek.current &&
        weekStartYmd === initialWeekStartYmd.current &&
        taskList.length === 0
      ) {
        didAutoJumpToPlannedWeek.current = true;
        const allTasks = await apiCall<TaskItem[]>("/api/tasks");
        const targetWeek = [...allTasks]
          .map((task) => task.plannedWeekStart)
          .filter(Boolean)
          .sort((a, b) => a.localeCompare(b))
          .find((week) => week >= weekStartYmd)
          ?? [...allTasks]
            .map((task) => task.plannedWeekStart)
            .filter(Boolean)
            .sort((a, b) => a.localeCompare(b))[0];
        if (targetWeek && targetWeek !== weekStartYmd) {
          setWeekStart(parseYmd(targetWeek));
          flash("Showing the next week that has planned tasks.");
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load tasks.");
    } finally {
      setLoading(false);
    }
  }, [weekStartYmd]);

  useEffect(() => {
    loadWeek();
  }, [loadWeek]);

  useEffect(() => {
    // Reset selection when navigating to a different week.
    setSelectMode(false);
    setSelectedIds(new Set());
  }, [weekStartYmd]);

  useEffect(() => {
    apiCall<Category[]>("/api/categories")
      .then(setCategories)
      .catch(() => setCategories([]));
  }, []);

  const buckets = useMemo(() => {
    const byDay: TaskItem[][] = [[], [], [], [], [], [], []];
    const unscheduled: TaskItem[] = [];
    for (const t of tasks) {
      if (!t.plannedDate) {
        unscheduled.push(t);
        continue;
      }
      const idx = Math.round((parseYmd(t.plannedDate).getTime() - weekStart.getTime()) / 86_400_000);
      if (idx >= 0 && idx < 7) byDay[idx].push(t);
      else unscheduled.push(t);
    }
    return { byDay, unscheduled };
  }, [tasks, weekStart]);

  const flash = (text: string) => {
    setMessage(text);
    window.setTimeout(() => setMessage(null), 2500);
  };

  const createTask = async (
    title: string,
    categoryId: string | null,
    pattern: string | null,
    difficulty: string | null,
    target: string
  ) => {
    try {
      const base =
        target === "unscheduled"
          ? { title, categoryId, plannedWeekStart: weekStartYmd }
          : { title, categoryId, plannedDate: target };
      const body = { ...base, pattern, difficulty };
      await apiCall<TaskItem>("/api/tasks", { method: "POST", body: JSON.stringify(body) });
      setAddFor(null);
      flash("Task added.");
      await loadWeek();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to add task.");
    }
  };

  const moveSameWeek = async (taskId: string, plannedDate: string) => {
    try {
      await apiCall(`/api/tasks/${taskId}`, {
        method: "PATCH",
        body: JSON.stringify({ plannedDate })
      });
      flash("Task moved.");
      await loadWeek();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to move task.");
    }
  };

  const toggleSelect = (taskId: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(taskId)) next.delete(taskId);
      else next.add(taskId);
      return next;
    });
  };

  const exitSelectMode = () => {
    setSelectMode(false);
    setSelectedIds(new Set());
  };

  const bulkDeleteSelected = async () => {
    const ids = tasks.map((t) => t.id).filter((id) => selectedIds.has(id));
    if (ids.length === 0) return;
    const confirmed = window.confirm(
      `Delete ${ids.length} task${ids.length === 1 ? "" : "s"}? This also removes their sub-steps and cannot be undone.`
    );
    if (!confirmed) return;
    setBulkDeleting(true);
    setError(null);
    try {
      await apiCall("/api/tasks/bulk-delete", {
        method: "POST",
        body: JSON.stringify({ taskIds: ids })
      });
      flash(`Deleted ${ids.length} task${ids.length === 1 ? "" : "s"}.`);
      exitSelectMode();
      await loadWeek();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to delete tasks.");
    } finally {
      setBulkDeleting(false);
    }
  };

  const startCrossWeekMove = (task: TaskItem, direction: -1 | 1) => {
    const targetWeek = addDays(weekStart, direction * 7);
    // keep the same weekday if the task currently has a day, else default to Monday of the target week
    const currentIdx = task.plannedDate
      ? Math.round((parseYmd(task.plannedDate).getTime() - weekStart.getTime()) / 86_400_000)
      : 0;
    const dayIdx = currentIdx >= 0 && currentIdx < 7 ? currentIdx : 0;
    setMoveReason(SPILLOVER_REASONS[0]);
    setPendingMove({
      taskId: task.id,
      taskTitle: task.title,
      toWeekStart: ymd(targetWeek),
      toPlannedDate: ymd(addDays(targetWeek, dayIdx))
    });
  };

  const confirmCrossWeekMove = async () => {
    if (!pendingMove) return;
    try {
      await apiCall("/api/tasks/spillover", {
        method: "POST",
        body: JSON.stringify({
          taskIds: [pendingMove.taskId],
          toWeekStart: pendingMove.toWeekStart,
          reason: moveReason
        })
      });
      await apiCall(`/api/tasks/${pendingMove.taskId}`, {
        method: "PATCH",
        body: JSON.stringify({ plannedDate: pendingMove.toPlannedDate, plannedWeekStart: pendingMove.toWeekStart })
      });
      setPendingMove(null);
      flash("Task moved to another week.");
      await loadWeek();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to move task.");
      setPendingMove(null);
    }
  };

  const changeMode = async (next: (typeof WORKLOAD_MODES)[number]) => {
    setMode(next);
    try {
      await apiCall(`/api/week-plans/${weekStartYmd}`, {
        method: "PUT",
        body: JSON.stringify({ workloadMode: next, notes: null })
      });
      flash(`Workload mode set to ${next}.`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to update workload mode.");
    }
  };

  const toggleConfidence = async (item: WeekConfidenceItem) => {
    const nextDone = !item.isDone;
    setConfidence((prev) => prev.map((c) => (c.id === item.id ? { ...c, isDone: nextDone } : c)));
    try {
      await apiCall(`/api/week-confidence/${item.id}`, {
        method: "PATCH",
        body: JSON.stringify({ isDone: nextDone })
      });
    } catch (err) {
      setConfidence((prev) => prev.map((c) => (c.id === item.id ? { ...c, isDone: item.isDone } : c)));
      setError(err instanceof Error ? err.message : "Failed to update confidence item.");
    }
  };

  const confidenceGroups = useMemo(() => {
    const order: string[] = [];
    const map = new Map<string, WeekConfidenceItem[]>();
    for (const item of [...confidence].sort((a, b) => a.orderIndex - b.orderIndex)) {
      if (!map.has(item.label)) {
        map.set(item.label, []);
        order.push(item.label);
      }
      map.get(item.label)!.push(item);
    }
    return order.map((label) => ({ label, items: map.get(label)! }));
  }, [confidence]);

  const weekEnd = addDays(weekStart, 6);
  const weekTitle = `Week of ${longDate(weekStart)} – ${longDate(weekEnd)}, ${weekEnd.getFullYear()}`;

  const totalActualHours = tasks.reduce((sum, t) => sum + (t.actualHours ?? 0), 0);

  const allWeekTaskIds = tasks.map((t) => t.id);
  const selectedCount = allWeekTaskIds.filter((id) => selectedIds.has(id)).length;
  const allWeekSelected =
    allWeekTaskIds.length > 0 && selectedCount === allWeekTaskIds.length;
  const toggleSelectAllWeek = () => {
    setSelectedIds(allWeekSelected ? new Set() : new Set(allWeekTaskIds));
  };

  const renderBulkToolbar = () =>
    tasks.length === 0 ? null : selectMode ? (
      <div className="bulk-toolbar">
        <label className="bulk-selectall">
          <input type="checkbox" checked={allWeekSelected} onChange={toggleSelectAllWeek} />
          <span>All ({allWeekTaskIds.length})</span>
        </label>
        <button
          className="danger"
          disabled={selectedCount === 0 || bulkDeleting}
          onClick={bulkDeleteSelected}
        >
          {bulkDeleting ? "Deleting…" : `Delete selected (${selectedCount})`}
        </button>
        <button className="secondary" onClick={exitSelectMode} disabled={bulkDeleting}>
          Cancel
        </button>
      </div>
    ) : (
      <button className="col-add" onClick={() => setSelectMode(true)}>
        Select
      </button>
    );

  return (
    <section className="page">
      <div className="card">
        <div className="week-toolbar">
          <div className="week-nav">
            <button onClick={() => setWeekStart(addDays(weekStart, -7))}>‹ Prev</button>
            <button onClick={() => setWeekStart(mondayOf(new Date()))}>This Week</button>
            <button onClick={() => setWeekStart(addDays(weekStart, 7))}>Next ›</button>
          </div>
          <h2 className="week-title">{weekTitle}</h2>
          <div className="inline-actions">
            <div className="mode-select">
              <span className={`mode-dot ${mode.toLowerCase()}`} />
              <label style={{ margin: 0 }}>Workload</label>
              <select value={mode} onChange={(e) => changeMode(e.target.value as (typeof WORKLOAD_MODES)[number])}>
                {WORKLOAD_MODES.map((m) => (
                  <option key={m} value={m}>
                    {m}
                  </option>
                ))}
              </select>
            </div>
            <div className="seg">
              <button className={view === "grid" ? "active" : ""} onClick={() => setView("grid")}>
                Grid
              </button>
              <button className={view === "list" ? "active" : ""} onClick={() => setView("list")}>
                List
              </button>
            </div>
            {renderBulkToolbar()}
          </div>
        </div>
        {!loading && (
          <p className="muted" style={{ fontSize: "0.875rem", margin: "0.25rem 0 0 0" }}>
            Total actual hours: {totalActualHours.toFixed(1)}h
          </p>
        )}
        {message && <p className="success-text">{message}</p>}
        {error && <p className="error-text">{error}</p>}
      </div>

      {!loading && confidenceGroups.length > 0 && (
        <div className="card confidence-card">
          <h3 className="confidence-title">Pattern Confidence</h3>
          <p className="muted">Self-assessment checklist for this week.</p>
          {confidenceGroups.map((group) => (
            <div key={group.label} className="confidence-group">
              {group.label && <h4 className="confidence-group-label">{group.label}</h4>}
              <ul className="confidence-list">
                {group.items.map((item) => (
                  <li key={item.id}>
                    <label className={`confidence-item ${item.isDone ? "done" : ""}`}>
                      <input
                        type="checkbox"
                        checked={item.isDone}
                        onChange={() => toggleConfidence(item)}
                      />
                      <span>{item.text}</span>
                    </label>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}

      {loading ? (
        <div className="card">
          <p className="muted">Loading week…</p>
        </div>
      ) : view === "grid" ? (
        <div className="week-grid-wrap">
          <div className="week-grid">
            {DAY_LABELS.map((label, i) => {
              const date = addDays(weekStart, i);
              const dateStr = ymd(date);
              const dayTasks = buckets.byDay[i];
              return (
                <div key={dateStr} className={`day-col ${dateStr === todayYmd ? "today" : ""}`}>
                  <div className="day-col-head">
                    <div>
                      <div className="dow">{label}</div>
                      <div className="dnum">{longDate(date)}</div>
                    </div>
                    <button className="col-add" onClick={() => setAddFor(addFor === dateStr ? null : dateStr)}>
                      ＋ Add
                    </button>
                  </div>
                  {dayTasks.length === 0 && addFor !== dateStr && <p className="muted" style={{ fontSize: 12 }}>No tasks</p>}
                  {dayTasks.map((task) =>
                    selectMode ? (
                      <label key={task.id} className="select-wrap">
                        <input
                          type="checkbox"
                          className="select-box"
                          checked={selectedIds.has(task.id)}
                          onChange={() => toggleSelect(task.id)}
                        />
                        <TaskCard
                          task={task}
                          weekStart={weekStart}
                          subCategoryName={subCategoryName(task.subCategoryId)}
                          onMoveSameWeek={moveSameWeek}
                          onMoveCrossWeek={startCrossWeekMove}
                        />
                      </label>
                    ) : (
                      <TaskCard
                        key={task.id}
                        task={task}
                        weekStart={weekStart}
                        subCategoryName={subCategoryName(task.subCategoryId)}
                        onMoveSameWeek={moveSameWeek}
                        onMoveCrossWeek={startCrossWeekMove}
                      />
                    )
                  )}
                  {addFor === dateStr && (
                    <AddTaskForm
                      categories={categories}
                      isDsaCategoryId={isDsaCategory}
                      onAdd={(title, categoryId, pattern, difficulty) =>
                        createTask(title, categoryId, pattern, difficulty, dateStr)
                      }
                      onCancel={() => setAddFor(null)}
                    />
                  )}
                </div>
              );
            })}
          </div>

          <div className="card" style={{ marginTop: 12 }}>
            <div className="day-col-head">
              <h3 style={{ margin: 0 }}>Unscheduled (this week)</h3>
              <div className="row" style={{ gap: 6 }}>
                <button className="col-add" onClick={() => setAddFor(addFor === "unscheduled" ? null : "unscheduled")}>
                  ＋ Add
                </button>
              </div>
            </div>
            {buckets.unscheduled.length === 0 && addFor !== "unscheduled" && (
              <p className="muted">Nothing unscheduled this week.</p>
            )}
            <div className="grid-3" style={{ marginTop: 8 }}>
              {buckets.unscheduled.map((task) =>
                selectMode ? (
                  <label key={task.id} className="select-wrap">
                    <input
                      type="checkbox"
                      className="select-box"
                      checked={selectedIds.has(task.id)}
                      onChange={() => toggleSelect(task.id)}
                    />
                    <TaskCard
                      task={task}
                      weekStart={weekStart}
                      subCategoryName={subCategoryName(task.subCategoryId)}
                      onMoveSameWeek={moveSameWeek}
                      onMoveCrossWeek={startCrossWeekMove}
                    />
                  </label>
                ) : (
                  <TaskCard
                    key={task.id}
                    task={task}
                    weekStart={weekStart}
                    subCategoryName={subCategoryName(task.subCategoryId)}
                    onMoveSameWeek={moveSameWeek}
                    onMoveCrossWeek={startCrossWeekMove}
                  />
                )
              )}
            </div>
            {addFor === "unscheduled" && (
              <div style={{ maxWidth: 320, marginTop: 8 }}>
                <AddTaskForm
                  categories={categories}
                  isDsaCategoryId={isDsaCategory}
                  onAdd={(title, categoryId, pattern, difficulty) =>
                    createTask(title, categoryId, pattern, difficulty, "unscheduled")
                  }
                  onCancel={() => setAddFor(null)}
                />
              </div>
            )}
          </div>
        </div>
      ) : (
        <div className="card">
          {DAY_LABELS.map((label, i) => {
            const date = addDays(weekStart, i);
            const dayTasks = buckets.byDay[i];
            return (
              <div key={ymd(date)} className="list-day">
                <h4>
                  {label} · {longDate(date)}{" "}
                  {ymd(date) === todayYmd && <span className="status-pill">Today</span>}
                </h4>
                {dayTasks.length === 0 ? (
                  <p className="muted" style={{ fontSize: 13 }}>No tasks</p>
                ) : (
                  dayTasks.map((task) => (
                    <div className="task-row" key={task.id}>
                      <div className="row" style={{ gap: 8, alignItems: "center" }}>
                        {selectMode && (
                          <input
                            type="checkbox"
                            className="select-box"
                            checked={selectedIds.has(task.id)}
                            onChange={() => toggleSelect(task.id)}
                          />
                        )}
                        <div>
                          <Link className="t-title" href={`/tasks/${task.id}`}>
                            {task.title}
                          </Link>
                          {subCategoryName(task.subCategoryId) && (
                            <div className="t-subcat">{subCategoryName(task.subCategoryId)}</div>
                          )}
                          <div className="muted">
                            {task.status} · {taskProgress(task)}%
                          </div>
                        </div>
                      </div>
                      <select
                        value=""
                        aria-label="Move task"
                        style={{ width: "auto" }}
                        onChange={(event) => {
                          const v = event.target.value;
                          event.target.value = "";
                          if (!v) return;
                          if (v === "prev") startCrossWeekMove(task, -1);
                          else if (v === "next") startCrossWeekMove(task, 1);
                          else moveSameWeek(task.id, v);
                        }}
                      >
                        <option value="">Move…</option>
                        <optgroup label="This week">
                          {DAY_LABELS.map((dl, di) => {
                            const dd = ymd(addDays(weekStart, di));
                            return (
                              <option key={dd} value={dd}>
                                {dl}
                              </option>
                            );
                          })}
                        </optgroup>
                        <optgroup label="Other week">
                          <option value="prev">← Prev week</option>
                          <option value="next">→ Next week</option>
                        </optgroup>
                      </select>
                    </div>
                  ))
                )}
              </div>
            );
          })}
          {buckets.unscheduled.length > 0 && (
            <div className="list-day">
              <div className="day-col-head">
                <h4 style={{ margin: 0 }}>Unscheduled (this week)</h4>
              </div>
              {buckets.unscheduled.map((task) => (
                <div className="task-row" key={task.id}>
                  <div className="row" style={{ gap: 8, alignItems: "center" }}>
                    {selectMode && (
                      <input
                        type="checkbox"
                        className="select-box"
                        checked={selectedIds.has(task.id)}
                        onChange={() => toggleSelect(task.id)}
                      />
                    )}
                    <div>
                      <Link className="t-title" href={`/tasks/${task.id}`}>
                        {task.title}
                      </Link>
                      {subCategoryName(task.subCategoryId) && (
                        <div className="t-subcat">{subCategoryName(task.subCategoryId)}</div>
                      )}
                      <div className="muted">
                        {task.status} · {taskProgress(task)}%
                      </div>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {pendingMove && (
        <div className="modal-overlay" onClick={() => setPendingMove(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <h3 style={{ margin: 0 }}>Move to another week</h3>
            <p className="muted" style={{ margin: 0 }}>
              “{pendingMove.taskTitle}” → week of {pendingMove.toWeekStart} (
              {longDate(parseYmd(pendingMove.toPlannedDate))}). Moving across weeks is recorded as a spillover — pick a
              reason.
            </p>
            <div>
              <label>Reason</label>
              <select value={moveReason} onChange={(e) => setMoveReason(e.target.value)}>
                {SPILLOVER_REASONS.map((r) => (
                  <option key={r} value={r}>
                    {r}
                  </option>
                ))}
              </select>
            </div>
            <div className="row" style={{ display: "flex", gap: 8 }}>
              <button onClick={confirmCrossWeekMove}>Confirm move</button>
              <button className="secondary" onClick={() => setPendingMove(null)}>
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
