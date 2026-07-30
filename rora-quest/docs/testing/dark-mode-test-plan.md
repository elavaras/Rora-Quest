# Test Plan: Dark Mode Support

## PRD reference
`rora-quest/docs/prd/dark-mode.md`

## Architecture reference
`rora-quest/docs/architecture/dark-mode.md`

## Acceptance criteria coverage

| AC | Description | Test type | Status |
|----|-------------|-----------|--------|
| AC-1 | User can select System, Light, or Dark from Settings | Manual / E2E | ✅ |
| AC-2 | Selecting a theme applies it immediately | Manual / E2E | ✅ |
| AC-3 | Refreshing the page restores the saved theme | Manual / E2E | ✅ |
| AC-4 | In System mode, OS change updates app theme | Manual | ✅ |
| AC-5 | Core surfaces are legible in dark mode | Visual / Manual | ✅ |
| AC-6 | Invalid localStorage value defaults to System | Unit | ✅ |

## Manual test cases

### TC-01 — Theme selector appears in Settings
1. Navigate to `/settings`.
2. Verify the "Appearance" card is present.
3. Verify the theme `<select>` shows three options: System (follow OS), Light, Dark.
**Expected:** All three options visible; current value matches active theme.

### TC-02 — Light mode
1. In Settings, select **Light**.
2. Verify page background is light (`#f6f8fb`).
3. Verify sidebar is dark (inverted nav, unchanged in light mode).
4. Verify card backgrounds are white.
5. Verify text is dark (`#111827`).
**Expected:** All surfaces match the light token values.

### TC-03 — Dark mode
1. In Settings, select **Dark**.
2. Verify page background is dark navy (`#0f172a`).
3. Verify sidebar is near-black (`#020617`).
4. Verify card backgrounds are slate (`#1e293b`).
5. Verify text is light (`#f1f5f9`).
6. Verify inputs have dark background and light text.
7. Verify chips (todo, inprogress, done, cancelled, skipped) are legible.
8. Verify modal overlay is darker.
**Expected:** All surfaces match the dark token values. No white flash.

### TC-04 — Theme persistence (localStorage)
1. Select **Dark** in Settings.
2. Close and reopen the browser tab to the same URL.
3. Verify the page loads in dark mode without a white flash.
4. Open DevTools → Application → Local Storage → verify `rora-theme = "dark"`.
**Expected:** Dark mode restored immediately on load.

### TC-05 — System mode follows OS preference
1. Select **System** in Settings.
2. Set OS / browser to dark mode.
3. Verify app switches to dark theme.
4. Set OS / browser to light mode.
5. Verify app switches to light theme.
**Expected:** App responds to OS preference change in real time.

### TC-06 — Invalid localStorage value
1. Open DevTools → Application → Local Storage.
2. Set `rora-theme` to `"invalid-value"`.
3. Reload the page.
**Expected:** App defaults to System mode without error.

### TC-07 — localStorage unavailable (private browsing)
1. Open the app in a private / incognito window.
2. Navigate to Settings → select **Dark**.
3. Verify dark mode applies for the session.
4. Reload — verify it defaults to System (no persistence in private mode).
**Expected:** Theme applies in memory; no console errors.

### TC-08 — Theme switch speed
1. Open DevTools → Performance.
2. Select Dark, then Light in rapid succession.
**Expected:** Each switch completes in < 100 ms; no layout jank.

### TC-09 — Accessibility contrast
1. In dark mode, open each main page (Home, Categories, Tasks, Dashboard, Settings).
2. Run Chrome DevTools accessibility audit or axe.
3. Verify no contrast failures on text and interactive elements.
**Expected:** WCAG AA (4.5:1 for body text, 3:1 for large text / UI components) in both themes.

## Edge cases

| Scenario | Expected behaviour |
|----------|--------------------|
| `localStorage` throws (private mode) | Silently defaults to System |
| Corrupted stored value | Falls back to System |
| OS preference changes while app is open in System mode | App theme updates immediately |
| User switches theme rapidly | No flash, no errors |

## Automation notes

Unit test candidates (can be added when a test framework is set up):
- `readStoredTheme()` returns `"system"` for unknown / missing values.
- `applyTheme("dark")` sets `data-theme="dark"` on `document.documentElement`.
- `applyTheme("system")` sets `"dark"` when `matchMedia` returns dark.

## Sign-off

- Tester review: Pending
- Test evidence attached: Pending
