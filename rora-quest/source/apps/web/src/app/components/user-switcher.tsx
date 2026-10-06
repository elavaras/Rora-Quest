"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { AuthMe, getApiBaseUrl } from "../lib/user-session";

export default function UserSwitcher() {
  const [me, setMe] = useState<AuthMe | null>(null);
  const [loading, setLoading] = useState(true);
  const [isOpen, setIsOpen] = useState(false);
  const accountRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const initialFocus = useRef<"first" | "last">("first");
  const menuId = useId();
  const pathname = usePathname();

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      try {
        const apiBase = getApiBaseUrl();
        const response = await fetch(`${apiBase}/api/auth/me`, {
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

  useEffect(() => {
    setIsOpen(false);
  }, [pathname]);

  useEffect(() => {
    if (!isOpen) return;

    const items = menuRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]');
    const index = initialFocus.current === "last" ? (items?.length ?? 1) - 1 : 0;
    items?.[index]?.focus();

    const dismissOutside = (event: PointerEvent) => {
      if (event.target instanceof Node && !accountRef.current?.contains(event.target)) {
        setIsOpen(false);
      }
    };
    document.addEventListener("pointerdown", dismissOutside);
    return () => document.removeEventListener("pointerdown", dismissOutside);
  }, [isOpen]);

  const handleTriggerKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
    event.preventDefault();
    initialFocus.current = event.key === "ArrowUp" ? "last" : "first";
    if (isOpen) {
      const items = menuRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]');
      items?.[initialFocus.current === "last" ? items.length - 1 : 0]?.focus();
    } else {
      setIsOpen(true);
    }
  };

  const handleMenuKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const items = Array.from(menuRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? []);
    const currentIndex = items.findIndex((item) => item === document.activeElement);
    let nextIndex: number;
    switch (event.key) {
      case "Tab":
        setIsOpen(false);
        triggerRef.current?.focus();
        return;
      case "ArrowDown":
        nextIndex = (currentIndex + 1) % items.length;
        break;
      case "ArrowUp":
        nextIndex = (currentIndex - 1 + items.length) % items.length;
        break;
      case "Home":
        nextIndex = 0;
        break;
      case "End":
        nextIndex = items.length - 1;
        break;
      default:
        return;
    }
    event.preventDefault();
    items[nextIndex]?.focus();
  };

  const signIn = () => {
    const apiBase = getApiBaseUrl();
    const returnUrl = encodeURIComponent(window.location.href);
    window.location.href = `${apiBase}/api/auth/login?returnUrl=${returnUrl}`;
  };

  const signOut = () => {
    const apiBase = getApiBaseUrl();
    const returnUrl = encodeURIComponent(window.location.origin);
    window.location.href = `${apiBase}/api/auth/logout?returnUrl=${returnUrl}`;
  };

  const accountName = me?.displayName?.trim() || me?.email?.trim() || me?.userId || "Account";
  const accountDetail = me?.email?.trim() || me?.userId;
  const nameParts = accountName.split(/\s+/);
  const initials = nameParts.length > 1
    ? `${Array.from(nameParts[0])[0]}${Array.from(nameParts[nameParts.length - 1])[0]}`.toUpperCase()
    : Array.from(accountName).slice(0, 2).join("").toUpperCase();

  return (
    <div
      className="account-navigation"
      ref={accountRef}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) setIsOpen(false);
      }}
      onKeyDown={(event) => {
        if (isOpen && event.key === "Escape") {
          event.preventDefault();
          event.stopPropagation();
          setIsOpen(false);
          triggerRef.current?.focus();
        }
      }}
    >
      {loading ? (
        <span className="account-status muted" role="status">Checking sign-in...</span>
      ) : me ? (
        <>
          <button
            type="button"
            className="account-menu-trigger"
            ref={triggerRef}
            aria-label={`Account menu for ${accountName}`}
            aria-haspopup="menu"
            aria-expanded={isOpen}
            aria-controls={isOpen ? menuId : undefined}
            title={accountName}
            onClick={() => {
              initialFocus.current = "first";
              setIsOpen((open) => !open);
            }}
            onKeyDown={handleTriggerKeyDown}
          >
            <span className="account-avatar" aria-hidden="true">{initials}</span>
            <svg className="account-chevron" width="14" height="14" viewBox="0 0 16 16" fill="none" aria-hidden="true">
              <path d="m4 6 4 4 4-4" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </button>
          {isOpen && (
            <div className="account-menu-panel">
              <div className="account-menu-identity">
                <p className="account-menu-name">{accountName}</p>
                {accountDetail && accountDetail !== accountName && (
                  <p className="account-menu-detail muted">{accountDetail}</p>
                )}
              </div>
              <div id={menuId} role="menu" aria-label="Account" ref={menuRef} onKeyDown={handleMenuKeyDown}>
                <Link
                  href="/settings"
                  role="menuitem"
                  tabIndex={-1}
                  className="account-menu-item"
                  onClick={() => {
                    setIsOpen(false);
                    triggerRef.current?.focus();
                  }}
                >
                  Settings
                </Link>
                <button type="button" role="menuitem" tabIndex={-1} className="account-menu-item" onClick={signOut}>
                  Sign out
                </button>
              </div>
            </div>
          )}
        </>
      ) : (
        <button type="button" className="account-sign-in" onClick={signIn}>
          Sign in
        </button>
      )}
    </div>
  );
}
