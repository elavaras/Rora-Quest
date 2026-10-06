// Run against a production build using the optional Playwright tooling documented
// in docs/task-week-navigation.md. All API requests and auth redirects are mocked.
// USER_NAV_TEST_ORIGIN and USER_NAV_TEST_BROWSER override the local server/browser.
const assert = require("node:assert/strict");
const { mkdirSync } = require("node:fs");
const path = require("node:path");
const { chromium } = require("playwright-core");

const origin = process.env.USER_NAV_TEST_ORIGIN || "http://127.0.0.1:3137";
const user = { userId: "synthetic-user", displayName: "Alex Morgan", email: "alex@example.invalid" };
const headers = {
  "access-control-allow-origin": origin,
  "access-control-allow-credentials": "true"
};

async function fixture(browser, {
  me = user, width = 1280, theme = "light", delayAuth = false, authStatus = 200, failAuth = false
} = {}) {
  const context = await browser.newContext({
    viewport: { width, height: 1000 }, colorScheme: theme, hasTouch: width < 720,
    serviceWorkers: "block"
  });
  const page = await context.newPage();
  page.setDefaultTimeout(8000);
  const errors = [];
  const authRequests = [];
  const authNavigations = [];
  page.on("pageerror", (error) => errors.push(error.message));
  let releaseAuth;
  const gate = new Promise((resolve) => { releaseAuth = resolve; });
  if (!delayAuth) releaseAuth();
  await context.route("**/*", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (!url.pathname.startsWith("/api/")) {
      if (url.origin === origin && request.method() === "GET") return route.continue();
      throw new Error(`Unexpected external request: ${request.method()} ${url}`);
    }
    assert.equal(request.method(), "GET", `Unexpected API write: ${url}`);
    if (url.pathname === "/api/auth/me") {
      authRequests.push(url.href);
      await gate;
      if (failAuth) return route.abort("failed");
      return route.fulfill({
        status: me ? authStatus : 401, headers, json: me || { error: "Not signed in" }
      });
    }
    if (url.pathname === "/api/auth/login" || url.pathname === "/api/auth/logout") {
      authNavigations.push(url.href);
      return route.fulfill({ contentType: "text/html", body: "<p>Synthetic authentication destination</p>" });
    }
    const reply = (json) => route.fulfill({ json, headers });
    if (url.pathname === "/api/settings/integrations") return reply([]);
    if (url.pathname === "/api/notifications/settings") {
      return reply({ dailyDigestTime: "09:00", eveningReminderTime: "18:00", teamsDestination: "personal-chat" });
    }
    if (url.pathname === "/api/tasks" || url.pathname === "/api/categories"
      || url.pathname.startsWith("/api/week-confidence/")) return reply([]);
    if (url.pathname.startsWith("/api/week-plans/")) {
      return reply({ weekStartDate: url.pathname.split("/").pop(), workloadMode: "Yellow", notes: null });
    }
    throw new Error(`Unexpected API request: ${request.method()} ${url}`);
  });
  return {
    page, authRequests, authNavigations, releaseAuth, errors,
    async close() { releaseAuth(); await context.close(); }
  };
}

const trigger = (page) => page.getByRole("button", { name: /^Account menu for / });
const menu = (page) => page.getByRole("menu", { name: "Account", exact: true });
const settings = (page) => menu(page).getByRole("menuitem", { name: "Settings", exact: true });
const signOut = (page) => menu(page).getByRole("menuitem", { name: "Sign out", exact: true });

async function assertFocused(locator) {
  assert.ok(await locator.evaluate((element) => element === document.activeElement), "Expected focused element");
}

async function assertClosed(page) {
  await menu(page).waitFor({ state: "hidden" });
  assert.equal(await trigger(page).getAttribute("aria-expanded"), "false");
}

async function assertCompact(page, signedIn) {
  const control = signedIn ? trigger(page) : page.getByRole("button", { name: "Sign in", exact: true });
  const bounds = await control.boundingBox();
  const header = await page.locator(".top-bar").boundingBox();
  const viewport = page.viewportSize();
  assert.ok(bounds.width <= 100 && bounds.height === 44, `Compact control: ${JSON.stringify(bounds)}`);
  assert.ok(header.height <= 60, `Compact header: ${header.height}`);
  assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= viewport.width, "Control stays inside viewport");
  const rightInset = viewport.width - bounds.x - bounds.width;
  assert.ok(Math.abs(rightInset - (viewport.width <= 720 ? 16 : 24)) <= 1, "Control stays in top-right");
  assert.equal(await page.locator(".user-switcher").count(), 0, "The user tile is removed");
}

(async () => {
  const browser = await chromium.launch({
    channel: process.env.USER_NAV_TEST_BROWSER || "chrome", headless: true
  });
  let passed = 0;
  async function check(name, options, run) {
    const test = await fixture(browser, options);
    try {
      await run(test);
      assert.deepEqual(test.errors, [], "No browser runtime errors");
      console.log(`PASS ${name}`);
      passed++;
    } finally {
      await test.close();
    }
  }
  try {
    await check("signed-in account is compact and details only appear in the dropdown", {}, async ({ page, authRequests }) => {
      await page.goto(origin);
      await trigger(page).waitFor();
      await assertCompact(page, true);
      assert.equal(await trigger(page).getAttribute("aria-haspopup"), "menu");
      assert.equal(await page.locator(".account-avatar").textContent(), "AM");
      assert.equal(await page.getByText(user.email, { exact: true }).count(), 0);
      assert.equal(await page.getByRole("button", { name: "Sign in", exact: true }).count(), 0);
      const contentBefore = await page.locator("main").boundingBox();
      await trigger(page).click();
      await menu(page).waitFor();
      assert.equal(await trigger(page).getAttribute("aria-expanded"), "true");
      assert.equal(await trigger(page).getAttribute("aria-controls"), await menu(page).getAttribute("id"));
      assert.equal(await page.locator(".account-menu-name").textContent(), user.displayName);
      assert.equal(await page.locator(".account-menu-detail").textContent(), user.email);
      assert.equal(await menu(page).getByRole("menuitem").count(), 2);
      assert.equal(await settings(page).getAttribute("href"), "/settings");
      await assertFocused(settings(page));
      assert.deepEqual(await page.locator("main").boundingBox(), contentBefore, "Opening must not shift content");
      await trigger(page).click();
      await assertClosed(page);
      assert.equal(authRequests.length, 1, "Opening/closing must not refetch or alter the session");
    });

    await check("loading does not flash Sign in or an account menu", { delayAuth: true }, async ({ page, releaseAuth }) => {
      await page.goto(origin);
      await page.getByRole("status").filter({ hasText: "Checking sign-in..." }).waitFor();
      assert.equal(await page.locator(".account-navigation button").count(), 0);
      assert.ok((await page.locator(".top-bar").boundingBox()).height <= 60);
      releaseAuth();
      await trigger(page).waitFor();
      assert.equal(await page.getByRole("status").count(), 0);
    });

    await check("signed-out Sign in preserves Microsoft login and full return URL", { me: null }, async ({
      page, authRequests, authNavigations
    }) => {
      const returnUrl = `${origin}/?source=account%20menu#return-here`;
      await page.goto(returnUrl);
      const signIn = page.getByRole("button", { name: "Sign in", exact: true });
      await signIn.waitFor();
      await assertCompact(page, false);
      assert.equal(await trigger(page).count(), 0);
      assert.equal(await menu(page).count(), 0);
      await signIn.click();
      await page.waitForURL((url) => url.pathname === "/api/auth/login");
      assert.equal(authNavigations.length, 1);
      const navigation = new URL(authNavigations[0]);
      assert.equal(navigation.origin, new URL(authRequests[0]).origin);
      assert.equal(navigation.searchParams.get("returnUrl"), returnUrl);
    });

    await check("Sign out preserves existing logout endpoint and origin return URL", {}, async ({
      page, authRequests, authNavigations
    }) => {
      await page.goto(`${origin}/?from=menu#account`);
      await trigger(page).click();
      await signOut(page).click();
      await page.waitForURL((url) => url.pathname === "/api/auth/logout");
      assert.equal(authNavigations.length, 1);
      const navigation = new URL(authNavigations[0]);
      assert.equal(navigation.origin, new URL(authRequests[0]).origin);
      assert.equal(navigation.searchParams.get("returnUrl"), origin);
    });

    await check("keyboard opening, arrows, Home/End, Escape and Tab", {}, async ({ page }) => {
      await page.goto(origin);
      await trigger(page).waitFor();
      for (const key of ["Enter", " ", "ArrowDown", "ArrowUp"]) {
        await trigger(page).focus();
        await trigger(page).press(key);
        await menu(page).waitFor();
        await assertFocused(key === "ArrowUp" ? signOut(page) : settings(page));
        await page.keyboard.press("Escape");
        await assertClosed(page);
        await assertFocused(trigger(page));
      }
      await trigger(page).press("ArrowDown");
      await settings(page).press("ArrowUp");
      await assertFocused(signOut(page));
      await signOut(page).press("ArrowDown");
      await assertFocused(settings(page));
      await settings(page).press("End");
      await assertFocused(signOut(page));
      await signOut(page).press("Home");
      await assertFocused(settings(page));
      await page.keyboard.press("Tab");
      await assertClosed(page);
      await trigger(page).focus();
      await trigger(page).press("Enter");
      await settings(page).press("Shift+Tab");
      await assertClosed(page);
      await assertFocused(page.locator(".sidebar").getByRole("link", { name: "Settings", exact: true }));
    });

    await check("outside click, focus departure, Settings navigation and history dismiss the menu", {}, async ({
      page, authRequests
    }) => {
      await page.goto(origin);
      await trigger(page).click();
      await page.getByRole("heading", { name: "Welcome to Rora Quest", exact: true }).click();
      await assertClosed(page);
      await trigger(page).click();
      await page.locator(".sidebar").getByRole("link", { name: "Home", exact: true }).focus();
      await assertClosed(page);
      await trigger(page).click();
      await settings(page).click();
      await page.waitForURL(`${origin}/settings`);
      await assertClosed(page);
      await trigger(page).click();
      await settings(page).click();
      await assertClosed(page);
      await assertFocused(trigger(page));
      await trigger(page).click();
      await page.goBack();
      await page.waitForURL(`${origin}/`);
      await assertClosed(page);
      assert.equal(authRequests.length, 1, "Client navigation preserves the existing loaded session");
    });

    for (const [name, me, label, initials] of [
      ["email fallback", { ...user, displayName: null }, user.email, "AL"],
      ["user ID fallback", { ...user, displayName: null, email: null }, user.userId, "SY"],
      ["blank display name", { ...user, displayName: "   " }, user.email, "AL"],
      ["single-word name", { ...user, displayName: "Alex" }, "Alex", "AL"],
      ["Unicode name", { ...user, displayName: "\u00c9lodie \u674e" }, "\u00c9lodie \u674e", "\u00c9\u674e"]
    ]) {
      await check(name, { me }, async ({ page }) => {
        await page.goto(origin);
        await trigger(page).waitFor();
        assert.equal(await trigger(page).getAttribute("aria-label"), `Account menu for ${label}`);
        assert.equal(await page.locator(".account-avatar").textContent(), initials);
        await trigger(page).click();
        assert.equal(await page.locator(".account-menu-name").textContent(), label);
        assert.ok(await signOut(page).isVisible());
      });
    }

    for (const [name, options] of [
      ["failed session check", { authStatus: 500 }],
      ["unreachable session check", { failAuth: true }]
    ]) {
      await check(`${name} retains the existing signed-out fallback`, options, async ({ page }) => {
        await page.goto(origin);
        await page.getByRole("button", { name: "Sign in", exact: true }).waitFor();
        assert.equal(await trigger(page).count(), 0);
      });
    }

    const longUser = {
      ...user, displayName: "LongAccountNameWithoutSpaces".repeat(5),
      email: `${"long-email-".repeat(8)}@example.invalid`
    };
    for (const theme of ["light", "dark"]) {
      for (const width of [320, 390, 768, 1280, 1920]) {
        await check(`${theme} ${width}px long account details and touch/layout`, {
          width, theme, me: longUser
        }, async ({ page }) => {
          await page.goto(origin);
          await trigger(page).waitFor();
          await assertCompact(page, true);
          assert.equal(await page.locator("html").getAttribute("data-theme"), theme);
          if (width < 720) await trigger(page).tap();
          else await trigger(page).click();
          const panel = page.locator(".account-menu-panel");
          await panel.waitFor();
          const geometry = await panel.evaluate((element) => {
            const rect = element.getBoundingClientRect();
            return {
              left: rect.left, right: rect.right, width: rect.width,
              clientWidth: element.clientWidth, scrollWidth: element.scrollWidth,
              background: getComputedStyle(element).backgroundColor,
              color: getComputedStyle(element).color,
              children: [...element.querySelectorAll("p, [role=menuitem]")].map((child) => {
                const bounds = child.getBoundingClientRect();
                return { left: bounds.left, right: bounds.right, clientWidth: child.clientWidth, scrollWidth: child.scrollWidth };
              })
            };
          });
          assert.ok(geometry.left >= 0 && geometry.right <= width, "Menu fits viewport");
          assert.ok(geometry.width <= 280 && geometry.scrollWidth <= geometry.clientWidth, "Menu does not overflow");
          assert.equal(geometry.background, theme === "dark" ? "rgb(30, 41, 59)" : "rgb(255, 255, 255)");
          assert.equal(geometry.color, theme === "dark" ? "rgb(241, 245, 249)" : "rgb(17, 24, 39)");
          for (const child of geometry.children) {
            assert.ok(child.left >= geometry.left && child.right <= geometry.right);
            assert.ok(child.scrollWidth <= child.clientWidth + 1, "Identity wraps without clipping");
          }
          if (process.env.USER_NAV_SCREENSHOT_DIR && [390, 1280].includes(width)) {
            mkdirSync(process.env.USER_NAV_SCREENSHOT_DIR, { recursive: true });
            await page.screenshot({ path: path.join(process.env.USER_NAV_SCREENSHOT_DIR, `account-${theme}-${width}.png`) });
          }
          if (width < 720) await settings(page).tap();
          else await settings(page).click();
          await page.waitForURL(`${origin}/settings`);
          await assertClosed(page);
          await assertCompact(page, true);
          await page.locator(".sidebar").getByRole("link", { name: "Tasks by Week", exact: true }).click();
          await page.waitForURL(`${origin}/tasks`);
          await assertCompact(page, true);
        });
        await check(`${theme} ${width}px signed-out control`, { width, theme, me: null }, async ({ page }) => {
          await page.goto(origin);
          await page.getByRole("button", { name: "Sign in", exact: true }).waitFor();
          await assertCompact(page, false);
          assert.equal(await menu(page).count(), 0);
        });
      }
    }
    console.log(`${passed} account navigation browser checks passed.`);
  } finally {
    await browser.close();
  }
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
