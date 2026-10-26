import { getApiAuthHeaders, getApiBaseUrl } from "../../lib/user-session";

export type SearchTask = {
  id: string;
  title: string;
  status: "Todo" | "InProgress" | "Done" | "Cancelled" | "Skipped";
  categoryId: string | null;
  subCategoryId: string | null;
  plannedWeekStart: string;
  plannedDate: string | null;
};

export type SearchCategory = { id: string; name: string; parentCategoryId: string | null };
export type SearchData = { tasks: SearchTask[]; categories: SearchCategory[] };

export function taskSearchPath(query: string): string | null {
  const search = query.trim();
  return search ? `/api/tasks?${new URLSearchParams({ search })}` : null;
}

async function readList<T>(path: string, signal: AbortSignal): Promise<T[]> {
  const response = await fetch(`${getApiBaseUrl()}${path}`, {
    headers: getApiAuthHeaders(),
    credentials: "include",
    cache: "no-store",
    signal
  });
  if (!response.ok) {
    throw new Error(`Unable to load search results (status ${response.status}). Please try again.`);
  }
  const data: unknown = await response.json();
  if (!Array.isArray(data)) throw new Error("Invalid search response. Please try again.");
  return data as T[];
}

export async function loadTaskSearch(query: string, signal: AbortSignal): Promise<SearchData> {
  const path = taskSearchPath(query);
  if (!path) return { tasks: [], categories: [] };
  const [tasks, categories] = await Promise.all([
    readList<SearchTask>(path, signal),
    readList<SearchCategory>("/api/categories", signal)
  ]);
  return { tasks, categories };
}

export function categoryPath(
  task: Pick<SearchTask, "categoryId" | "subCategoryId">,
  categories: ReadonlyMap<string, SearchCategory>
): string {
  const path: SearchCategory[] = [];
  const included = new Set<string>();
  for (const assignedId of [task.categoryId, task.subCategoryId]) {
    const ancestors: SearchCategory[] = [];
    const visited = new Set<string>();
    let id = assignedId;
    while (id && !visited.has(id)) {
      visited.add(id);
      const category = categories.get(id);
      if (!category) break;
      ancestors.unshift(category);
      id = category.parentCategoryId;
    }
    for (const category of ancestors) {
      if (!included.has(category.id)) {
        path.push(category);
        included.add(category.id);
      }
    }
  }
  return path.map(c => c.name).join(" / ")
    || (task.categoryId || task.subCategoryId ? "Unknown category" : "No category");
}
