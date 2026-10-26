"use client";

import Link from "next/link";
import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { categoryPath, loadTaskSearch, type SearchData } from "./search-data";
import styles from "./search.module.css";

export default function TaskSearchPage() {
  const [query, setQuery] = useState("");
  const [result, setResult] = useState<(SearchData & { query: string }) | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const activeRequest = useRef<AbortController | null>(null);
  const input = useRef<HTMLInputElement>(null);
  const categories = useMemo(
    () => new Map(result?.categories.map(category => [category.id, category]) ?? []),
    [result]
  );

  useEffect(() => () => activeRequest.current?.abort(), []);

  function reset(nextQuery: string) {
    activeRequest.current?.abort();
    activeRequest.current = null;
    setQuery(nextQuery);
    setResult(null);
    setError(null);
    setLoading(false);
  }

  async function search(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    activeRequest.current?.abort();
    const text = query.trim();
    setResult(null);
    setError(null);
    if (!text) {
      setLoading(false);
      return;
    }
    const request = new AbortController();
    activeRequest.current = request;
    setLoading(true);
    try {
      const data = await loadTaskSearch(text, request.signal);
      if (!request.signal.aborted) setResult({ ...data, query: text });
    } catch (failure) {
      if (!request.signal.aborted) {
        setError(failure instanceof Error ? failure.message : "Unable to search tasks. Please try again.");
      }
    } finally {
      if (!request.signal.aborted) setLoading(false);
    }
  }

  const status = loading ? "Searching all weeks…"
    : error ? ""
    : result ? (result.tasks.length
      ? `${result.tasks.length} task${result.tasks.length === 1 ? "" : "s"} found for “${result.query}”.`
      : `No tasks found for “${result.query}”. Try another task, category, or subcategory name.`)
    : "Enter a task, category, or subcategory name to search.";

  return (
    <section className={`page ${styles.page}`}>
      <div className="card">
        <h2>Search Tasks</h2>
        <p id="task-search-help" className="muted">
          Search all weeks and statuses by task name, category, or subcategory.
          Matches contain your text, ignoring case and surrounding spaces.
          Categories include their subcategories.
        </p>
        <form role="search" onSubmit={search}>
          <label htmlFor="task-search">Task, category, or subcategory name</label>
          <div className={styles.controls}>
            <input
              id="task-search"
              ref={input}
              type="search"
              value={query}
              aria-describedby="task-search-help"
              placeholder="e.g., cakes, two sum, three sum"
              autoComplete="off"
              onChange={event => reset(event.target.value)}
            />
            <button type="submit" disabled={!query.trim()}>Search</button>
            <button type="button" className="secondary" onClick={() => {
              reset("");
              input.current?.focus();
            }}>Clear</button>
          </div>
        </form>
        <p><Link href="/tasks">Back to Tasks by Week</Link></p>
      </div>
      <div className="card">
        <p role="status" aria-live="polite" aria-atomic="true">{status}</p>
        {error && <p role="alert" className={styles.error}>{error}</p>}
        {result && result.tasks.length > 0 && (
          <ul className={styles.results} aria-label="Matching tasks">
            {result.tasks.map(task => (
              <li key={task.id} className={styles.result}>
                <Link className={styles.title} href={`/tasks/${task.id}`}>{task.title}</Link>
                <p className="muted">{categoryPath(task, categories)}</p>
                <div className={styles.meta}>
                  <span className={`chip ${task.status.toLowerCase()}`}>{task.status}</span>
                  <span>{task.plannedDate ? `Planned date: ${task.plannedDate}` : "No planned day"}</span>
                  <Link href={`/tasks?weekStart=${encodeURIComponent(task.plannedWeekStart)}`}>
                    Week of {task.plannedWeekStart}
                  </Link>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
