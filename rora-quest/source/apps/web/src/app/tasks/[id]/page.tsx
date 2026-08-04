"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useRef, useState } from "react";
import { getApiAuthHeaders, getApiBaseUrl } from "../../lib/user-session";
const DIFFICULTIES = ["Easy", "Medium", "Hard"] as const;
const TASK_STATUSES = ["Todo", "InProgress", "Done", "Cancelled", "Skipped"] as const;
const DSA_CATEGORY_NAME = "DSA";

type SubStep = {
  id: string;
  title: string;
  isDone: boolean;
  orderIndex: number;
  completedAt: string | null;
  rowVersion: number;
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
  categoryId: string | null;
  subCategoryId: string | null;
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
type Category = { id: string; name: string; parentCategoryId: string | null };

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
type ExpandableDetailField = "questionAndReasoning" | "logicNotes" | "algorithmNotes";

const EXPANDABLE_DETAIL_FIELDS: Record<
  ExpandableDetailField,
  { label: string; placeholder: string }
> = {
  questionAndReasoning: {
    label: "Question & Reasoning",
    placeholder: "Capture the question intent, edge cases, and your reasoning."
  },
  logicNotes: {
    label: "Logic",
    placeholder: "High-level logic and thought process."
  },
  algorithmNotes: {
    label: "Algorithm",
    placeholder: "Step-by-step algorithm or pseudocode."
  }
};

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

export default function TaskDetailPage({ params }: Props) {
  const { id } = params;
  const router = useRouter();
  const [task, setTask] = useState<TaskItem | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [savingMeta, setSavingMeta] = useState(false);
  const [savingStatus, setSavingStatus] = useState(false);
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
  const [statusDraft, setStatusDraft] = useState<TaskItem["status"]>("Todo");
  const [categories, setCategories] = useState<Category[]>([]);
  const [removingAssetId, setRemovingAssetId] = useState<string | null>(null);
  const [subStepTitle, setSubStepTitle] = useState("");
  const [subStepWeight, setSubStepWeight] = useState("1");
  const [addingSubStep, setAddingSubStep] = useState(false);
  const [removingSubStepId, setRemovingSubStepId] = useState<string | null>(null);
  const [togglingSubStepId, setTogglingSubStepId] = useState<string | null>(null);
  const [expandedField, setExpandedField] = useState<ExpandableDetailField | null>(null);
  const workflowMutationRef = useRef(false);
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
    setStatusDraft(nextTask.status);
  }, []);

  const normalizeDecimalInput = (value: string): number | null | undefined => {
    if (value.trim() === "") return null;
    const parsed = Number(value);
    return Number.isFinite(parsed) && parsed >= 0 ? parsed : undefined;
  };

  const normalizeIntegerInput = (value: string): number | null | undefined => {
    if (value.trim() === "") return null;
    const parsed = Number(value);
    return Number.isInteger(parsed) && parsed >= 0 ? parsed : undefined;
  };

  const hasInvalidDetailInputs =
    normalizeDecimalInput(estimatedHours) === undefined ||
    normalizeDecimalInput(actualHours) === undefined ||
    normalizeIntegerInput(storyPoints) === undefined;
  const normalizedSubStepWeight = normalizeIntegerInput(subStepWeight);
  const hasInvalidSubStepWeight = normalizedSubStepWeight === undefined;

  const hasPendingDetailChanges = !!task && (
    (pattern.trim() || null) !== (task.pattern ?? null) ||
    (difficulty || null) !== (task.difficulty ?? null) ||
    questionAndReasoning !== (task.questionAndReasoning ?? "") ||
    logicNotes !== (task.logicNotes ?? "") ||
    algorithmNotes !== (task.algorithmNotes ?? "") ||
    diagramContent !== (task.diagramContent ?? "") ||
    normalizeDecimalInput(estimatedHours) !== (task.estimatedHours ?? null) ||
    normalizeDecimalInput(actualHours) !== (task.actualHours ?? null) ||
    normalizeIntegerInput(storyPoints) !== (task.storyPoints ?? null)
  );

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

  const reconcileWorkflowState = useCallback(async () => {
    const authoritativeTask = await apiCall<TaskItem>(`/api/tasks/${id}`);
    const ordered = [...(authoritativeTask.subSteps ?? [])].sort(
      (a, b) => a.orderIndex - b.orderIndex
    );
    setTask({ ...authoritativeTask, subSteps: ordered });
    setStatusDraft(authoritativeTask.status);
  }, [id]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    apiCall<Category[]>("/api/categories")
      .then(setCategories)
      .catch(() => setCategories([]));
  }, []);

  useEffect(() => {
    if (!expandedField) {
      return;
    }

    const handleEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setExpandedField(null);
      }
    };

    window.addEventListener("keydown", handleEscape);
    return () => window.removeEventListener("keydown", handleEscape);
  }, [expandedField]);

  const toggleSubStep = async (sub: SubStep) => {
    if (!task || workflowMutationRef.current) return;
    workflowMutationRef.current = true;
    setTogglingSubStepId(sub.id);
    setError(null);
    try {
      const isDone = !sub.isDone;
      const next = task.subSteps.map((s) =>
        s.id === sub.id ? { ...s, isDone } : s
      );
      let status = task.status;
      if (isDone && next.every((step) => step.isDone) && (status === "Todo" || status === "InProgress")) {
        status = "Done";
      } else if (!isDone && status === "Done") {
        status = "InProgress";
      }

      setTask({ ...task, status, subSteps: next });
      setStatusDraft(status);
      const updatedSubStep = await apiCall<SubStep>(`/api/tasks/${id}/substeps/${sub.id}`, {
        method: "PATCH",
        body: JSON.stringify({ isDone, ifMatchVersion: sub.rowVersion })
      });
      setTask((current) =>
        current
          ? {
              ...current,
              subSteps: current.subSteps.map((step) =>
                step.id === updatedSubStep.id ? updatedSubStep : step
              )
            }
          : current
      );
      await reconcileWorkflowState();
    } catch (err) {
      const message = err instanceof Error ? err.message : "Failed to update sub-step.";
      try {
        await reconcileWorkflowState();
        setError(message);
      } catch (reconcileError) {
        const reconcileMessage =
          reconcileError instanceof Error ? reconcileError.message : "Failed to reload task.";
        setError(`${message} ${reconcileMessage}`);
      }
      setStatus("error", "Failed to update sub-step.");
    } finally {
      setTogglingSubStepId(null);
      workflowMutationRef.current = false;
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
          estimatedHours: normalizeDecimalInput(estimatedHours),
          actualHours: normalizeDecimalInput(actualHours),
          storyPoints: normalizeIntegerInput(storyPoints)
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

  const updateTaskStatus = async (nextStatus: TaskItem["status"]) => {
    if (!task || nextStatus === task.status) return;
    if (workflowMutationRef.current) return;
    workflowMutationRef.current = true;
    setSavingStatus(true);
    setStatusDraft(nextStatus);
    setError(null);
    setStatus("info", "Updating task status...");
    try {
      const updated = await apiCall<TaskItem>(`/api/tasks/${id}/status`, {
        method: "PATCH",
        body: JSON.stringify({
          status: nextStatus,
          overrideIncompleteSubsteps: false
        })
      });
      applyTaskToView(updated);
      setStatus("success", "Task status updated.", 1800);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to update task status.");
      setStatus("error", "Failed to update task status.");
      setStatusDraft(task.status);
    } finally {
      setSavingStatus(false);
      workflowMutationRef.current = false;
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

  const createSubStep = async () => {
    if (
      !task ||
      isDsaLocked ||
      !subStepTitle.trim() ||
      hasInvalidSubStepWeight ||
      workflowMutationRef.current
    ) return;
    workflowMutationRef.current = true;
    setAddingSubStep(true);
    setError(null);
    setStatus("info", "Creating sub-step...");
    try {
      const created = await apiCall<SubStep>(`/api/tasks/${id}/substeps`, {
        method: "POST",
        body: JSON.stringify({
          title: subStepTitle.trim(),
          weight: normalizedSubStepWeight ?? 0
        })
      });
      setTask((current) =>
        current
          ? {
              ...current,
              subSteps: [...(current.subSteps ?? []), created].sort((a, b) => a.orderIndex - b.orderIndex)
            }
          : current
      );
      setSubStepTitle("");
      setSubStepWeight("1");
      setStatus("success", "Sub-step created.", 1800);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create sub-step.");
      setStatus("error", "Failed to create sub-step.");
    } finally {
      setAddingSubStep(false);
      workflowMutationRef.current = false;
    }
  };

  const removeSubStep = async (subStepId: string) => {
    if (!task || isDsaLocked || workflowMutationRef.current) return;
    workflowMutationRef.current = true;
    setRemovingSubStepId(subStepId);
    setError(null);
    setStatus("info", "Removing sub-step...");
    try {
      await apiCall<void>(`/api/tasks/${id}/substeps/${subStepId}`, { method: "DELETE" });
      setTask((current) =>
        current
          ? {
              ...current,
              subSteps: (current.subSteps ?? []).filter((subStep) => subStep.id !== subStepId)
            }
          : current
      );
      setStatus("success", "Sub-step removed.", 1800);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to remove sub-step.");
      setStatus("error", "Failed to remove sub-step.");
    } finally {
      setRemovingSubStepId(null);
      workflowMutationRef.current = false;
    }
  };

  const progress = task ? weightedProgress(task.subSteps, task.status) : 0;
  const categoryById = new Map(categories.map((category) => [category.id, category] as const));
  const isDsaLocked = task
    ? isDsaCategoryId(task.categoryId, categoryById) || isDsaCategoryId(task.subCategoryId, categoryById)
    : false;
  const diagramImages = (task?.assets ?? []).filter(
    (a) =>
      (a.contentType?.startsWith("image/") ?? false) ||
      a.assetType.toLowerCase().includes("diagram")
  );
  const expandedFieldConfig = expandedField ? EXPANDABLE_DETAIL_FIELDS[expandedField] : null;
  const expandedFieldValue =
    expandedField === "questionAndReasoning"
      ? questionAndReasoning
      : expandedField === "logicNotes"
        ? logicNotes
        : expandedField === "algorithmNotes"
          ? algorithmNotes
          : "";

  const setExpandedFieldValue = (value: string) => {
    if (expandedField === "questionAndReasoning") {
      setQuestionAndReasoning(value);
      return;
    }

    if (expandedField === "logicNotes") {
      setLogicNotes(value);
      return;
    }

    if (expandedField === "algorithmNotes") {
      setAlgorithmNotes(value);
    }
  };

  const renderExpandableTextarea = (
    field: ExpandableDetailField,
    value: string,
    setValue: (nextValue: string) => void
  ) => {
    const config = EXPANDABLE_DETAIL_FIELDS[field];
    return (
      <div className="detail-field-block">
        <div className="detail-field-header">
          <h3>{config.label}</h3>
          <button
            type="button"
            className="secondary detail-expand-btn"
            onClick={() => setExpandedField(field)}
            aria-label={`Maximize ${config.label}`}
          >
            Maximize
          </button>
        </div>
        <textarea value={value} onChange={(e) => setValue(e.target.value)} rows={4} placeholder={config.placeholder} />
      </div>
    );
  };

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
              <div className="row" style={{ gap: "0.5rem" }}>
                <button disabled={savingMeta || hasInvalidDetailInputs || !hasPendingDetailChanges} onClick={saveMeta}>
                  {savingMeta ? "Saving…" : "Save Task Details"}
                </button>
                <button className="danger" onClick={handleDelete} disabled={deleting}>
                  {deleting ? "Deleting…" : "Delete Task"}
                </button>
              </div>
            </div>
            <div className="t-meta">
              <span className={`chip ${task.status.toLowerCase()}`}>{task.status}</span> ·{" "}
              {progress}%
            </div>
            <div className="row" style={{ marginTop: "0.75rem", gap: "0.5rem" }}>
              <select
                aria-label="Task status"
                value={statusDraft}
                onChange={(event) => {
                  const nextStatus = event.target.value as TaskItem["status"];
                  void updateTaskStatus(nextStatus);
                }}
                disabled={
                  isDsaLocked ||
                  savingStatus ||
                  togglingSubStepId !== null ||
                  addingSubStep ||
                  removingSubStepId !== null
                }
              >
                {TASK_STATUSES.map((status) => (
                  <option key={status} value={status}>
                    {status}
                  </option>
                ))}
              </select>
            </div>
            {isDsaLocked && (
              <p className="muted" style={{ marginTop: "0.5rem" }}>
                DSA tasks are system-managed. Manual task status and sub-step structure updates are disabled.
              </p>
            )}
            {!isDsaLocked && (
              <p className="muted" style={{ marginTop: "0.5rem" }}>
                Status changes save automatically.
              </p>
            )}
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
              {renderExpandableTextarea("questionAndReasoning", questionAndReasoning, setQuestionAndReasoning)}
              {renderExpandableTextarea("logicNotes", logicNotes, setLogicNotes)}
              {renderExpandableTextarea("algorithmNotes", algorithmNotes, setAlgorithmNotes)}
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
            </div>
          </div>

          <div className="card">
            <h3>
              Sub-steps · {task.subSteps.filter((s) => s.isDone).length}/{task.subSteps.length}
            </h3>
            <div className="grid-2" style={{ marginTop: "0.75rem" }}>
              <div>
                <label className="field-label" htmlFor="sub-step-title">
                  New sub-step title
                </label>
                <input
                  id="sub-step-title"
                  placeholder="e.g., Draft solution approach"
                  value={subStepTitle}
                  onChange={(e) => setSubStepTitle(e.target.value)}
                  disabled={
                    isDsaLocked ||
                    addingSubStep ||
                    savingStatus ||
                    togglingSubStepId !== null ||
                    removingSubStepId !== null
                  }
                />
              </div>
              <div>
                <label className="field-label" htmlFor="sub-step-weight">
                  Weight
                </label>
                <input
                  id="sub-step-weight"
                  type="number"
                  min="0"
                  step="1"
                  value={subStepWeight}
                  onChange={(e) => setSubStepWeight(e.target.value)}
                  disabled={
                    isDsaLocked ||
                    addingSubStep ||
                    savingStatus ||
                    togglingSubStepId !== null ||
                    removingSubStepId !== null
                  }
                />
              </div>
            </div>
            <div className="row" style={{ marginTop: "0.75rem" }}>
              <button
                disabled={
                  isDsaLocked ||
                  addingSubStep ||
                  savingStatus ||
                  togglingSubStepId !== null ||
                  removingSubStepId !== null ||
                  !subStepTitle.trim() ||
                  hasInvalidSubStepWeight
                }
                onClick={createSubStep}
              >
                {addingSubStep ? "Creating…" : "Create Sub-step"}
              </button>
            </div>
            {isDsaLocked && (
              <p className="muted" style={{ marginTop: "0.5rem" }}>
                Manual sub-step creation/removal is disabled for DSA tasks.
              </p>
            )}
            <ul className="substep-list">
              {task.subSteps.map((s) => (
                <li key={s.id} className="substep-row">
                  <label className="substep-check">
                    <input
                      type="checkbox"
                      checked={s.isDone}
                      disabled={
                        savingStatus ||
                        togglingSubStepId !== null ||
                        addingSubStep ||
                        removingSubStepId !== null
                      }
                      onChange={() => toggleSubStep(s)}
                    />
                    <span className={s.isDone ? "substep-done" : ""}>{s.title}</span>
                  </label>
                  <div className="row" style={{ gap: "0.5rem", alignItems: "center" }}>
                    <span className="substep-weight">{s.weight} pts</span>
                    {!isDsaLocked && (
                      <button
                        type="button"
                        className="secondary"
                        disabled={
                          savingStatus ||
                          togglingSubStepId !== null ||
                          addingSubStep ||
                          removingSubStepId !== null
                        }
                        onClick={() => void removeSubStep(s.id)}
                      >
                        {removingSubStepId === s.id ? "Removing…" : "Remove"}
                      </button>
                    )}
                  </div>
                </li>
              ))}
            </ul>
            {task.subSteps.length === 0 && <p className="muted">No sub-steps.</p>}
          </div>
        </>
      )}

      {expandedFieldConfig && (
        <div className="modal-overlay" onClick={() => setExpandedField(null)}>
          <div
            className="modal detail-expand-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="expanded-detail-field-title"
            onClick={(event) => event.stopPropagation()}
          >
            <div className="detail-field-header detail-expand-modal-header">
              <div>
                <h3 id="expanded-detail-field-title">{expandedFieldConfig.label}</h3>
                <p className="muted detail-expand-modal-note">Expanded editor · press Esc to close</p>
              </div>
              <button type="button" className="secondary detail-expand-btn" onClick={() => setExpandedField(null)}>
                Done
              </button>
            </div>
            <textarea
              className="detail-expand-textarea"
              value={expandedFieldValue}
              onChange={(event) => setExpandedFieldValue(event.target.value)}
              rows={18}
              placeholder={expandedFieldConfig.placeholder}
              autoFocus
            />
          </div>
        </div>
      )}
    </section>
  );
}
