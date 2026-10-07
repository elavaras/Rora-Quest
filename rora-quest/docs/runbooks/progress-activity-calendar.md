# Progress activity calendar: operating and validating the backend

See the [PRD](../prd/progress-activity-calendar.md) and [approved design](../architecture/progress-activity-calendar.md).
The wire contract is design §5. This runbook does not redefine the legacy task, Dashboard, Scorecard, or Tracking APIs.

## What is durable

- Only the first accepted false→true substep completion, or non-Done→Done transition of a task **without substeps**, is recorded.
- Stable task/substep IDs, completion-time names, UTC microsecond instants, owner starts, finite capture certificates, interruptions, permanent deduplication keys, and mutation receipts are retained.
- Reopen/recomplete never adds another event for that identity. Parent automatic completion is not a second unit. Imports, creation as Done, edits, weights, confidence, schedules and deletions are not activity.
- Names survive deletion. A missing task **or missing substep** has no task link. The ledger does not reconstruct task content.
- InMemory still has **volatile tasks**. Its SQLite sidecar durably stores **Progress only**; task links can disappear after restart. PostgreSQL persists tasks and Progress together.
- The service keeps its existing instance gate. Every owner load/write also takes a scoped store lock. PostgreSQL uses owner advisory locks and durable aggregate revisions for independent-process cache invalidation; task completion, evidence, certification and receipts share one transaction.
- Mutations stage detached task/substep/status-event state before I/O. Known rollback never updates already-returned live task/substep references. An uncertain commit is resolved by retained receipt under a reacquired owner fence. If reconciliation is unavailable, that owner's task/report access remains unavailable until restart/reconciliation, never a definitive stale result.

## InMemory installation: explicit offline initialization

Requirements: writable **persistent local filesystem**, outside any checkout, build output or release directory. An ephemeral container filesystem is **not supported**. Do not deploy without durable storage.

Default: `<LocalApplicationData>\RoraQuest\Progress`. Override with an absolute `Progress:DataDirectory` / `Progress__DataDirectory`.

From `rora-quest\source\apps\api` after building:

```powershell
$env:Progress__DataDirectory = 'D:\RoraQuestData\Progress'
dotnet .\src\RoraQuest.Api\bin\Release\net8.0\RoraQuest.Api.dll --progress-store-init
dotnet .\src\RoraQuest.Api\bin\Release\net8.0\RoraQuest.Api.dll
```

The init command is offline and non-destructive. It registers a new store identity and creates the schema transactionally. It rejects an existing initialized schema, conflicting/unregistered database, or unrelated contents. If interrupted before schema commit, rerunning against the same registration can finish initialization; never overwrite a conflicting registration.

Ordinary startup **cannot create** a missing database/directory. It validates registration/schema, SQLite integrity, foreign keys and retained owner/session/event counters. A missing/corrupt store is an operational error, not empty history; there is no volatile Progress fallback. `process.lock` is held with exclusive file sharing for the entire API lifetime. A second API using the same directory fails startup. SQLite uses `synchronous=FULL`, foreign keys, and rollback-journal mode.

Do not delete/reinitialize a failed store. Preserve it, stop the application and restore a verified complete backup. Tests use separate temp directories; they never reuse an operator's default sidecar.

## PostgreSQL migration and rollout

1. Back up the database, drain and stop **all** pre-capture writers. Mixed old/new writers are not a supported rollout mode.
2. Configure the existing `ConnectionStrings:Postgres`; do not configure a sidecar for this store.
3. Apply through the existing `DatabaseMigrator` at upgraded startup. `V9__progress_activity.sql` follows V1–V8, runs transactionally, and records V9. The API image already includes the migration directory. Set `Postgres:MigrationsPath` if auto-discovery is unavailable.
4. V9 creates `progress_owners`, `progress_completions`, `progress_mutation_receipts`, `progress_capture_sessions`, and `progress_capture_interruptions`, plus indexes and legacy-writer triggers. It creates **no historical events or owner starts**. Retained tables deliberately have no task/category/substep cascade.
5. Finish seed operations **before** capture starts. After capture, an untagged seed/task writer taints coverage just like an old binary.
6. Start upgraded writers only. Readiness starts each process's own finite capture session. Ensure the normal health endpoint and a scoped Progress request succeed; confirm new completion→report and no-op/recompletion behavior before opening traffic.

New writer transactions set transaction-local `roraquest.progress_capture_version='1'`. BEFORE STATEMENT triggers on `task_items` and `task_sub_steps` serialize with capture-control advisory lock `726578430019`. Untagged INSERT/UPDATE/DELETE remains allowed, but opens/widens an `unrecognizedWriter` interruption to the earliest known capture session. No sessions yet means no earlier reliable history to taint. The tag is a consistency protocol, **not authorization**.

Do not remove V9 tables, receipts, keys, or triggers on rollback. Do not cascade a task/category deletion into Progress. Every aggregate/targeted writer increments `aggregate_revision`. Current reports bypass cached aggregates while suspension is open.

## Finite coverage, failures and shutdown

Each healthy process certifies independently every 30 seconds. Reports and accepted mutations certify transactionally through their authoritative instant. A delay of **more than 90 seconds**, failed certification, crash, suspension or restart starts a new interval; no unproved time is bridged. Stored certification and process clocks must not move backwards.

The owner tracking start is its first successful scoped access, not deployment/account creation. Full inactivity requires a completely elapsed local day covered midnight-to-midnight. Unknown/partial days break gaps; positive events remain positive even after a coverage correction. Today's positive activity resets the current gap; otherwise today is never a full inactive day. No daily adherence or historical work is inferred from schedules or task metadata.

Normal shutdown best-effort certifies the final healthy boundary. Hard termination retains only the previous finite certificate. Certification failures are logged. Progress errors return `503` with the fixed `progressUnavailable` body, not a successful zero report. Legacy mutation persistence failures continue through the existing non-2xx error path.

## Capture suspension, old backend rollback, and recovery

A **UI-only rollback** can leave upgraded backend capture running. For an old backend rollback:

1. Drain requests and stop **all API writers** (including other replicas and independent local processes).
2. Run the upgraded executable offline using the same database/sidecar configuration:

   ```powershell
   dotnet .\src\RoraQuest.Api\bin\Release\net8.0\RoraQuest.Api.dll --progress-capture-suspend --confirm-writers-stopped
   ```

3. Record the printed persisted operation ID/boundary. If the command fails, **do not start an old writer**. Suspension never extends a stopped certificate.
4. Run the old backend only while the interruption remains open. Retained events remain readable by upgraded code, but current completeness is not asserted. Upgraded completion mutations fail closed while suspended.
5. Before upgrading again, drain and stop **every old and upgraded writer**, then:

   ```powershell
   dotnet .\src\RoraQuest.Api\bin\Release\net8.0\RoraQuest.Api.dll --progress-capture-resume --confirm-writers-stopped
   ```

6. Restart upgraded processes with fresh aggregate caches. The command-to-readiness interval remains unknown. Startup never automatically clears suspension.

For a forgotten rollback, unsafe restore or uncertain write period, widen the exclusion, never backfill reliable coverage:

```powershell
dotnet .\src\RoraQuest.Api\bin\Release\net8.0\RoraQuest.Api.dll --progress-capture-suspend --confirm-writers-stopped --unknown-since 2026-10-01T00:00:00Z
# After repair, still offline:
dotnet .\src\RoraQuest.Api\bin\Release\net8.0\RoraQuest.Api.dll --progress-capture-resume --confirm-writers-stopped
```

Choose the earliest possibly unsafe instant. If that cannot be established, use the earliest retained capture-session start (`SELECT min(started_at) FROM progress_capture_sessions`); do not guess a later cutoff. The PostgreSQL legacy trigger automatically uses this conservative earliest boundary. Explicit recovery instants can only widen an open interruption. An old InMemory binary cannot extend the upgraded sidecar's finite certificates; never run overlapping independent volatile task APIs.

## Backup/restore

- Stop all InMemory API processes and copy the **entire** registered sidecar directory together (database, registration and any journal). Keep its identity; never pair a database with another registration.
- PostgreSQL backup must include all retained Progress tables, identity sequence state and task data in a consistent database backup.
- Keep completion keys and receipts forever under the feature's retention contract.
- After a possibly stale restore, apply a conservative `--unknown-since` before reopening capture.
- **Lost acknowledged completion records are not repairable by coverage flags.** Restore a complete backup or block release. Do not invent events or reset the history.

## Validation commands and isolated integration tests

From `rora-quest\source\apps\api`:

```powershell
dotnet build .\RoraQuest.sln -c Release
dotnet test .\tests\RoraQuest.Api.Tests\RoraQuest.Api.Tests.csproj -c Release --no-build --no-restore
```

Existing workflow, effort, asset and AI-review tests retain their assertions and reference-behavior checks. Their fixture now explicitly initializes isolated **real SQLite** sidecars.

New tests cover strict HTTP grammar/casing/nulls/no-store, 1/84/85-day bounds and year endpoints, IANA zones/DST/leap/year transitions, actual status/substep endpoints, owner isolation, 100-row snapshot paging, weighted current weeks, gap ties/clipping/global scope, failure boundaries, receipt reconciliation/fencing, finite certification, deletion/duplicate cleanup, and **fresh child-process** restart. A separate test-only executable can restore volatile task fixture IDs before startup to prove durable dedup through actual mutation routes; it is not referenced by or shipped as the production API, and adds no production restore endpoint.

### Real PostgreSQL (required release gate)

Supply **only** `RORAQUEST_PROGRESS_TEST_POSTGRES` through your secure local test environment, pointing to a dedicated disposable database with CREATE SCHEMA permissions. Never point it at production. The tests ignore the normal application connection configuration, create unique `progress_test_<guid>` schemas, and retain them for diagnostics (operator owns cleanup). No credentials are printed or written into source.

```powershell
dotnet test .\tests\RoraQuest.Api.Tests\RoraQuest.Api.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~ProgressPostgresTests
```

Tests exercise real migrations (clean/populated/repeated), two independent child API processes, restart/hydration, owner revision cache invalidation, atomic event+aggregate rollback, lost-commit receipts, settings/deletion/cleanup writers and untagged-writer taint. If the dedicated variable is absent, they are explicitly **skipped / not run**, not counted as mock evidence. PostgreSQL deployment remains gated on their real execution.

### Performance fixture

```powershell
$env:RORAQUEST_PROGRESS_RUN_PERFORMANCE = '1'
dotnet test .\tests\RoraQuest.Api.Tests\RoraQuest.Api.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~ProgressPerformanceTests --logger 'console;verbosity=normal'
```

The opt-in test creates 1,000 tasks and 10,000 retained units via actual service mutations against SQLite, then samples 20 warmed HTTP responses each for the default and 84-day reports. It prints environment, fixture time, median/p95 latency and response bytes and asserts p95 ≤2 seconds, exact 10,000-unit count and 100-row page bound. This is a local backend baseline, not an internet-latency guarantee or PostgreSQL performance claim.

### Implementation validation record

Validation is recorded at implementation handoff; independent tester acceptance review and frontend/browser evidence remain separate. Linux SQLite native loading and real PostgreSQL must be validated in their deployment environments. Restore initially encountered nuget.org TLS `HandshakeFailure`; a temporary config using Microsoft's `dotnet-public` mirror restored pinned packages without changing repository NuGet configuration or TLS verification.

**2026-10-06 backend handoff results**

Executed from `C:\Workspaces\Rora-Quest\rora-quest\source\apps\api`:

```powershell
dotnet build .\RoraQuest.sln -c Release
$env:RORAQUEST_PROGRESS_RUN_PERFORMANCE = '1'
dotnet test .\RoraQuest.sln -c Release --no-build --no-restore --logger 'console;verbosity=detailed'
```

- Full Release solution build: **passed, 0 errors, 6 NU1900 warnings**. Warnings are failed nuget.org vulnerability-feed retrieval, not suppressed checks or C# build errors.
- Backend suite with performance enabled: **102 total, 99 passed, 3 skipped**, 2.9726 minutes. The 35 pre-existing tests remain included with their original assertions.
- The **three skipped tests are real PostgreSQL integration tests**. `RORAQUEST_PROGRESS_TEST_POSTGRES` was not supplied. No real PostgreSQL persistence/migration/multi-replica claim is made; this remains a release-validation gate.
- Actual production child API restart, test-only same-identity restore/recompletion, offline CLI initialization/suspend/resume, SQLite corruption/missing-storage, rollback, and ambiguous-commit receipt tests passed.
- `git diff --check` for backend/migration/runbook paths: passed.

Performance baseline: Windows NT 10.0.26100.0, .NET 8.0.31, 16 logical CPUs; ASP.NET TestServer HTTP transport, real SQLite sidecar. Fixture generation through service mutations took 2m55.08s; report samples exclude fixture construction.

| Report | Warm samples | Median | p95 | JSON bytes |
|---|---:|---:|---:|---:|
| Default four weeks | 20 | 37.47 ms | **52.37 ms** | 397,008 |
| 84 days, Jul 20–Oct 11 | 20 | 34.34 ms | **36.44 ms** | 407,257 |

Both backend p95 measurements passed the ≤2s target. These are not browser, network, PostgreSQL, or Linux-container measurements.
