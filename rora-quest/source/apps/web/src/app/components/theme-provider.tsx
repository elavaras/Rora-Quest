"use client";

import { createContext, useContext, useEffect, useState } from "react";
import type { ReactNode } from "react";

export type Theme = "system" | "light" | "dark";

type ThemeContextValue = {
  theme: Theme;
  setTheme: (t: Theme) => void;
};

const ThemeContext = createContext<ThemeContextValue>({
  theme: "system",
  setTheme: () => {}
});

export function useTheme(): ThemeContextValue {
  return useContext(ThemeContext);
}

function applyTheme(t: Theme): void {
  const applied =
    t === "system"
      ? window.matchMedia("(prefers-color-scheme: dark)").matches
        ? "dark"
        : "light"
      : t;
  document.documentElement.setAttribute("data-theme", applied);
}

function readStoredTheme(): Theme {
  try {
    const raw = localStorage.getItem("rora-theme");
    if (raw === "light" || raw === "dark" || raw === "system") return raw;
  } catch {
    // localStorage unavailable (e.g. private browsing) — fall back to system
  }
  return "system";
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>("system");

  useEffect(() => {
    const stored = readStoredTheme();
    setThemeState(stored);
    applyTheme(stored);
  }, []);

  // Re-apply when OS preference changes while in "system" mode
  useEffect(() => {
    if (theme !== "system") return;
    const mq = window.matchMedia("(prefers-color-scheme: dark)");
    const handler = () => applyTheme("system");
    mq.addEventListener("change", handler);
    return () => mq.removeEventListener("change", handler);
  }, [theme]);

  function setTheme(t: Theme) {
    try {
      localStorage.setItem("rora-theme", t);
    } catch {
      // ignore — still apply in-memory
    }
    setThemeState(t);
    applyTheme(t);
  }

  return (
    <ThemeContext.Provider value={{ theme, setTheme }}>
      {children}
    </ThemeContext.Provider>
  );
}
