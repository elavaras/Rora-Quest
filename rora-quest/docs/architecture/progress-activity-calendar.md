# Progress activity calendar — design

**Phase:** Implementation design; execution evidence and outstanding release gates are recorded in the [runbook](../runbooks/progress-activity-calendar.md) and [acceptance handoff](../../source/apps/web/tests/progress-acceptance.md).

**Date:** 2026-10-06

**PRD:** [Progress activity calendar](../prd/progress-activity-calendar.md), including **AC-01–AC-38**.

**Scope:** Calendar, day details, participation count, **current** weekly plan, longest/current gaps and last activity. Paths below are relative to `rora-quest\`.

## 1. Context, constraints, and decision

Use a small retained completion ledger plus explicit recording coverage, projected by the API into a bounded report. Do not reconstruct history from `UpdatedAt`, `CompletedAt`, status events, imports, or current progress. Keep the existing task model and workflow; this is not event-sourcing the application.

Source inspection establishing the integration points:

| Current implementation | Design consequence |
|---|---|
| `source\apps\api\src\RoraQuest.Api\ApiEndpoints.cs`: static route mapping; singleton `RoraQuestService` with an **instance** `_gate` serializing its methods across all owners | Static routing is not mutable global report state. Retain the service lock; add localized store scopes rather than a lock/framework rewrite. |
| `UpdateTaskStatus` and `UpdateSubstep` mutate cached objects **before** `store.Save`; `GetTask` returns references | Stage these mutations before persistence; eviction alone cannot protect already-returned references from phantom completions. |
| `UpdateSubstep` detects no-ops, preserves terminal parents, promotes/reopens parents; status writes can append same-state events | Recognize only qualifying transitions in these two methods, after their existing validation. Preserve row-version, DSA, no-op, and automatic-status behavior. |
| `GetTaskProgress` rounds weighted/count progress to two decimals; Tasks by Week membership is week equality **OR** planned date within the week | Reuse this function and predicate, not frontend integer rounding or status-only completion. |
| `GetTasks` invokes `HealDuplicateTasks`, which deletes duplicates and performs an aggregate save | A Progress read must not invoke this mutating read. Existing cleanup remains supported and cannot delete ledger evidence. |
| `Persistence\PostgresRoraQuestStore.cs`: cached `UserData`, aggregate delete/reinsert transaction, targeted task deletes/settings writes | Ledger must have no task/child cascade. Completion and aggregate save share a transaction. All writers need revision invalidation/serialization. |
| `Persistence\IRoraQuestStore.cs`: default InMemory keeps only process dictionaries | Add durable **Progress-only** backing; task persistence remains unchanged. A restart can leave historical names with no surviving task link. |
| `Program.cs`, `Persistence\DatabaseMigrator.cs`, `infra\sql\V1…V8` | Reuse Npgsql/Dapper and next versioned transactional migration. No EF or migration framework. |
| `infra\aca\main.bicep`: API permits 1–3 replicas; API Dockerfile already copies all `infra/sql` scripts | Do not assume Postgres has a single process or require an infrastructure edit. No mixed pre-capture/new writer rollout. |
| Web `layout.tsx`, `scorecard\page.tsx`, `tracking\page.tsx`, `tasks\page.tsx` | Replace two navigation entries, modify old files into redirects, add a narrow explicit-week entry path that suppresses Tasks' initial auto-jump. |
| API xUnit tests and web `tests\*.test.cjs` / `*.browser.cjs` | Extend existing tooling: node tests transpile production TS; browser scripts use `playwright-core` and installed Chrome/Edge. |

**Options:** (a) mutable-field reconstruction fails the PRD; (b) an in-process ledger fails restart; (c) a hand-written JSON/WAL protocol adds avoidable crash-recovery complexity; **(d) retained Postgres tables and a SQLite sidecar for InMemory is recommended**. One justified new runtime dependency is a pinned .NET 8-compatible `Microsoft.Data.Sqlite` package. No EF, broker, calendar/chart library, third-party telemetry, or whole-application persistence rewrite.

Owner resolution remains exactly `UserScope.GetUserId(http)` inside the existing `/api` route group: current authenticated identity, existing header fallback, existing demo fallback. Do not edit `Auth.cs`, authentication configuration, or `UserScope`. Owner means the aggregate owner, **not** `AssignedTo`. No owner query parameter or cross-owner reporting.

## 2. Component boundaries and durable data

### 2.1 Small implementation units

- `Progress/ProgressContracts.cs`: additive DTOs and validation contract in §5.
- `Progress/ProgressProjection.cs`, `Progress/ReportingDates.cs`: pure coverage/date/gap projection with an injected .NET `TimeProvider`; no store or HTTP access.
- `RoraQuestService.Progress.cs`: partial service implementation, obtaining one immutable report snapshot under the existing gate/store scope; reuse private `GetTaskProgress`.
- `Persistence/Progress*.cs`: store-specific ledger queries, capture certification, and InMemory SQLite support. Extend `IRoraQuestStore` with owner scope, durable owner initialization, snapshot/event reads, and **combined task-mutation commit**; no separate “save event later” API.
- `Progress/ProgressCaptureLifecycle.cs`: small hosted certifier and offline maintenance command handling. Not a general job/outbox system.
- `Progress/ProgressEndpoints.cs`: map the two endpoints onto the **existing authorized-or-development `/api` group**.

### 2.2 Logical schema (both adapters)

Use `infra\sql\V9__progress_activity.sql` if V9 is still free at implementation. Tables are separate from `UserData` and are never replaced by aggregate saves:

| Table | Required columns / constraints |
|---|---|
| `progress_owners` | `owner_id` text PK; immutable `tracking_started_at` instant; `aggregate_revision` bigint default 0. No FK to `users`: read-only first access need not create a legacy user, and retained history must not cascade. |
| `progress_completions` | `sequence` generated positive bigint PK; `owner_id`; `unit_key` text; `kind` (`task`/`substep`); `occurred_at`; `task_id` UUID; nullable `substep_id`; `task_title`; nullable `substep_title`. UNIQUE `(owner_id, unit_key)`; owner FK to `progress_owners` with RESTRICT; **no task/substep/category FK**. |
| `progress_mutation_receipts` | `commit_id` UUID PK; `owner_id` FK with RESTRICT; `committed_at`; `aggregate_revision`. One small retained receipt for each staged status/substep write, including reopen/recomplete with no new event. Resolves an uncertain commit even after later writes advance the owner revision. No task payload or public retry key. |
| `progress_capture_sessions` | `session_id` UUID PK; `started_at`; `verified_through`; nullable `stopped_at`. Only the owning running process extends its finite verified interval. This is shared capture-capability evidence, not per-user presence. |
| `progress_capture_interruptions` | `id` UUID PK; `start_at`; nullable `end_at`; `reason` (`maintenance`, `unrecognizedWriter`, `recovery`). At most one open interruption. Closed rows remain retained; overlap is unioned. |

Index completions on `(owner_id, occurred_at, unit_key)` and `(owner_id, sequence)`. Validate the kind/substep nullability pair and `verified_through >= started_at`, `end_at >= start_at`. PostgreSQL uses `timestamptz`, UUID, bigint; SQLite uses UTC microsecond integers, canonical UUID text, INTEGER keys and equivalent constraints. Store instants at microsecond precision in **both** adapters to prevent restart-dependent ordering.

Identity is `task:<task-guid>` or `substep:<task-guid>:<substep-guid>`, using lowercase D-format GUIDs. Titles are not identity. Retain only completion-time names, IDs, kind, time and ordering/dedup evidence—no notes, attachments or effort fields. A title changed in the same accepted completion request is snapshotted **after** that title change. A new identity with the same name is distinct.

First recorded completion wins permanently. Adding/removing substeps never emits an event; a later genuine no-substep task completion can record its distinct task unit if not previously recorded. Reopen, recomplete, deletes, duplicate cleanup, and coverage interruption never remove or reset the uniqueness key.

### 2.3 Default InMemory durability and lifecycle

Keep `InMemoryRoraQuestStore` as the default task store. Its Progress adapter opens SQLite at:

`Progress:DataDirectory`, default `<LocalApplicationData>\RoraQuest\Progress` (absolute, outside checkout/build/release directories), containing `progress.sqlite` and a registration file with schema/store identity.

- Provide an **offline, explicit, non-destructive** `--progress-store-init` application command for first installation. It creates the registration and empty SQLite schema transactionally/recoverably, refuses a nonempty or conflicting store, and does not create owner coverage. Ordinary API startup **never creates a missing database**. This makes missing data distinguishable from a fresh account; an entire missing directory also fails, rather than silently restarting history.
- SQLite `synchronous=FULL`, transactional writes, foreign keys enabled; use the standard rollback journal for this small single-process sidecar. Hold an exclusive process lock on this directory for the API lifetime. A second local API using the same sidecar fails startup rather than writing an independent volatile task graph.
- Validate registration, schema, integrity and expected owner/session records at startup. Missing, mismatched or corrupt state is unavailable, not `{events:[]}`. No fallback to volatile Progress storage, including in tests.
- Completion records, owner starts, capture intervals/interruptions and dedup keys survive process termination. **Current tasks still do not**; do not resurrect a deleted/missing task from its historical snapshot. This limitation is explicit in the UI help/runbook.
- Back up the stopped sidecar and registration together; restore the same identity and retain all records. After a potentially stale restore, do not reopen capture until recovery establishes a conservative unknown interval; if acknowledged ledger records were lost, restore a complete backup or block release—coverage flags cannot repair missing positive evidence.
- A writable persistent local filesystem is required. An ephemeral container filesystem is **not** a supported durable InMemory deployment. Existing production Postgres needs no sidecar. If a non-Postgres deployment has no persistent directory, that is a concrete release blocker; do not add volumes/cloud resources within this feature.

### 2.4 Postgres serialization and cache correctness

The current process lock is insufficient across the configured replica range. Add a narrow `AcquireOwnerScope(ownerId)` around each existing service critical section that loads/saves owner state (including background-service calls), **inside `_gate` and before `GetUser`**. Nested same-owner calls reuse the scope. `GetKnownUserIds` is enumeration, not a mutation.

For Postgres, the scope owns a connection and a session advisory lock keyed by a stable hash of owner ID; release it in `finally`. Hash collision only serializes unrelated owners. Enlist Save, combined completion, delete and targeted setting operations on that connection. No async work may outlive the scope; existing asset network I/O remains outside its short service critical sections.

At Load, read durable `aggregate_revision`; hydrate only when cache revision differs. Every aggregate/targeted write increments that revision in its database transaction. Extract existing `Persist` body to accept connection/transaction so completion insertion and aggregate replacement commit once. Evict the owner cache on any uncertain/failed write, including targeted deletes. No cached “already recorded” flag substitutes for database uniqueness.

This is mechanical scoping/revision plumbing, not a new domain framework. Keep existing public success/error/version policy. Ensure every writer, including settings, digest scheduling, duplicate healing and deletion, participates; a status-only lock would still allow another replica's stale aggregate to overwrite accepted completion state. A Progress report uses one READ COMMITTED transaction on the scoped connection: take the capture-control transaction lock **before** choosing as-of or reading evidence, then certify/read coverage, ledger and aggregate revision, and build detached DTOs while both owner/control locks remain held. Those locks stabilize the relevant data; do not establish an MVCC snapshot before waiting for the control lock and then miss a just-committed suspension. Initialize a missing owner before this report transaction. Read current aggregate at the observed revision; bypass its cache while an interruption is open. Never return mutable task objects.

## 3. Mutation sequence and failure behavior

```text
existing completion endpoint -> resolve existing owner -> service _gate
  -> acquire owner scope -> load/revision-check -> ensure durable coverage owner
  -> existing policy + version checks
  -> stage task/substep/status-event changes; obtain one authoritative UTC instant
  -> candidate first-completion record, only for qualifying transition
  -> adapter transaction [task aggregate if Postgres + unique ledger insert
                          + revision/commit receipt + capture certification]
  -> durable COMMIT -> publish staged in-memory change -> existing response
```

Recognizers are only:

1. `UpdateTaskStatus`: previous status is not Done, requested status is Done, **zero substeps**, and existing policy accepts.
2. `UpdateSubstep`: previous `IsDone=false`, requested `true`, and existing policy accepts.

Final-substep automatic Done is context, not another unit. Same-state status writes may retain their existing status-event/version behavior without emitting activity. Substep no-ops retain their early-return behavior after required store readiness/owner initialization. No recognizer is added to creation/import, metadata/structure/weight changes, confidence, scheduling, reads, or cleanup.

Stage a detached copy of the affected task, including mutable substeps/lists, and a prospective aggregate view. Never first toggle the live cached object and then attempt persistence. After commit, publish the already-prepared changed fields/list entries into the live objects under the gate; preserve the existing successful-call object-reference behavior relied on by workflow tests. Do not perform fallible I/O after commit and before publication.

- **Postgres:** prospective aggregate, event insertion (`ON CONFLICT DO NOTHING`), receipt/revision and certification share one transaction. Existing aggregate DELETEs never address Progress tables. Targeted delete also becomes transactional with revision update; remove live items only after commit. Apply this staging rule to duplicate removal as well.
- **InMemory:** SQLite transaction stores the candidate event, receipt and certification before publishing the staged task change. There is no pretend durable task aggregate. A crash after durable commit is a committed completion with a lost response; its retained event may outlive the volatile task.
- **Known rollback:** discard staged state, return the existing persistence-error path/non-2xx; subsequent task/report reads see neither phantom state nor event.
- **Ambiguous commit outcome:** use the retained `commit_id` receipt and a fresh durable read while keeping the owner fenced. On a lost Postgres connection, discard its scope/cache and reacquire the owner lock before resolution; only then can receipt absence establish rollback. If committed and no later revision exists, publish the prepared state; if a later write superseded it, hydrate that latest aggregate instead of overwriting it with the prepared copy. If rolled back, discard the copy. If storage cannot resolve this, return non-2xx and keep that owner's task/report reads and writes unavailable until reconciled (or restart/hydration). Never serve the stale graph as a definitive outcome. A committed-but-lost-response retry is allowed to find one legitimate event. Do not replace receipts with one overwritten “last commit ID.”
- Concurrent requests are serialized by owner scope, and the durable unique key is the final dedup guard. Preserve optional `IfMatchVersion` conflicts rather than inventing a new idempotency header/public mutation shape.
- Owner coverage initialization is itself transactional and completes before accepting the first mutation. Failure cannot cache a start, allow completion, or return a zero-history report. A successful standalone initialization followed by a rejected mutation may retain coverage; it is capability, not activity.

## 4. Coverage and rollback truth

### 4.1 Finite certification, not infinite “enabled since”

Before accepting capture-capable traffic, persist a session at actual process readiness. A small hosted timer extends its `verified_through` every 30 seconds, independently of users, visits or completions. Reports and mutations certify through their authoritative instant transactionally. A process can certify only its own continuously healthy capture interval; no extension across suspension, a failed certifier, or an unverified lifecycle break. If more than 90 seconds have passed since that process's last successful certification, start a new session at now instead of extending across the missed interval. On persistence failure, fence capture; recovery starts a new session at recovery time, leaving the uncertain tail untracked. Log certification failures using existing logging.

Initialize each owner at the actual first successful scoped access/mutation, not at deployment or historical account creation. Owner effective reliable coverage is:

`union(verified session intervals) minus interruption intervals`, intersected with `[owner.tracking_started_at, asOfUtc]`.

Treat all intervals as half-open `[start,end)`. A day ending exactly at `verified_through` is covered. A report certifies its live process through `asOfUtc`; events at `asOfUtc` are included. Do not issue timestamps in the past from a client. Detect backwards clock movement relative to stored certification and fail/suspend rather than silently backdate.

Sessions contain **finite** persisted ends. A normal restart preserves all earlier verified coverage, events and starts, but does not automatically certify the final unverified seconds or downtime. The new session starts at actual readiness; only genuinely overlapping verified intervals merge. A stopped/old process cannot extend its record. Conservative partial days after restart are preferable to unproved continuity. This also preserves coverage for owners with zero events and no page visits.

### 4.2 Practical maintenance and old-writer defense

Provide offline commands in the API executable, not public HTTP/admin/auth endpoints:

- `--progress-capture-suspend`: drain and stop all API writers first (also releases the InMemory directory lock), then durably open a `maintenance` interruption at command time. Normal shutdown may best-effort certify its own final healthy instant; the offline command **never extends a stopped session**. Any interval between last certification and the command is already unknown. Print the persisted boundary/operation ID; if this fails, **do not** start an old binary.
- `--progress-capture-resume`: operator first confirms **all** old and upgraded API processes are stopped, then closes the open interruption at command time. Start fresh upgraded processes with empty aggregate caches and new sessions at readiness. The command-to-readiness interval remains unknown; no inferred bridge. Restarting all processes also eliminates stale caches from unrecognized writers that did not increment aggregate revisions.
- Recovery accepts an explicit `--unknown-since <UTC instant>` **only to widen uncertainty**, never to backfill reliable coverage. If the earliest possibly unsafe write cannot be located, use the earliest capture-session start. Retain events throughout the exclusion.

To cover forgotten/unannounced/mixed-version Postgres rollback, V9 also installs a small **BEFORE STATEMENT trigger** on INSERT/UPDATE/DELETE of `task_items` and `task_sub_steps`. New store transactions set a **transaction-local** `roraquest.progress_capture_version='1'` tag on all these writes (including aggregate replacement and cleanup). Untagged writes are still allowed for old-client/backend compatibility, but transactionally create/widen an open `unrecognizedWriter` interruption. If no capture session has ever existed, the trigger does nothing: initial migration/seed has no reliable history to invalidate. Otherwise conservatively start the interruption at the earliest capture-session start when a narrower trustworthy cutoff is unavailable. Serialize interruption changes with one database advisory lock; a partial unique index permits only one open interruption. Do not throw from the trigger merely because capture is suspended; ordinary old writes must remain possible. The before-statement hook takes control before task-row locks; nevertheless an old aggregate transaction may already hold other row locks, so mixed-version overlap is not a supported deployment mode and ordinary database deadlock errors must roll back, never partly certify.

The tag is a consistency protocol, not an authorization mechanism. Do not change privileges/auth. The trigger never reconstructs completions. Old writers cannot erase ledger tables, and their successful task writes cannot leave apparently continuous inactivity. Seed scripts are likewise unrecognized writes if run after capture; finish seed/migration before rollout/resume.

Upgraded writers check suspension in the same transaction used for tagged writes/certification, using the same control lock ordering. While suspended, qualifying mutations fail closed; retained reports may be read with `captureReliableNow=false` and current gap unavailable (except positive activity today). Missing/corrupt storage instead yields an error. No new process automatically clears suspension.

InMemory permits only one active API/sidecar. An old binary cannot extend the finite sidecar sessions; on resume, all time after the last durable certification until new readiness stays unknown, even if the operator forgot the suspension command. Rollbacks with overlapping independent volatile APIs are unsupported; drain before replacement.

Lock order for new writers: service gate → owner advisory scope → capture-control transaction lock. Lifecycle/trigger control never acquires owner locks, preventing inversion. Certification reads/writes and report evidence must be a coherent snapshot, not pieced together after releasing the scope.

## 5. Shared HTTP contract — freeze before parallel implementation

### 5.1 Requests, serialization and errors

**New:** `GET /api/progress?from=YYYY-MM-DD&to=YYYY-MM-DD&selectedDate=YYYY-MM-DD&timeZone=Asia%2FKolkata`

All parameters are optional subject to:

| Parameter | Exact rule |
|---|---|
| `from`, `to` | Both absent → current reporting-zone Monday minus 21 dates through current Sunday. Otherwise both required; strict invariant `yyyy-MM-dd`, real Gregorian date, no whitespace/datetime; inclusive span **1–84**; `0001-01-01` through `9999-12-26`; from ≤ to. Near domain endpoints clamp only the computed default to the supported domain, never a supplied range. |
| `selectedDate` | Same date grammar/domain and inside resolved range. Absent → reporting today when in range, otherwise first date. Outside range is 400, not silently corrected. |
| `timeZone` | Absent → **`Asia/Kolkata`**. V1 exact, case-sensitive allowlist: `Asia/Kolkata`, `UTC`, `America/New_York`. Empty, unknown, duplicate, or >100 characters is 400; no OS/browser-local fallback. |

Reject duplicate or unrecognized query keys on the new endpoint; no request `asOf`/owner override. The API controls time via injected `TimeProvider`. Dates are strings, never JS instants. Use .NET `DateOnly` day arithmetic and `TimeZoneInfo`; resolve IANA IDs explicitly (Windows mapping where needed), failing readiness if required zones are unavailable.

Local day boundaries are the first instants of successive local dates, not `start + 24h`. At an ambiguous midnight use the earliest occurrence; at an invalid midnight use the first valid instant belonging to that date. Compare pre-tracking/out-of-instant-domain dates without overflowing .NET UTC conversion (signed boundary ticks or early coverage exclusion); valid year-0001 input must not 500. Schedule dates are never timezone-shifted.

**Continuation:** `GET /api/progress/events?cursor=<opaque>`; cursor is required, single, max 2048 characters; no other query keys. Page size is fixed at **100**. It encodes a version, resolved owner binding, date, zone, snapshot as-of, maximum committed sequence, and last `(occurredAtUtc, unitKey)`. Validate every decoded field; malformed/mismatched-owner/future-snapshot cursors return 400. A cursor is not authorization: always resolve/filter by the current owner. Keyset paging uses the fixed snapshot sequence and instant, so later events cannot shift pages. No in-memory report-session registry or full-ledger browser transfer.

200 responses are `application/json`, `Cache-Control: no-store`. JSON names below are **camelCase exactly**, enum strings exactly as shown, GUIDs lowercase D-format, instants RFC3339 with six fractional digits and `Z`; local timestamps include their offset. `number` means finite JSON number; count/day fields are nonnegative integers. Sequences are decimal **strings**, avoiding JS bigint precision loss. Every listed field is present; arrays may be empty, never null; nullable fields explicitly contain `null`.

400 body: `{"code":"invalidProgressQuery","message":"…","errors":{"from":["…"]}}`, with `errors` a map of query-field names to nonempty string arrays. 503 storage/certification failure: `{"code":"progressUnavailable","message":"Progress records are unavailable; retry later.","errors":{}}`; no internal path/DB details. Existing 401/403 behavior remains unchanged. Existing mutation APIs retain payloads/status policy, with persistence failures non-2xx as before; **no existing report endpoint is repurposed**.

### 5.2 Normative response shapes

The following TypeScript notation is the wire schema, not an implementation file. `DateKey`/`Instant`/`LocalInstant` are strings in the formats above; `Guid` is a string. New categorical fields are strings, not numeric .NET enums. Existing task statuses retain PascalCase.

```ts
type TaskStatus = "Todo" | "InProgress" | "Done" | "Cancelled" | "Skipped";
type Day = {
  date: DateKey;
  position: "elapsed" | "today" | "future";
  status: "active" | "inactive" | "partial" | "unknown" | "todayPending" | "future";
  coverage: "full" | "partial" | "none" | "reliableSoFar" | "notApplicable";
  coverageReasons: ("beforeTracking" | "trackingStartedDuringDay" |
    "captureInterruption" | "dayInProgress" | "futureDate")[];
  unitCount: number; taskCount: number; substepCount: number; substepTaskCount: number;
};
type Completion = {
  unitKey: string; sequence: string; kind: "task" | "substep";
  occurredAtUtc: Instant; occurredAtLocal: LocalInstant; localDate: DateKey;
  taskId: Guid; substepId: Guid | null;
  taskTitle: string; substepTitle: string | null;
  availability: "available" | "taskRemoved" | "substepRemoved";
  taskHref: string | null;
};
type EventPage = {
  date: DateKey; timeZone: string; asOfUtc: Instant; snapshotSequence: string;
  totalCount: number; events: Completion[]; nextCursor: string | null;
};
type LongestGap = {
  scope: "selectedRange"; state: "gap" | "none" | "insufficientCoverage";
  days: number | null; from: DateKey | null; to: DateKey | null;
  boundaries: ("rangeStart" | "rangeEnd" | "coverageStart" | "coverageEnd")[];
};
type CurrentGap = {
  scope: "allReliableHistory"; state: "gap" | "none" | "unavailable";
  days: number | null; from: DateKey | null; to: DateKey | null;
  throughDate: DateKey | null; lowerBound: boolean;
  reason: "sinceLastActivity" | "activityToday" | "activityYesterday" |
    "trackingStart" | "coverageBoundary" | "yesterdayUnknown" |
    "captureUnavailable" | "noElapsedDay";
};
type WeeklyTask = {
  taskId: Guid; title: string; status: TaskStatus;
  plannedWeekStart: DateKey; plannedDate: DateKey | null; taskHref: string;
  progressPercent: number; isComplete: boolean;
  progressBasis: "weightedSubsteps" | "substepCount" | "taskStatus";
  doneWeight: number; totalWeight: number; doneSubsteps: number; totalSubsteps: number;
};
type ProgressReport = {
  asOfUtc: Instant; snapshotSequence: string; timeZone: string;
  today: DateKey; nextMidnightUtc: Instant;
  from: DateKey; to: DateKey; selectedDate: DateKey; isDefaultRange: boolean;
  coverage: {
    trackingStartedAtUtc: Instant; captureReliableNow: boolean; hasInterruptions: boolean;
  };
  days: Day[];
  participation: {
    activeDays: number; fullyTrackedElapsedDays: number;
    unknownOrPartialElapsedDays: number; includesToday: boolean; futureDays: number;
  };
  longestGap: LongestGap; currentGap: CurrentGap;
  lastActivity: {
    scope: "allRecordedHistory"; state: "recorded" | "none"; event: Completion | null;
  };
  selectedDay: EventPage;
  weeklyPlan: {
    weekStart: DateKey; weekEnd: DateKey; asOfUtc: Instant;
    extendsOutsideRange: boolean; totalTasks: number; completeTasks: number;
    progressPercent: number | null; tasksHref: string; tasks: WeeklyTask[];
  };
};
```

`GET /events` returns exactly `EventPage`. All report areas use one snapshot/as-of; `selectedDay` uses the same sequence/as-of as its containing report. `snapshotSequence` is the maximum committed sequence for this owner at snapshot time, or `"0"`. `days` includes every requested date in ascending order, never padding dates. No null “successful empty report.” `coverage.hasInterruptions` means at least one uncovered positive-duration interval after this owner's tracking start and before as-of, including uncertified downtime, not merely an explicit interruption-table row.

Counts: `unitCount = taskCount + substepCount`; `taskCount` counts task-unit events, **not** parent headings; `substepTaskCount` counts distinct parent task IDs among that day's substep events. A task can have different historical unit kinds; avoid copy claiming these parent sets are necessarily disjoint. `totalCount` equals selected day's `unitCount`, even when only 100 events are returned. Events sort by `(occurredAtUtc ascending, unitKey ordinal ascending)`; PostgreSQL text comparison uses C collation. Last activity is the maximum of that same tuple over all committed owner events through as-of, not only the selected range.

Names always come from the event. Link resolution checks the scoped **current** aggregate: `/tasks/<guid>` only when the task exists and, for a substep unit, that substep still exists in it. Otherwise href is null with the appropriate removed label. A substep removal suppresses the link even if its parent survives. Availability in a later continuation response is evaluated at that response's read; the snapshot counts/names/time stay fixed. Current status is deliberately absent from historical events.

### 5.3 Complete small response example

For `/api/progress?from=2026-10-06&to=2026-10-06` with one first substep completion today and one currently scheduled 25%-complete task:

```json
{
  "asOfUtc": "2026-10-06T06:31:00.000000Z",
  "snapshotSequence": "1",
  "timeZone": "Asia/Kolkata",
  "today": "2026-10-06",
  "nextMidnightUtc": "2026-10-06T18:30:00.000000Z",
  "from": "2026-10-06", "to": "2026-10-06", "selectedDate": "2026-10-06",
  "isDefaultRange": false,
  "coverage": {
    "trackingStartedAtUtc": "2026-10-01T04:30:00.000000Z",
    "captureReliableNow": true, "hasInterruptions": false
  },
  "days": [{
    "date": "2026-10-06", "position": "today", "status": "active",
    "coverage": "reliableSoFar", "coverageReasons": ["dayInProgress"],
    "unitCount": 1, "taskCount": 0, "substepCount": 1, "substepTaskCount": 1
  }],
  "participation": {
    "activeDays": 1, "fullyTrackedElapsedDays": 0,
    "unknownOrPartialElapsedDays": 0, "includesToday": true, "futureDays": 0
  },
  "longestGap": {
    "scope": "selectedRange", "state": "insufficientCoverage",
    "days": null, "from": null, "to": null, "boundaries": []
  },
  "currentGap": {
    "scope": "allReliableHistory", "state": "none", "days": 0,
    "from": null, "to": null, "throughDate": "2026-10-05",
    "lowerBound": false, "reason": "activityToday"
  },
  "lastActivity": {
    "scope": "allRecordedHistory", "state": "recorded",
    "event": {
      "unitKey": "substep:11111111-1111-1111-1111-111111111111:22222222-2222-2222-2222-222222222222",
      "sequence": "1", "kind": "substep",
      "occurredAtUtc": "2026-10-06T06:30:00.000000Z",
      "occurredAtLocal": "2026-10-06T12:00:00.000000+05:30", "localDate": "2026-10-06",
      "taskId": "11111111-1111-1111-1111-111111111111",
      "substepId": "22222222-2222-2222-2222-222222222222",
      "taskTitle": "Practice graph traversal", "substepTitle": "Trace the algorithm",
      "availability": "available", "taskHref": "/tasks/11111111-1111-1111-1111-111111111111"
    }
  },
  "selectedDay": {
    "date": "2026-10-06", "timeZone": "Asia/Kolkata",
    "asOfUtc": "2026-10-06T06:31:00.000000Z", "snapshotSequence": "1", "totalCount": 1,
    "events": [{
      "unitKey": "substep:11111111-1111-1111-1111-111111111111:22222222-2222-2222-2222-222222222222",
      "sequence": "1", "kind": "substep",
      "occurredAtUtc": "2026-10-06T06:30:00.000000Z",
      "occurredAtLocal": "2026-10-06T12:00:00.000000+05:30", "localDate": "2026-10-06",
      "taskId": "11111111-1111-1111-1111-111111111111",
      "substepId": "22222222-2222-2222-2222-222222222222",
      "taskTitle": "Practice graph traversal", "substepTitle": "Trace the algorithm",
      "availability": "available", "taskHref": "/tasks/11111111-1111-1111-1111-111111111111"
    }],
    "nextCursor": null
  },
  "weeklyPlan": {
    "weekStart": "2026-10-05", "weekEnd": "2026-10-11",
    "asOfUtc": "2026-10-06T06:31:00.000000Z", "extendsOutsideRange": true,
    "totalTasks": 1, "completeTasks": 0, "progressPercent": 25,
    "tasksHref": "/tasks?weekStart=2026-10-05",
    "tasks": [{
      "taskId": "11111111-1111-1111-1111-111111111111", "title": "Practice graph traversal",
      "status": "Todo", "plannedWeekStart": "2026-10-05", "plannedDate": "2026-10-06",
      "taskHref": "/tasks/11111111-1111-1111-1111-111111111111",
      "progressPercent": 25, "isComplete": false, "progressBasis": "weightedSubsteps",
      "doneWeight": 1, "totalWeight": 4, "doneSubsteps": 1, "totalSubsteps": 2
    }]
  }
}
```

Additional exact variants: no events → `"snapshotSequence":"0"` when the owner has no events at all, `events:[]`, `nextCursor:null`, `lastActivity:{"scope":"allRecordedHistory","state":"none","event":null}`. No scheduled tasks → `totalTasks:0`, `completeTasks:0`, `progressPercent:null`, `tasks:[]` (retain week bounds/link).

## 6. Projection rules engineers must share

### 6.1 Day states and participation

Determine coverage separately from positive evidence:

| Date/evidence | `status` | `coverage` |
|---|---|---|
| Elapsed, full midnight-to-midnight reliability, no event | `inactive` | `full` |
| Elapsed, some but not all of that day reliable, no event | `partial` | `partial` |
| Elapsed, no reliable portion, no event | `unknown` | `none` |
| Today, no event | `todayPending` | `reliableSoFar` if midnight→as-of reliable; otherwise `partial`/`none` |
| Any elapsed/today date with event(s) | `active` | Independently determined as above |
| Future | `future` | `notApplicable` |

An elapsed active date can still be partial/unknown after an uncertainty correction. Today can never have `coverage:"full"` or `status:"inactive"`. `coverageReasons` includes every applicable reason, in the schema's listed order, without duplicates: a date ending at/before tracking start → `beforeTracking`; start strictly inside that date → `trackingStartedDuringDay`; any uncovered portion after tracking start → `captureInterruption` (including uncertified downtime); today → `dayInProgress`. Future dates always have just `["futureDate"]`. Fully reliable elapsed dates have `[]`.

Only `status:"inactive"` is gap-eligible. Active positive days—including today/partial days—count toward `activeDays` once. `fullyTrackedElapsedDays` includes **both** full active and full inactive dates; unknown/partial elapsed is its disjoint complement. Together with today (0/1) and future counts these partition the displayed dates. No participation percentage.

### 6.2 Longest gap and current gap

**Longest:** scan at most 84 dates for contiguous inactive runs. Maximize length, then end date (latest wins). `days = to.DayNumber - from.DayNumber + 1`. `gap` has positive days and nonnull bounds; `none` has 0/null/null; `insufficientCoverage` has null/null/null when no fully tracked elapsed date exists.

For a winning run touching a requested edge, include `rangeStart`/`rangeEnd` conservatively: “Within selected range; the gap may extend outside it.” This flags clipping scope, not a claim that an outside date is actually inactive. Inspect adjacent dates for unknown/partial coverage and include `coverageStart`/`coverageEnd` when that, rather than an active date, stops the run. Today/future are incomplete, not asserted unknown elapsed history. Boundary array order is the schema order. No gap → empty boundaries. Example: Oct 1–5 fully tracked, only Oct 3 active → choose **Oct 4–5, 2 days**, `["rangeEnd"]`. Requested Oct 3–5 within a longer inactive interval → report only Oct 3–5, with both range edges.

**Current:** independent of from/to. Evaluate in this order:

1. Any qualifying event today → `none`, 0, reason `activityToday`, even if today is partially tracked.
2. Capture is suspended or today midnight→as-of is not reliable → `unavailable`, null, `captureUnavailable`.
3. No yesterday in date domain → `unavailable`, `noElapsedDay`.
4. Yesterday active (even if partial) → `none`, 0, `activityYesterday`.
5. Yesterday not fully covered → `unavailable`, `yesterdayUnknown` (or `noElapsedDay` if no full elapsed day exists since initial start).
6. Otherwise count the contiguous full inactive suffix ending yesterday. Stop at positive activity or a coverage boundary. Positive activity immediately before the suffix gives exact length, `sinceLastActivity`, `lowerBound:false`; a boundary gives `trackingStart` or `coverageBoundary`, `lowerBound:true`.

`throughDate` is yesterday for every variant, null only when yesterday is outside the domain. Non-gap variants have null from/to and `lowerBound:false`. A positive gap may exceed the selected longest gap. Lower-bound copy: “At least N fully tracked days; earlier history unknown” / “Since reliable tracking began; no earlier activity recorded.” Otherwise “Through yesterday; today is in progress.” Weekends count; these are absence of recorded completions, **not missed commitments**.

Do not scan every date since year 0001. Normalize coverage into full-local-day spans; find the span ending yesterday and the last active date in/adjacent to it, then subtract DateOnly day numbers. Range aggregation, latest event and the active-date lookup use indexed server queries; only day detail is paged to the browser.

**Last activity:** maximum event tuple across owner history through as-of. `none` means no recorded progress **since the returned tracking start**, not “never worked.” Corrupt/missing coverage causes 503, not this variant. Recompletion/edit/deletion never changes its occurrence time. “View date” uses a one-date range containing `localDate`, selecting that date; the user can then expand/navigate it.

### 6.3 Current weekly plan

Monday–Sunday containing `selectedDate`, even if the activity range clips the week. Use current aggregate membership `PlannedWeekStart == Monday OR PlannedDate in [Monday,Sunday]`, count each ID once. Do not repair inconsistent schedules or use historical events to select tasks.

Each `progressPercent` is exactly existing `GetTaskProgress`: completed weight / positive total weight, otherwise completed substep count / substep count, rounded by that helper; no substeps → Done=100, otherwise 0. `isComplete = progressPercent >= 100`. `progressBasis` exposes the chosen path; weight/substep counts are 0 when there are no substeps. Preserve even legacy weights; do not add effort weighting, validation or a new clamp/rounding rule here.

`completeTasks` counts `isComplete`, including complete-substep Skipped/Cancelled; incomplete terminal tasks remain in the denominator and are labeled by status, not as obligations. Manual Done override with unfinished substeps remains partial. Weekly `progressPercent` is the equal-task mean of existing task progress values, rounded to two decimals; null for no tasks. Display with up to two decimals and use `isComplete`, never integer display rounding, for “complete.”

Sort tasks by planned date (null last), created instant, task ID; show raw date if an inconsistent legacy date lies outside the reviewed week. Null date is **No day selected**. Header: **Current schedule, as of [weeklyPlan.asOfUtc]**; optional mean label **Current plan progress**. Example 100,100,25 → 2 of 3 complete, 75% mean. Rescheduling/reopening/deletion only changes this current view and live link availability.

## 7. Web integration, accessible interaction, and races

- New `/progress` page renders one response atomically; all five required areas use server-projected evidence. Selecting a day reloads the same bounded endpoint with `selectedDate`, refreshing day detail and its full week together. A failed next event page preserves already-loaded detail with a labeled retry, not false completion of the list.
- Query state: from/to/timeZone/selectedDate; absent bounds remain absent in **default mode**. Previous/Next shift by the displayed count using date arithmetic; disable when either resulting endpoint is outside domain (do not silently shorten custom navigation). Retain selected day if in range, else today if included, else first date. “Current four weeks” removes explicit bounds and restores today.
- Use pure Gregorian date-key/ordinal helpers in `progress/dates.ts`; do not interpret a date key as UTC or browser midnight. Reuse the existing Tasks week helper only for its date-picker entry behavior, not for determining reporting today. Format instants with explicit returned zone. Optional selector has exactly the three supported zones; changing it refetches everything without rewriting schedule dates/events.
- Use AbortController **and** a generation key including owner-session identity, bounds, zone, selected date; invalidate immediately on change/unmount/logout. Clear old success state before loading new context. Validate required JSON structure/counts/enums at the new API boundary; no `?? 0` for missing data. Continuation responses must match the active snapshot/date/zone.
- Refresh at `nextMidnightUtc`, on visibility/focus return, and on manual Retry/Refresh. Mark current metrics stale/loading during refresh, especially after a background midnight. Default range follows the new current week; an explicit historical range/selection stays explicit. Schedule delay from server `asOfUtc` to next midnight (not browser timezone); recompute after every response.
- Grid uses Monday-first date buttons, at most 84, full-date/count/state accessible names, keyboard access and visible focus. Provide an equivalent chronological day list using the **same** Day data and detail controls; events in detail are occurrence-ordered. Gaps have a pattern/border/text marker in both presentations, separate from selected-day focus. Selecting longest gap selects its first day and announces its bounds/length; Clear and every range/zone change remove highlight.
- One positive activity level; non-color labels distinguish inactive, partial/unknown, today-pending and future. Loading/error/selection use appropriate live regions. Long names wrap; no hover-only interaction. Use current theme tokens, 24px minimum targets with spacing (44px preferred for navigation), 320px viewport and 200% zoom without page overflow.
- Participation copy is “Recorded progress on X days in [range]” plus coverage counts. All-unknown historical ranges explain unavailable historical coverage, not zero work; empty reliable history, no-plan and network errors remain distinct.
- Always show reporting zone, range, freshness, tracking start and: **“First recorded completions; reopening and rechecking do not add activity.”** Explain retained historical names after deletion. Do not add scheduled rows to day history in v1.

**Redirect contract:** modify, do not delete, `scorecard/page.tsx` and `tracking/page.tsx` into server redirects (Next `redirect`, temporary 307) to `/progress`. No parameters → `/progress`. Accept only one valid from/to pair plus optional legacy `rangeType` in `Weekly|Monthly|Custom` (bounds remain authoritative); transfer bounds, drop rangeType. Incomplete/invalid/duplicated dates, unknown keys, unsupported rangeType, or a rangeType without bounds → `/progress?notice=legacyFiltersReset`. Show a fixed visible explanation there (“The old filters could not be applied; showing the current four weeks”); never echo untrusted values. `notice` is **web-only**, never forwarded to the strict API. Both routes use one shared parser; no redirect back to legacy routes. New-page invalid explicit filters show validation, not an unannounced default.

**Tasks handoff:** `/tasks?weekStart=YYYY-MM-DD` with a valid Monday in the existing complete-week domain. Initialize entry context **before the first loadWeek**; set initial/active week refs and the auto-jump suppression consistently. Use a small server entry wrapper plus client component if needed to avoid Next static `useSearchParams`/Suspense pitfalls. Handle same-page query navigation too. An explicit empty week never auto-jumps; plain `/tasks` retains its existing initialization and auto-jump. Invalid weekStart shows a small explanation and uses existing default behavior. Preserve date picker, Grid/List, bulk selection/move and stale-response guards.

Replace only obsolete primary entries/home copy outside these pages. `dashboard/page.tsx`, task detail workflow and legacy APIs stay unchanged.

## 8. Implementation partitions and sequencing

Only this design document is changed in DESIGN. PRD wording requires no correction.

| Owner | Exclusive implementation files |
|---|---|
| **Backend engineer** | `source\apps\api\src\RoraQuest.Api\Progress\**`, `RoraQuestService.Progress.cs`, `ApiEndpoints.cs` (minimal route hookup, partial service declaration, staged mutation/scoping), `Persistence\**`, `Program.cs` (DI/lifecycle/maintenance wiring only), API `.csproj`; `source\apps\api\tests\RoraQuest.Api.Tests\**` including fixtures and existing-constructor adaptation; **only** `infra\sql\V9__progress_activity.sql` under infra; new `docs\runbooks\progress-activity-calendar.md`. No Auth/security edits. |
| **Web engineer** | `source\apps\web\src\app\progress\**` including independent TS DTO/runtime validation and fixtures; shared legacy redirect helper under `app\lib\progress-redirect.ts`; existing `scorecard\page.tsx`, `tracking\page.tsx`, `layout.tsx`, home `page.tsx`; narrow `tasks\page.tsx` entry-context change and optional extracted `tasks-client.tsx`; scoped CSS module (or narrowly prefixed additions to `globals.css`); `source\apps\web\tests\progress*`, relevant week/navigation browser tests; web manifest/lockfile **only** for a justified test-tool addition. |
| **Parent/tester** | Integration/test evidence and acceptance review; parent owns final git actions. Neither engineer edits this design or the PRD without escalation. No file deletion, branch/worktree/session/factory or git publication is part of this handoff. |

Backend and web can start **in parallel after contract approval**: backend implements the §5 DTOs; web builds against the complete example plus independent fixtures for all states. Do not share a generated file across ownership boundaries. Contract changes are coordinated before either side silently changes casing/semantics.

Backend internal order: migration/sidecar initialization + owner serialization → staged commit + capture lifecycle/old-writer guard → projections/API → actual-store failure/restart tests. Web order: date/contract validation + fixtures → five-area page/accessibility → redirects/Tasks handoff → browser regressions. Integration follows both, with actual API fixture comparison. Capture readiness, not UI readiness, gates rollout.

## 9. Validation plan — all 38 criteria remain release gates

Use controllable `TimeProvider` for projection/service tests, never a production client `asOf` override. Use isolated temp sidecars and disposable Postgres schemas/databases. Existing service tests currently keep task/substep references: preserve successful publication semantics rather than weakening assertions. No installs/restores are needed for DESIGN; later manifests changed for SQLite justify the corresponding restore.

| Evidence | Required tests / AC mapping |
|---|---|
| Contract/navigation | HTTP strict 1/84/85-day, invalid/duplicate/omitted bounds and zone checks; exact casing/nulls and schema fixtures. Default 2026-10-06 → Sep 14–Oct 11, Previous Aug 17–Sep 13. Legacy redirects/default explanation plus unchanged Dashboard/legacy APIs. **AC-01–04** |
| Time projection | Kolkata 18:29:59Z/18:30Z split; UTC reproject; New York Mar 8 and Nov 1 2026 (both 01:30 instants), full 23/25h coverage; leap 2028/month/year and both date endpoints with different browser zones. **AC-05–06** |
| Mutation recognition | Through actual API routes: no-substep Done, first substep, final auto-Done, overrides; matrix of every non-activity path; no-op, optional-version conflicts, rejected DSA, two concurrent requests, response-loss retry, reopen/recomplete, terminal parents. Existing effort/AI/asset/workflow tests stay green. **AC-07–12** |
| Retention/no backfill | Rename then delete substep/task, bulk deletion and actual duplicate-healing path; new same-title identity; populated legacy Done/CompletedAt/status history yields no events. Check snapshots/links before and after fresh hydration. **AC-13–14** |
| Coverage | Start 10:00 vs midnight; positive partial day; no-visit/no-event days certified by hosted timer; interruption mid-day; certifier failure; today/future; open-view midnight refresh. **AC-15–17** |
| Detail/participation | Stable equal-instant ordering, >100 events over multiple pages with concurrent additions, removed targets, full/partial/unknown counts, empty reliable vs unavailable history. **AC-18–19** |
| Current weekly plan | OR membership including contradictory legacy fields, dedup IDs, no-day tasks, clipped activity range; 100/100/25 and zero-weight fallback; near-100 precision, Done override, terminal states; move/reopen/delete and empty-week handoff, default Tasks navigation regression. **AC-20–24** |
| Gaps and last activity | Latest-ending tie, weekends, clipped ends/unknown break, none vs insufficient; exact highlight/focus/list clearing; current gap independent of selected range, today resets zero, yesterday active/unknown, start-boundary lower bounds; latest tuple and View date. Include partial active date immediately before a full inactive suffix. **AC-25–30** |
| **Real persistence/restart** | API mutation → read → terminate child API process → launch **fresh process** on same backing → read again. Run for default InMemory+SQLite and actual Postgres, including zero-event owner, interruption, titles, owner start and dedup identity. A new store object in the old process is not sufficient. **AC-31** |
| **Failure atomicity** | Inject failures before/during event insert, aggregate save, coverage start/certification and commit; capture task references/HTTP responses before mutation and verify no dirty publication. Test committed-but-response-lost reconciliation; corrupt/delete existing sidecar, DB unavailable, initial-owner failure. No 200 empty success. **AC-32** |
| **Writer paths/migration/rollback** | Unrelated settings/metadata, aggregate replacement, targeted/bulk delete and cleanup then restart, two owners and two independent Postgres service processes. V9 clean/populated/repeated migration; old untagged aggregate writer preserves ledger and opens uncertainty; UI-only rollback preserves capture; suspend/old writes/resume, unannounced crash/rollback and first partial day. **AC-33–34** |
| Web reliability/ownership | Slow/failing/null/malformed responses, stale responses after range/day/zone/identity change, continuation retry and midnight; two-owner API checks for every event/count/gap/link and cursor mismatch. **AC-35–36** |
| Browser/manual/performance | Existing Playwright-style scripts, keyboard-only navigation, screen-reader names/status announcements, 320px/200% zoom, touch, light/dark, long names, list equivalence. No forbidden old metrics. 1,000 tasks/10,000 units, default **and** 84-day reports, 20 warm samples each, p95 ≤2s with environment/baseline and payload size recorded. **AC-37–38** |

Fresh-process InMemory tests must acknowledge volatile tasks: after restart history remains and absent links are null. To exercise same-identity recommit after restart, the **test harness only** restores a known task/substep fixture with those IDs, then completes through the actual HTTP mutation endpoint; do not insert ledger rows to fake recognition. Also prove a newly created same-title task gets a new identity/activity record. No test-only restore endpoint ships in production.

Prefer the existing test project plus a small justified `Microsoft.AspNetCore.Mvc.Testing` test dependency for HTTP/fault-injection tests; use actual child `dotnet` API processes for restart proof. Real Postgres tests are conditional on a dedicated supplied test connection—never use production. If unavailable, record **not run / release blocked for Postgres verification**, not passed via a mock. Run API Release build/tests, web node tests/build/lint with existing tooling; run browser scripts separately as today.

## 10. Rollout, rollback, risks, and open questions

1. Apply/test the additive versioned V9 via the existing migrator (IF NOT EXISTS objects, safe trigger creation/replacement, version registration; do not edit V1–V8). It creates no owner starts/events/backfill. Old schemas/payloads remain usable. No infra/Bicep/pipeline/Docker changes.
2. For InMemory, explicitly initialize and validate the persistent sidecar before upgraded API startup; record path/store identity/backup procedure. Pin/test SQLite native support on Windows and the current Linux runtime image.
3. Drain pre-capture writers before enabling capture; rolling overlap among **upgraded** replicas is supported by owner scopes, but pre-capture writers taint coverage. Complete any seed loading first. Start upgraded backend; establish capture before shipping the web/nav change. Verify both-store restart, failures and zero-event coverage.
4. Ship new UI and redirects only after the API contract/health gates pass. Observe existing logs for persistence/certifier/unknown-writer errors and bounded-report latency. Retain explicit start/partial coverage messaging; no inferred historical inactivity.
5. Preferred rollback is web-only, leaving compatible capture enabled and retained evidence intact. For backend rollback, drain/stop API processes and execute suspend before starting old writers. Never drop Progress tables, triggers, migration version or sidecar. On resume, stop every API process, restore upgraded binaries, close uncertainty with the offline maintenance command and start fresh processes/sessions.
6. If a rollback boundary is unknown, widen the interruption conservatively; Postgres trigger and finite session ends are fallback defenses, not permission to skip the procedure. Do not “repair” downtime by extending an old session to now. Prove retained records and unknown/partial boundary dates before re-exposure.

| Risk / tradeoff | Mitigation / release gate |
|---|---|
| Small additional SQLite dependency/setup; entire application is still volatile in default mode | Worth avoiding a custom crash-safe file protocol. One-time explicit init, stable local disk and clear missing-link behavior; no claim of durable tasks. |
| Existing global gate and remote aggregate writes can be slow | Retain simple locking first, batch hydration queries on one connection, cache by durable revision; measure the specified fixture. No generic concurrency/performance redesign without evidence. |
| Multi-replica stale cache or bypass writer | Owner scope + revision on **all** writers; real two-process tests. Untagged old task writes invalidate completeness, never infer events. |
| Conservative restart/old-writer exclusions reduce known inactive history | Intentional: positive records survive, false inactivity does not. Explain partial/unknown dates and lower bounds. |
| Disk/DB failure or uncertain commit | Stage-before-persist, durable receipt, fenced recovery; neither acknowledge unrecorded success nor publish phantom objects. |
| User expects repeat practice to count | First-recorded policy remains visible; do not change A1 or existing task progress. |
| Historical names persist after deletion | Minimal fields only, existing owner access, removed labels/no target links; no new retention/auth feature. |

**Open questions / blockers:** no unresolved product choice or PRD conflict. Before release, the operator/tester must supply (1) a persistent writable directory for each supported non-Postgres deployment, (2) a disposable real Postgres test connection, and (3) confirmation that old writers can be drained for rollout/resume. These have **not** been provisioned or validated in DESIGN. If local persistence is only ephemeral, or real Postgres restart/rollback evidence is unavailable, escalate the concrete gate; do not ship a Postgres-only/volatile substitute or silently expand infrastructure scope.
