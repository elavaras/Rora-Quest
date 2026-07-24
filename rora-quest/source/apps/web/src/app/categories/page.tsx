 "use client";

import { FormEvent, useEffect, useMemo, useState } from "react";
import { getApiAuthHeaders } from "../lib/user-session";

type Category = {
  id: string;
  userId: string;
  name: string;
  parentCategoryId: string | null;
  createdAt: string;
};

type CategoryRequest = {
  name: string;
  parentCategoryId: string | null;
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

export default function CategoriesPage() {
  const [categories, setCategories] = useState<Category[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [name, setName] = useState("");
  const [parentCategoryId, setParentCategoryId] = useState("");
  const [editingCategoryId, setEditingCategoryId] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const topLevelCategories = useMemo(
    () => categories.filter((item) => item.parentCategoryId === null),
    [categories]
  );

  const subCategoriesByParent = useMemo(() => {
    const map = new Map<string, Category[]>();
    categories
      .filter((item) => item.parentCategoryId !== null)
      .forEach((item) => {
        const key = item.parentCategoryId as string;
        const existing = map.get(key) ?? [];
        existing.push(item);
        map.set(key, existing.sort((a, b) => a.name.localeCompare(b.name)));
      });
    return map;
  }, [categories]);

  async function loadCategories() {
    try {
      setLoading(true);
      const data = await apiCall<Category[]>("/api/categories");
      setCategories(data.sort((a, b) => a.name.localeCompare(b.name)));
      setError(null);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : "Failed to load categories.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadCategories();
  }, []);

  function resetForm() {
    setEditingCategoryId(null);
    setName("");
    setParentCategoryId("");
  }

  async function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    setMessage(null);

    const payload: CategoryRequest = {
      name: name.trim(),
      parentCategoryId: parentCategoryId || null
    };

    try {
      if (editingCategoryId) {
        await apiCall<Category>(`/api/categories/${editingCategoryId}`, {
          method: "PATCH",
          body: JSON.stringify(payload)
        });
        setMessage("Category updated.");
      } else {
        await apiCall<Category>("/api/categories", {
          method: "POST",
          body: JSON.stringify(payload)
        });
        setMessage("Category created.");
      }

      resetForm();
      await loadCategories();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Failed to save category.");
    } finally {
      setSaving(false);
    }
  }

  function handleEdit(category: Category) {
    setEditingCategoryId(category.id);
    setName(category.name);
    setParentCategoryId(category.parentCategoryId ?? "");
    setMessage(null);
    setError(null);
  }

  async function handleDelete(category: Category) {
    setError(null);
    setMessage(null);

    if (subCategoriesByParent.has(category.id)) {
      setError("Delete sub-categories first, then delete the parent category.");
      return;
    }

    try {
      await apiCall<void>(`/api/categories/${category.id}`, { method: "DELETE" });
      if (editingCategoryId === category.id) {
        resetForm();
      }
      setMessage("Category deleted.");
      await loadCategories();
    } catch (deleteError) {
      setError(deleteError instanceof Error ? deleteError.message : "Failed to delete category.");
    }
  }

  return (
    <section className="page">
      <div className="card">
        <h2>Categories</h2>
        <p className="muted">
          Create and save both categories and sub-categories in one place using parent-child mapping.
        </p>
        <p className="muted">Database-backed via API endpoints.</p>
      </div>

      <div className="grid-2">
        <div className="card">
          <h3>{editingCategoryId ? "Edit Category" : "Create Category / Sub-category"}</h3>
          <form onSubmit={(event) => void handleSave(event)}>
            <label>Category name</label>
            <input
              required
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="e.g., DSA or Sliding Window"
            />
            <br />
            <br />
            <label>Parent category (optional)</label>
            <select
              value={parentCategoryId}
              onChange={(event) => setParentCategoryId(event.target.value)}
            >
              <option value="">No parent (top-level category)</option>
              {topLevelCategories
                .filter((item) => item.id !== editingCategoryId)
                .map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name}
                  </option>
                ))}
            </select>
            <br />
            <br />
            <div className="inline-actions">
              <button disabled={saving}>{saving ? "Saving..." : "Save Category"}</button>
              {editingCategoryId ? (
                <button type="button" className="secondary" onClick={resetForm}>
                  Cancel Edit
                </button>
              ) : null}
            </div>
          </form>
          {message ? <p className="success-text">{message}</p> : null}
          {error ? <p className="error-text">{error}</p> : null}
        </div>

        <div className="card">
          <h3>Category Tree</h3>
          {loading ? <p className="muted">Loading categories...</p> : null}
          {!loading && topLevelCategories.length === 0 ? (
            <p className="muted">No categories yet. Create one to get started.</p>
          ) : null}
          {!loading &&
            topLevelCategories.map((category) => (
              <div key={category.id} className="task-row">
                <div>
                  <strong>{category.name}</strong>
                  <div className="muted">
                    {(subCategoriesByParent.get(category.id) ?? []).map((item) => item.name).join(", ") ||
                      "No sub-categories"}
                  </div>
                </div>
                <div className="inline-actions">
                  <button type="button" className="secondary" onClick={() => handleEdit(category)}>
                    Edit
                  </button>
                  <button type="button" className="secondary" onClick={() => void handleDelete(category)}>
                    Delete
                  </button>
                </div>
              </div>
            ))}
        </div>
      </div>
    </section>
  );
}
