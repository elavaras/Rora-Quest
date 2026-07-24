"use client";

import { useEffect, useState } from "react";
import { AuthMe } from "../lib/user-session";

const API_BASE = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

export default function UserSwitcher() {
  const [me, setMe] = useState<AuthMe | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      try {
        const response = await fetch(`${API_BASE}/api/auth/me`, {
          credentials: "include",
          cache: "no-store"
        });
        if (!response.ok) {
          if (!cancelled) setMe(null);
          return;
        }
        const data = (await response.json()) as AuthMe;
        if (!cancelled) setMe(data);
      } catch {
        if (!cancelled) setMe(null);
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    void load();
    return () => {
      cancelled = true;
    };
  }, []);

  const signIn = () => {
    const returnUrl = encodeURIComponent(window.location.href);
    window.location.href = `${API_BASE}/api/auth/login?returnUrl=${returnUrl}`;
  };

  const signOut = () => {
    const returnUrl = encodeURIComponent(window.location.origin);
    window.location.href = `${API_BASE}/api/auth/logout?returnUrl=${returnUrl}`;
  };

  return (
    <div className="user-switcher top-right-user">
      <p className="user-switcher-title">User</p>
      {loading ? (
        <p className="user-switcher-current">Checking sign-in…</p>
      ) : me ? (
        <div className="user-switcher-authenticated">
          <div>
            <p className="user-switcher-current">{me.displayName ?? me.email ?? me.userId}</p>
            <p className="muted user-switcher-sub">{me.email ?? me.userId}</p>
          </div>
          <button type="button" className="secondary" onClick={signOut}>
            Sign out
          </button>
        </div>
      ) : (
        <div className="user-switcher-actions">
          <button type="button" onClick={signIn}>
            Sign in with Microsoft
          </button>
        </div>
      )}
    </div>
  );
}
