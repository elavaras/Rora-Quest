"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { getApiAuthHeaders, getApiBaseUrl } from "../lib/user-session";
import { appendPage, validateReport, type EventPage, type ProgressReport } from "./contracts";
import { queryString, type Query } from "./dates";

async function json(path: string, signal: AbortSignal): Promise<unknown> {
  const response = await fetch(`${getApiBaseUrl()}${path}`, {
    signal, headers: getApiAuthHeaders(), credentials: "include", cache: "no-store"
  });
  if (!response.ok) {
    // Do not render arbitrary server HTML, database details, or an empty success.
    throw new Error(response.status === 401 || response.status === 403
      ? "Progress access is unavailable. Check your account and retry."
      : `Progress request failed (${response.status}). Retry when records are available.`);
  }
  return response.json();
}

// Read the existing identity endpoint; no auth policy, storage, or account-menu changes.
// Check on both sides of a read so a cookie/session change cannot publish an old owner's report.
async function owner(signal: AbortSignal): Promise<string> {
  const response = await fetch(`${getApiBaseUrl()}/api/auth/me`, { signal, credentials: "include", cache: "no-store" });
  if (response.status === 401) return "existing-anonymous-context";
  if (!response.ok) throw new Error("Account context could not be checked. Retry Progress.");
  const value: unknown = await response.json();
  if (!value || typeof value !== "object" || !("userId" in value) || typeof value.userId !== "string" || !value.userId) {
    throw new Error("Account context is unavailable. Retry Progress.");
  }
  return `user:${value.userId}`;
}

export function useProgress(query: Query, enabled: boolean) {
  const [report, setReport] = useState<ProgressReport | null>(null);
  const [page, setPage] = useState<EventPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pageError, setPageError] = useState<string | null>(null);
  const [paging, setPaging] = useState(false);
  const [revision, setRevision] = useState(0);
  const generation = useRef(0);
  const context = useRef<{ key: string; owner: string } | null>(null);
  const controller = useRef<AbortController | null>(null);
  const pageController = useRef<AbortController | null>(null);
  const pagingLock = useRef(false);
  const key = queryString(query);

  const invalidate = useCallback(() => {
    ++generation.current;
    controller.current?.abort();
    pageController.current?.abort();
    context.current = null;
    pagingLock.current = false;
    setReport(null); setPage(null); setError(null); setPageError(null); setPaging(false);
  }, []);
  const refresh = useCallback(() => { invalidate(); setRevision(v => v + 1); }, [invalidate]);

  useEffect(() => {
    invalidate();
    if (!enabled) return;
    const request = ++generation.current;
    const abort = new AbortController();
    controller.current = abort;
    const current = () => !abort.signal.aborted && generation.current === request;
    void (async () => {
      try {
        const identity = await owner(abort.signal);
        if (!current()) return;
        context.current = { key, owner: identity };
        const value = await json(`/api/progress${key ? `?${key}` : ""}`, abort.signal);
        if (!current()) return;
        const data = validateReport(value, query);
        if (await owner(abort.signal) !== identity) throw new Error("Account changed while loading. Retry for the current account.");
        if (!current() || context.current?.owner !== identity || context.current.key !== key) return;
        setReport(data); setPage(data.selectedDay);
      } catch (err) {
        if (current()) {
          context.current = null;
          setError(err instanceof Error ? err.message : "Progress records are unavailable. Retry.");
        }
      }
    })();
    return () => { abort.abort(); pageController.current?.abort(); context.current = null; };
    // key includes every API query field; query objects need not have stable identity.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, revision, enabled, invalidate]);

  const loadMore = async () => {
    if (!page?.nextCursor || !context.current || pagingLock.current) return;
    pagingLock.current = true; setPaging(true); setPageError(null);
    const request = generation.current, expected = context.current, previous = page;
    const abort = new AbortController();
    pageController.current = abort;
    const current = () => !abort.signal.aborted && request === generation.current && context.current === expected;
    try {
      if (await owner(abort.signal) !== expected.owner) {
        if (current()) { invalidate(); setError("Account changed. Retry for the current account."); }
        return;
      }
      const value = await json(`/api/progress/events?${new URLSearchParams({ cursor: previous.nextCursor! })}`, abort.signal);
      if (!current()) return;
      const next = appendPage(previous, value);
      if (await owner(abort.signal) !== expected.owner) {
        if (current()) { invalidate(); setError("Account changed. Retry for the current account."); }
        return;
      }
      if (current()) setPage(next);
    } catch (err) {
      if (current()) setPageError(err instanceof Error ? err.message : "More records could not be loaded. Retry.");
    } finally {
      if (current()) { pagingLock.current = false; setPaging(false); }
    }
  };
  return { report, page, error, pageError, paging, loadMore, invalidate, refresh };
}
