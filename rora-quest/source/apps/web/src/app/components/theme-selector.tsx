"use client";

import { useTheme } from "./theme-provider";
import type { Theme } from "./theme-provider";

export function ThemeSelector() {
  const { theme, setTheme } = useTheme();

  return (
    <div>
      <label className="field-label" htmlFor="theme-select">
        App theme
      </label>
      <select
        id="theme-select"
        value={theme}
        onChange={(e) => setTheme(e.target.value as Theme)}
        style={{ width: "auto" }}
      >
        <option value="system">System (follow OS)</option>
        <option value="light">Light</option>
        <option value="dark">Dark</option>
      </select>
    </div>
  );
}
