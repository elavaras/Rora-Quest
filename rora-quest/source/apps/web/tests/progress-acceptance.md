# Progress web acceptance

These tests use **synthetic contract fixtures**, not the live API. The complete
small example from design §5 is retained in `progress-example.json`.
`progress-fixtures.cjs` is independent of production date/projection helpers.
No test changes authentication or writes real tasks.

## Commands

Run from `rora-quest\source\apps\web`.

```powershell
npm test

# Existing optional lint tooling; use your installed tooling location if different.
$env:NODE_PATH = "$env:TEMP/rora-week-lint-tools/node_modules;$(Get-Location)/node_modules"
npm run lint
npm run build

# In a separately tracked foreground shell; stop only this server afterward.
node node_modules/next/dist/bin/next start --hostname 127.0.0.1 --port 3147
```

In the test shell:

```powershell
$env:NODE_PATH = "$env:TEMP/rora-week-browser-tools/node_modules"
$env:PROGRESS_TEST_ORIGIN = 'http://127.0.0.1:3147'
node tests/progress.browser.cjs
node tests/progress.performance.browser.cjs
$env:PROGRESS_TEST_BROWSER = 'msedge'
node tests/progress.browser.cjs

$env:WEEK_TEST_ORIGIN = $env:PROGRESS_TEST_ORIGIN
$env:USER_NAV_TEST_ORIGIN = $env:PROGRESS_TEST_ORIGIN
node tests/week-navigation.browser.cjs
node tests/task-tile-layout.browser.cjs
node tests/user-navigation.browser.cjs
```

`playwright-core`, ESLint and `eslint-config-next` were already available in
temporary tooling directories. No application dependency or lockfile changes
were needed. `.eslintrc.cjs` enables the previously unconfigured lint command
without an interactive prompt. Both the optional lint directory and the web
`node_modules` must be resolvable because the Next ESLint parser uses Next itself.

## Coverage and implementation notes

- Production-function tests transpile real TypeScript in memory, as the existing
  week-date tests do: Gregorian keys, bounds/defaults, selection, gap highlight,
  redirect parser, Monday entry, required fields/enums/counts/nulls, snapshot
  consistency, safe links, UTC/DST instants and continuation ordering/completeness.
- Browser acceptance covers all five areas, one activity intensity, coverage
  states, retained names/removed links, current weighted/terminal task progress,
  307 redirects, exact queries, invalid filters, 1/84-date views, keyboard focus,
  chronological alternative, gap highlight, API errors/null/malformed data,
  page retry/snapshot mismatch, late report/page results, owner changes,
  server-relative midnight, explicit history, Dashboard legacy reads and empty
  Tasks handoff (including same-page query navigation).
- 320px touch and 200% CSS zoom checks run in light/dark with long unbroken names.
  Keyboard tests check focused date and nonempty visible outline. These checks
  are not a substitute for a human screen-reader/comprehension audit.
- Progress renders only server-projected metrics. The browser does not calculate
  activity/gaps from task state. All five areas are replaced atomically; a failed
  continuation retains only the earlier pages with a visible retry.
- Existing `/api/auth/me` is read before/after a report or continuation to guard
  owner context, using existing credentials/fallbacks. No auth source or account
  menu is modified. Focus/visibility/storage/pagehide invalidate old contexts.
- Tasks uses a small Suspense entry wrapper and a query-keyed client instance.
  Explicit Monday state/refs suppress autojump before the first load. Plain
  Tasks initialization, native date picker and existing workflows remain intact.
- Existing task-detail lint warnings (two hook-dependency, two `<img>` warnings)
  are outside this workstream and are left unchanged.

## Validation result

| Check | Result |
|---|---|
| `npm test` | 28 passed; 0 failed |
| `npm run lint` | Passed; only the four existing task-detail warnings described above |
| `npm run build` | Passed, including typecheck/lint; Progress route 11.7 kB / 106 kB first load |
| Progress fixture browser acceptance | 16/16 in installed Chrome; 16/16 in installed Edge |
| Existing week navigation | 15/15 |
| Existing task tile / bulk / move layout | 10/10 |
| Existing account navigation | 33/33 |
| `git diff --check -- rora-quest/source/apps/web` | Passed |

## Performance baseline (frontend fixture only)

2026-10-06: Windows 10.0.26100, Intel Xeon Platinum 8370C 2.80GHz, Node 24.14.1,
Chrome 154.0.8037.92, production Next 14.2.5, viewport 1280x1000. All API traffic
is intercepted locally; each read includes 1,000 weekly tasks and a synthetic
ledger of 10,000 units, with only 100 of 9,999 selected-day units transferred.

| View | Warm samples | Reload-to-usable p95 | Report JSON |
|---|---:|---:|---:|
| Default 28 dates | 20 | 360.9 ms | 399,946 bytes |
| 84 dates | 20 | 351.0 ms | 410,232 bytes |

This measures the frontend fixture path, including render and runtime validation.
It **does not establish the live API's p95, persistence, capture correctness,
two-owner isolation, migration, or restart guarantees**. Those remain backend /
independent tester release gates. No claim of full AC-01–38 live acceptance is made.

## Independent bounded handoff — 2026-10-07

Executed the existing scripts against fresh local processes, without rebuilding,
installing packages, changing auth, or modifying production code. Live run began
at **02:52:51 UTC**. This supplements, rather than replaces, the earlier evidence.

### Setup and commands actually executed

- Read root `AGENTS.md`, PRD/design, both browser scripts, their fixture/loader,
  and `RoraQuest.Progress.TestHost/Program.cs` plus the process-test helper.
- Verified ports 5000/5147/3149 were free. Explicitly initialized a new store at
  `%TEMP%\rora-progress-independent-20261007-b64ffde4a9ae4964b282bea2dcbe6f2a`.
  The test host clears application configuration sources and uses environment
  configuration; `RORAQUEST_PROGRESS_TEST_POSTGRES`,
  `RORAQUEST_PROGRESS_TEST_FIXTURE`, and `ConnectionStrings__Postgres` were unset.
  Only the script's new synthetic owner/tasks were used; no default store or
  production database was contacted.

```powershell
# API directory; Progress__DataDirectory set to the isolated path above.
dotnet .\src\RoraQuest.Api\bin\Release\net8.0\RoraQuest.Api.dll --progress-store-init
# Attached tracked host; ASPNETCORE_URLS=http://127.0.0.1:5147,
# ASPNETCORE_ENVIRONMENT=Testing, DOTNET_ENVIRONMENT=Testing.
dotnet .\tests\RoraQuest.Progress.TestHost\bin\Release\net8.0\RoraQuest.Progress.TestHost.dll

# Web directory; attached tracked server using the already-built production output.
node node_modules/next/dist/bin/next start --hostname 127.0.0.1 --port 3149
$env:NODE_PATH = "$env:TEMP\rora-week-browser-tools\node_modules;$(Get-Location)\node_modules"
$env:PROGRESS_TEST_ORIGIN = 'http://127.0.0.1:3149'
$env:PROGRESS_LIVE_API = 'http://127.0.0.1:5147'
$env:PROGRESS_TEST_BROWSER = 'chrome'
node tests/progress-independent-live.browser.cjs
node tests/progress.browser.cjs
```

Readiness was verified by HTTP 200 from API `/health` and Next `/progress`, with
listener ownership checked. Next 14.2.5 emitted its `output: standalone` /
`next start` warning but served both scripts successfully; packaged standalone
deployment startup was not tested. API PID 28020 and Next PID 34492 were stopped
by exact PID in `finally`; both tracked shells exited. The live script closed its
browser and loopback proxy (PID 7636). No listeners remained on 5000/5147/3149.
The synthetic store and screenshots remain in TEMP, not source control.

### Results and scenario mapping

| Area / relevant ACs (subcases only) | Independent result |
|---|---|
| Activity calendar — AC-02,04,15,17 | **Live PASS:** 28 dates; server today `2026-10-07`, Kolkata reporting with an LA browser; today's positive activity retains partial coverage. Fixed-date navigation, unknown/future states and simulated midnight also passed in fixtures. |
| Day details — AC-07,08,18 | **Live PASS:** 3 units = 1 no-substep task + 2 substeps; final substep automatically completes its parent without a fourth unit. Names, chronological alternative and keyboard selection work. |
| Participation — AC-19 | **Live PASS:** the 3 units produce 1 active day, not 3 participation days. Unknown-history/reliable-empty/no-plan distinctions passed in fixtures. |
| Current weekly plan — AC-20–24 | **Live PASS for weighted example:** 100%/100%/25%, 2 of 3 complete, 75% equal-task mean; schedule dates unchanged by UTC reprojection. Empty-week Tasks handoff and current-state variations passed in fixtures, not a complete live membership/workflow audit. |
| Gaps / last activity — AC-25–30 | **Live PASS for fresh-store states:** longest gap says “Not enough fully tracked days”; current gap is 0 after activity today; latest completed substep appears as last activity. **Fixture PASS:** exact Oct 4–5 longest-gap highlight, first-day selection, equivalent list highlight, clear and range-change reset. Nonzero historical gap projection is not independently live-validated here. |
| Compatibility / reliability — AC-01,10,11,35 | **Live PASS:** both valid legacy routes return 307; invalid legacy filters explain reset; refresh accepts current data; same-state/reopen/recomplete preserve sequence and last instant; actual offline transport shows unavailable (not empty), and retry recovers. Fixtures additionally pass malformed/null/error responses, continuation retry/205 rows, stale responses and simulated owner changes. |
| Accessibility / mobile — AC-27,37 | **Automated PASS:** live 320px no overflow and keyboard/list selection; fixture light/dark, touch, long names, visible focus and 200% CSS zoom. Not a human screen-reader audit. |

- **Live script: exit 0**, six passing groups, **6 real browser Progress responses
  contract-accepted**, no Progress endpoint mocks or browser runtime/contract errors.
  The test-only host uses the real API routes/service/SQLite ledger; its loopback
  proxy only supplies missing test-host CORS headers.
- **Fixture script: exit 0, 16/16 scenarios passed** in installed Chrome. Fixture
  responses are synthetic and do not establish backend persistence/projection.
- Inspected the generated desktop screenshot confirming all five rendered areas.
  Desktop/mobile captures: `%TEMP%\rora-progress-acceptance-live-{desktop,mobile}.png`.
- No targeted API rerun was needed. Parent's fresh results are **reported context,
  not rerun evidence**: API build PASS (6 NU1900 feed warnings), tests 100 PASS /
  6 SKIP; web 28 PASS, lint/build PASS (4 pre-existing task-detail warnings).

### Remaining gates and recommendation

- **No failing AC or acceptance-blocking defect observed in the executed scope.**
  This is not full independent AC-01–38 signoff; unlisted subcases were not
  independently executed by this handoff.
- **PostgreSQL rollout BLOCKED:** no dedicated `RORAQUEST_PROGRESS_TEST_POSTGRES`
  supplied; the parent reports five real PostgreSQL tests skipped. Real migration,
  restart, multi-process, rollback and ownership guarantees remain unverified here
  (notably the PostgreSQL portions of AC-14,31–34,36). Do not substitute SQLite or
  fixture results for those gates.
- **Deployment validation outstanding:** target-runtime/Linux SQLite loading,
  persistent writable sidecar and backup readiness where InMemory is used,
  packaged startup, and draining old writers. No infrastructure was provisioned.
- Human screen-reader/status-announcement validation remains untested. Edge,
  real multi-day clock progression, fresh restart/fault injection, and performance
  were not rerun here; retain prior evidence and its limitations. The parent's
  sixth skip is the opt-in performance test, not a failure.
- **Ready to push as a feature branch**, subject to the parent's separate commit
  hygiene. **Not ready to deploy** until the outstanding release gates are met.
  No files were staged and no commit/push was performed by this tester.
