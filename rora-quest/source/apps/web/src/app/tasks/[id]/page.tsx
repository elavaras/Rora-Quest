"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { getApiAuthHeaders, getApiBaseUrl } from "../../lib/user-session";
const DIFFICULTIES = ["Easy", "Medium", "Hard"] as const;

type SubStep = {
  id: string;
  title: string;
  isDone: boolean;
  orderIndex: number;
  weight: number;
};

type ReferenceLink = { id: string; url: string; label: string | null; sourceType: string | null };
type TaskAsset = {
  id: string;
  assetType: string;
  storagePathOrUrl: string;
  fileName: string;
  contentType: string | null;
  sizeBytes: number | null;
  createdAt: string;
};

type TaskItem = {
  id: string;
  title: string;
  status: "Todo" | "InProgress" | "Done" | "Cancelled" | "Skipped";
  plannedWeekStart: string;
  plannedDate: string | null;
  dueDate: string | null;
  pattern: string | null;
  difficulty: (typeof DIFFICULTIES)[number] | null;
  questionAndReasoning?: string | null;
  logicNotes?: string | null;
  algorithmNotes?: string | null;
  diagramContent?: string | null;
  estimatedHours?: number | null;
  actualHours?: number | null;
  storyPoints?: number | null;
  subSteps: SubStep[];
  links?: ReferenceLink[];
  assets?: TaskAsset[];
};

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
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

function weightedProgress(subSteps: SubStep[], status: string): number {
  if (subSteps && subSteps.length > 0) {
    const totalWeight = subSteps.reduce((sum, s) => sum + (s.weight ?? 0), 0);
    if (totalWeight > 0) {
      const doneWeight = subSteps
        .filter((s) => s.isDone)
        .reduce((sum, s) => sum + (s.weight ?? 0), 0);
      return Math.round((doneWeight / totalWeight) * 100);
    }
    const done = subSteps.filter((s) => s.isDone).length;
    return Math.round((done / subSteps.length) * 100);
  }
  return status === "Done" ? 100 : 0;
}

type Props = { params: { id: string } };

export default function TaskDetailPage({ params }: Props) {
  const { id } = params;
  const router = useRouter();
  const [task, setTask] = useState<TaskItem | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [savingMeta, setSavingMeta] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [pattern, setPattern] = useState("");
  const [difficulty, setDifficulty] = useState("");
  const [questionAndReasoning, setQuestionAndReasoning] = useState("");
  const [logicNotes, setLogicNotes] = useState("");
  const [algorithmNotes, setAlgorithmNotes] = useState("");
  const [diagramContent, setDiagramContent] = useState("");
  const [linkUrl, setLinkUrl] = useState("");
  const [linkLabel, setLinkLabel] = useState("");
  const [addingLink, setAddingLink] = useState(false);
  const [uploadingDiagram, setUploadingDiagram] = useState(false);
  const [estimatedHours, setEstimatedHours] = useState<string>("");
  const [actualHours, setActualHours] = useState<string>("");
  const [storyPoints, setStoryPoints] = useState<string>("");
  const [removingAssetId, setRemovingAssetId] = useState<string | null>(null);
  const [opStatus, setOpStatus] = useState<{ tone: "info" | "success" | "error"; text: string } | null>(
    null
  );

  const setStatus = useCallback(
    (tone: "info" | "success" | "error", text: string, clearAfterMs?: number) => {
      setOpStatus({ tone, text });
      if (clearAfterMs && clearAfterMs > 0) {
        window.setTimeout(() => {
          setOpStatus((current) => (current?.text === text ? null : current));
        }, clearAfterMs);
      }
    },
    []
  );

  const applyTaskToView = useCallback((nextTask: TaskItem) => {
    const ordered = [...(nextTask.subSteps ?? [])].sort((a, b) => a.orderIndex - b.orderIndex);
    setTask({ ...nextTask, subSteps: ordered });
    setPattern(nextTask.pattern ?? "");
    setDifficulty(nextTask.difficulty ?? "");
    setQuestionAndReasoning(nextTask.questionAndReasoning ?? "");
    setLogicNotes(nextTask.logicNotes ?? "");
    setAlgorithmNotes(nextTask.algorithmNotes ?? "");
    setDiagramContent(nextTask.diagramContent ?? "");
    setEstimatedHours(nextTask.estimatedHours != null ? String(nextTask.estimatedHours) : "");
    setActualHours(nextTask.actualHours != null ? String(nextTask.actualHours) : "");
    setStoryPoints(nextTask.storyPoints != null ? String(nextTask.storyPoints) : "");
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const t = await apiCall<TaskItem>(`/api/tasks/${id}`);
      applyTaskToView(t);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load task.");
      setStatus("error", "Failed to load task details.");
    } finally {
      setLoading(false);
    }
  }, [applyTaskToView, id, setStatus]);

  useEffect(() => {
    load();
  }, [load]);

  const toggleSubStep = async (sub: SubStep) => {
    if (!task) return;
    // optimistic update
    const next = task.subSteps.map((s) =>
      s.id === sub.id ? { ...s, isDone: !s.isDone } : s
    );
    setTask({ ...task, subSteps: next });
    try {
      await apiCall(`/api/tasks/${id}/substeps/${sub.id}`, {
        method: "PATCH",
        body: JSON.stringify({ isDone: !sub.isDone })
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to update sub-step.");
      await load();
    }
  };

  const saveMeta = async () => {
    setSavingMeta(true);
    setError(null);
    setStatus("info", "Saving task details...");
    try {
      const updated = await apiCall<TaskItem>(`/api/tasks/${id}`, {
        method: "PATCH",
        body: JSON.stringify({
          pattern: pattern.trim() || null,
          difficulty: difficulty || null,
          questionAndReasoning,
          logicNotes,
          algorithmNotes,
          diagramContent,
          estimatedHours: estimatedHours !== "" ? parseFloat(estimatedHours) : null,
          actualHours: actualHours !== "" ? parseFloat(actualHours) : null,
          storyPoints: storyPoints !== "" ? parseInt(storyPoints, 10) : null
        })
      });
      applyTaskToView(updated);
      setStatus("success", "Task details saved.", 2200);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to save.");
      setStatus("error", "Failed to save task details.");
    } finally {
      setSavingMeta(false);
    }
  };

  const handleDelete = async () => {
    if (!task) return;
    const confirmed = window.confirm(
      `Delete "${task.title}"? This removes the task and all its sub-steps. This cannot be undone.`
    );
    if (!confirmed) return;
    setDeleting(true);
    setError(null);
    setStatus("info", "Deleting task...");
    try {
      await apiCall(`/api/tasks/${id}`, { method: "DELETE" });
      router.push("/tasks");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to delete task.");
      setStatus("error", "Failed to delete task.");
      setDeleting(false);
    }
  };

  const addPracticeLink = async () => {
    if (!linkUrl.trim()) return;
    setAddingLink(true);
    setError(null);
    setStatus("info", "Adding practice link...");
    try {
      const created = await apiCall<ReferenceLink>(`/api/tasks/${id}/links`, {
        method: "POST",
        body: JSON.stringify({
          url: linkUrl.trim(),
          label: linkLabel.trim() || null,
          sourceType: "Practice"
        })
      });
      setTask((current) =>
        current
          ? {
              ...current,
              links: [...(current.links ?? []), created]
            }
          : current
      );
      setLinkUrl("");
      setLinkLabel("");
      setStatus("success", "Practice link added.", 1800);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to add practice link.");
      setStatus("error", "Failed to add practice link.");
    } finally {
      setAddingLink(false);
    }
  };

  const uploadDiagramImage = async (file: File) => {
    setUploadingDiagram(true);
    setError(null);
    setStatus("info", "Uploading diagram image...");
    try {
      const dataUrl = await new Promise<string>((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(String(reader.result ?? ""));
        reader.onerror = () => reject(new Error("Unable to read file."));
        reader.readAsDataURL(file);
      });
      const created = await apiCall<TaskAsset>(`/api/tasks/${id}/assets`, {
        method: "POST",
        body: JSON.stringify({
          assetType: "DiagramImage",
          storagePathOrUrl: dataUrl,
          fileName: file.name,
          contentType: file.type || "image/*",
          sizeBytes: file.size
        })
      });
      setTask((current) =>
        current
          ? {
              ...current,
              assets: [...(current.assets ?? []), created]
            }
          : current
      );
      setStatus("success", "Diagram image uploaded.", 1800);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to upload image.");
      setStatus("error", "Failed to upload diagram image.");
    } finally {
      setUploadingDiagram(false);
    }
  };

  const removeDiagramImage = async (assetId: string) => {
    setRemovingAssetId(assetId);
    setError(null);
    setStatus("info", "Removing diagram image...");
    try {
      await apiCall<void>(`/api/tasks/${id}/assets/${assetId}`, { method: "DELETE" });
      setTask((current) =>
        current
          ? {
              ...current,
              assets: (current.assets ?? []).filter((asset) => asset.id !== assetId)
            }
          : current
      );
      setStatus("success", "Diagram image removed.", 1800);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to remove image.");
      setStatus("error", "Failed to remove diagram image.");
    } finally {
      setRemovingAssetId(null);
    }
  };

  const progress = task ? weightedProgress(task.subSteps, task.status) : 0;
  const diagramImages = (task?.assets ?? []).filter(
    (a) =>
      (a.contentType?.startsWith("image/") ?? false) ||
      a.assetType.toLowerCase().includes("diagram")
  );

  return (
    <section className="page">
      <nav className="detail-ribbon" aria-label="Breadcrumb">
        <Link href="/tasks" className="back-btn">
          ← Back to Tasks
        </Link>
        <span className="crumb-sep">/</span>
        <Link href="/tasks" className="crumb-link">
          Tasks by Week
        </Link>
        <span className="crumb-sep">/</span>
        <span className="crumb-current">Task Details</span>
      </nav>

      {opStatus && <div className={`op-status op-status-${opStatus.tone}`}>{opStatus.text}</div>}
      {loading && <div className="card">Loading task…</div>}
      {error && <div className="card error-text">{error}</div>}

      {task && (
        <>
          <div className="card">
            <div className="detail-title-row">
              <h2>{task.title}</h2>
              <button className="danger" onClick={handleDelete} disabled={deleting}>
                {deleting ? "Deleting…" : "Delete Task"}
              </button>
            </div>
            <div className="t-meta">
              <span className={`chip ${task.status.toLowerCase()}`}>{task.status}</span> ·{" "}
              {progress}%
            </div>
            <div className="progress-bar">
              <div className="progress-fill" style={{ width: `${progress}%` }} />
            </div>
          </div>

          <div className="grid-2">
            <div className="card">
              <h3>Problem Metadata</h3>
              <label className="field-label" htmlFor="pattern-input">
                Pattern
              </label>
              <input
                id="pattern-input"
                placeholder="e.g., Sliding Window"
                value={pattern}
                onChange={(e) => setPattern(e.target.value)}
              />
              <label className="field-label" htmlFor="difficulty-select">
                Difficulty
              </label>
              <select
                id="difficulty-select"
                value={difficulty}
                onChange={(e) => setDifficulty(e.target.value)}
              >
                <option value="">— none —</option>
                {DIFFICULTIES.map((d) => (
                  <option key={d} value={d}>
                    {d}
                  </option>
                ))}
              </select>
              <div className="row" style={{ marginTop: "0.75rem" }}>
                <button disabled={savingMeta} onClick={saveMeta}>
                  {savingMeta ? "Saving…" : "Save"}
                </button>
              </div>
              <h3 style={{ marginTop: "1rem" }}>Question &amp; Reasoning</h3>
              <textarea
                value={questionAndReasoning}
                onChange={(e) => setQuestionAndReasoning(e.target.value)}
                rows={4}
                placeholder="Capture the question intent, edge cases, and your reasoning."
              />
              <h3 style={{ marginTop: "1rem" }}>Logic</h3>
              <textarea
                value={logicNotes}
                onChange={(e) => setLogicNotes(e.target.value)}
                rows={4}
                placeholder="High-level logic and thought process."
              />
              <h3 style={{ marginTop: "1rem" }}>Algorithm</h3>
              <textarea
                value={algorithmNotes}
                onChange={(e) => setAlgorithmNotes(e.target.value)}
                rows={4}
                placeholder="Step-by-step algorithm or pseudocode."
              />
              <h3 style={{ marginTop: "1rem" }}>Diagrams</h3>
              <textarea
                value={diagramContent}
                onChange={(e) => setDiagramContent(e.target.value)}
                rows={4}
                placeholder="ASCII diagrams, flow sketches, or links to diagram references."
              />
              <div className="diagram-upload-box">
                <div className="row" style={{ justifyContent: "space-between", alignItems: "center" }}>
                  <label className="field-label" htmlFor="diagram-file-input" style={{ margin: 0 }}>
                    Upload diagram image
                  </label>
                  <input
                    id="diagram-file-input"
                    type="file"
                    accept="image/*"
                    disabled={uploadingDiagram}
                    onChange={(e) => {
                      const file = e.target.files?.[0];
                      if (file) {
                        void uploadDiagramImage(file);
                      }
                      e.currentTarget.value = "";
                    }}
                  />
                </div>
                {diagramImages.length > 0 ? (
                  <div className="diagram-grid">
                    {diagramImages.map((asset) => (
                      <figure key={asset.id} className="diagram-figure">
                        <img src={asset.storagePathOrUrl} alt={asset.fileName} />
                        <figcaption>
                          <span>{asset.fileName}</span>
                          <button
                            type="button"
                            className="secondary diagram-remove-btn"
                            disabled={removingAssetId === asset.id}
                            onClick={() => void removeDiagramImage(asset.id)}
                          >
                            {removingAssetId === asset.id ? "Removing…" : "Remove"}
                          </button>
                        </figcaption>
                      </figure>
                    ))}
                  </div>
                ) : (
                  <p className="muted" style={{ marginTop: "0.5rem" }}>
                    No diagram images uploaded yet.
                  </p>
                )}
              </div>
            </div>

            <div className="card">
              <h3>Task Info</h3>
              <p>
                <strong>Planned Week:</strong> {task.plannedWeekStart}
              </p>
              <p>
                <strong>Planned Day:</strong> {task.plannedDate ?? "Unscheduled"}
              </p>
              <p>
                <strong>Due:</strong> {task.dueDate ?? "—"}
              </p>
              <h3 style={{ marginTop: "1rem" }}>Practice Links (LeetCode/HackerRank/etc.)</h3>
              {task.links && task.links.length > 0 ? (
                <ul>
                  {task.links.map((l) => (
                    <li key={l.id}>
                      <a href={l.url} target="_blank" rel="noreferrer">
                        {l.label ?? l.url}
                      </a>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="muted">No links yet.</p>
              )}
              <div className="grid-2" style={{ marginTop: "0.75rem" }}>
                <div>
                  <label className="field-label" htmlFor="practice-link-url">
                    Link URL
                  </label>
                  <input
                    id="practice-link-url"
                    placeholder="https://leetcode.com/problems/..."
                    value={linkUrl}
                    onChange={(e) => setLinkUrl(e.target.value)}
                  />
                </div>
                <div>
                  <label className="field-label" htmlFor="practice-link-label">
                    Label (optional)
                  </label>
                  <input
                    id="practice-link-label"
                    placeholder="Two Sum"
                    value={linkLabel}
                    onChange={(e) => setLinkLabel(e.target.value)}
                  />
                </div>
              </div>
              <div className="row" style={{ marginTop: "0.75rem" }}>
                <button disabled={addingLink || !linkUrl.trim()} onClick={addPracticeLink}>
                  {addingLink ? "Adding…" : "Add Practice Link"}
                </button>
              </div>
              <h3 style={{ marginTop: "1rem" }}>Effort Tracking</h3>
              <div className="grid-2" style={{ marginTop: "0.5rem" }}>
                <div>
                  <label className="field-label" htmlFor="estimated-hours">
                    Estimated Hours
                  </label>
                  <input
                    id="estimated-hours"
                    type="number"
                    min="0"
                    step="0.5"
                    placeholder="e.g. 2"
                    value={estimatedHours}
                    onChange={(e) => setEstimatedHours(e.target.value)}
                  />
                </div>
                <div>
                  <label className="field-label" htmlFor="actual-hours">
                    Actual Hours
                  </label>
                  <input
                    id="actual-hours"
                    type="number"
                    min="0"
                    step="0.5"
                    placeholder="e.g. 1.5"
                    value={actualHours}
                    onChange={(e) => setActualHours(e.target.value)}
                  />
                </div>
              </div>
              <div style={{ marginTop: "0.5rem" }}>
                <label className="field-label" htmlFor="story-points">
                  Story Points
                </label>
                <input
                  id="story-points"
                  type="number"
                  min="0"
                  step="1"
                  placeholder="e.g. 3"
                  value={storyPoints}
                  onChange={(e) => setStoryPoints(e.target.value)}
                />
              </div>
              <div className="row" style={{ marginTop: "0.75rem" }}>
                <button disabled={savingMeta} onClick={saveMeta}>
                  {savingMeta ? "Saving…" : "Save Effort"}
                </button>
              </div>
            </div>
          </div>

          <div className="card">
            <h3>
              Sub-steps · {task.subSteps.filter((s) => s.isDone).length}/{task.subSteps.length}
            </h3>
            <ul className="substep-list">
              {task.subSteps.map((s) => (
                <li key={s.id} className="substep-row">
                  <label className="substep-check">
                    <input
                      type="checkbox"
                      checked={s.isDone}
                      onChange={() => toggleSubStep(s)}
                    />
                    <span className={s.isDone ? "substep-done" : ""}>{s.title}</span>
                  </label>
                  <span className="substep-weight">{s.weight} pts</span>
                </li>
              ))}
            </ul>
            {task.subSteps.length === 0 && <p className="muted">No sub-steps.</p>}
          </div>
        </>
      )}
    </section>
  );
}
