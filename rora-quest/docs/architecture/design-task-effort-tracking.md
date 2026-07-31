# Technical Design: Task Effort Tracking

**Feature:** Task Effort Tracking
**Status:** Approved for implementation
**Date:** 2026-07-31
**Author:** Architect Agent

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Data Model Changes](#2-data-model-changes)
3. [SQL Migration](#3-sql-migration)
4. [API Contract](#4-api-contract)
5. [Service Layer Changes](#5-service-layer-changes)
6. [Frontend Component Changes](#6-frontend-component-changes)
7. [Postgres Store Changes](#7-postgres-store-changes)
8. [Migration Considerations](#8-migration-considerations)
9. [Testing Strategy](#9-testing-strategy)

---

## 1. Architecture Overview

### Context

Rora-Quest follows a deliberately consolidated architecture: all backend logic — entity models, DTOs, service methods, and endpoint mappings — lives in a single file (`ApiEndpoints.cs`, ~85 KB). Persistence is abstracted behind `IRoraQuestStore`, with two implementations:

- `InMemoryRoraQuestStore` — default; no schema; fields are simply properties on the in-memory `TaskItem` object.
- `PostgresRoraQuestStore` — Dapper + `NpgsqlDataSource`; reads/writes through a `TaskRow` struct and raw SQL.

The frontend is a Next.js 14 "use client" app. The task detail page (`/tasks/[id]/page.tsx`) owns its own TypeScript `TaskItem` type, fetches from `GET /api/tasks/{id}`, and patches via `PATCH /api/tasks/{id}`.

### How This Feature Fits In

Task Effort Tracking adds three optional scalar fields (`estimatedHours`, `actualHours`, `storyPoints`) that flow through the full stack in a narrow, additive path:

```
PostgreSQL column (V7 migration)
        │
        ▼
TaskRow struct  ──► HydrateFromDb SELECT
        │
        ▼
TaskItem C# class  ──► RoraQuestService (Create / Update / Get)
        │
        ▼
Response JSON  ──► GET /api/tasks/{id}  &  GET /api/tasks (list)
        ▲
        │
PATCH /api/tasks/{id}  ◄──  UpdateTaskRequest DTO
        ▲
        │
Frontend TaskItem TS type
        │
  state variables  ──►  applyTaskToView()  ──►  saveMeta()  ──►  UI card
```

No new files, classes, controllers, or service abstractions are required. The change is purely additive at every layer.

---

## 2. Data Model Changes

### 2.1 PostgreSQL Columns

Three new nullable columns are added to the `task_items` table:

| Column | Type | Constraint | Description |
|---|---|---|---|
| `estimated_hours` | `NUMERIC(6,2)` | `NULL`, `CHECK >= 0` | Planned effort in hours |
| `actual_hours` | `NUMERIC(6,2)` | `NULL`, `CHECK >= 0` | Effort spent in hours |
| `story_points` | `INTEGER` | `NULL`, `CHECK >= 0` | Relative planning unit |

`NUMERIC(6,2)` allows values from `0.00` to `9999.99` — more than sufficient for task-level effort estimates.

### 2.2 C# `TaskItem` Class (in `ApiEndpoints.cs`)

Add three nullable properties to the `TaskItem` class:

```csharp
// ── NEW: Effort Tracking ──────────────────────────────────────────
public decimal? EstimatedHours { get; set; }
public decimal? ActualHours    { get; set; }
public int?     StoryPoints    { get; set; }
```

**Placement:** append after the existing `Spillovers` property (or any logical grouping boundary). Because the class uses simple property assignment, no constructor changes are needed.

### 2.3 `TaskRow` Dapper Struct (in `ApiEndpoints.cs`)

Add three fields to mirror the new columns:

```csharp
// ── NEW: Effort Tracking ──────────────────────────────────────────
public decimal? EstimatedHours { get; init; }
public decimal? ActualHours    { get; init; }
public int?     StoryPoints    { get; init; }
```

Dapper maps column names case-insensitively; `estimated_hours` → `EstimatedHours` resolves automatically when using the `DefaultTypeMap.MatchNamesWithUnderscores = true` convention, or via explicit column aliasing in the SELECT (see §7).

### 2.4 `UpdateTaskRequest` DTO

Add three nullable fields to support optional PATCH semantics (null = do not update):

```csharp
public decimal? EstimatedHours { get; set; }
public decimal? ActualHours    { get; set; }
public int?     StoryPoints    { get; set; }
```

### 2.5 `CreateTaskRequest` DTO

Add three nullable fields for optional inclusion at creation time:

```csharp
public decimal? EstimatedHours { get; set; }
public decimal? ActualHours    { get; set; }
public int?     StoryPoints    { get; set; }
```

---

## 3. SQL Migration

**File:** `rora-quest/source/apps/api/src/RoraQuest.Api/Migrations/V7__task_effort_tracking.sql`

```sql
-- V7__task_effort_tracking.sql
-- Adds optional effort-tracking columns to task_items.
-- All columns are nullable with no server-side default, ensuring:
--   1. Zero-downtime deployment (no table rewrite, no lock escalation on large tables)
--   2. Full backward compatibility (existing rows read as NULL)

ALTER TABLE task_items
    ADD COLUMN IF NOT EXISTS estimated_hours NUMERIC(6,2) NULL,
    ADD COLUMN IF NOT EXISTS actual_hours    NUMERIC(6,2) NULL,
    ADD COLUMN IF NOT EXISTS story_points    INTEGER      NULL;

-- Non-negative value constraints (applied after column creation for clarity)
ALTER TABLE task_items
    ADD CONSTRAINT chk_task_estimated_hours_non_negative
        CHECK (estimated_hours IS NULL OR estimated_hours >= 0),
    ADD CONSTRAINT chk_task_actual_hours_non_negative
        CHECK (actual_hours IS NULL OR actual_hours >= 0),
    ADD CONSTRAINT chk_task_story_points_non_negative
        CHECK (story_points IS NULL OR story_points >= 0);

COMMENT ON COLUMN task_items.estimated_hours IS 'Planned effort in hours (decimal, optional)';
COMMENT ON COLUMN task_items.actual_hours    IS 'Actual effort spent in hours (decimal, optional)';
COMMENT ON COLUMN task_items.story_points    IS 'Relative planning unit (integer, optional)';
```

> **Note on `ADD COLUMN IF NOT EXISTS`:** Supported in PostgreSQL 9.6+. This makes the migration idempotent — safe to re-run if a partial migration occurred.

> **Note on CHECK constraints:** The `IS NULL OR ...` pattern lets NULLs pass the check while still rejecting negative numbers when a value is supplied.

---

## 4. API Contract

### 4.1 `PATCH /api/tasks/{id}` — Update Task

**UpdateTaskRequest — Before:**
```json
{
  "title": "string | null",
  "description": "string | null",
  "status": "string | null",
  "priority": "string | null",
  "difficulty": "string | null",
  "pattern": "string | null",
  "plannedDate": "date | null",
  "dueDate": "date | null",
  "categoryId": "guid | null",
  "subCategoryId": "guid | null",
  "assignedTo": "string | null",
  "logicNotes": "string | null",
  "algorithmNotes": "string | null",
  "diagramContent": "string | null",
  "questionAndReasoning": "string | null"
}
```

**UpdateTaskRequest — After (additions highlighted):**
```json
{
  "title": "string | null",
  "description": "string | null",
  "status": "string | null",
  "priority": "string | null",
  "difficulty": "string | null",
  "pattern": "string | null",
  "plannedDate": "date | null",
  "dueDate": "date | null",
  "categoryId": "guid | null",
  "subCategoryId": "guid | null",
  "assignedTo": "string | null",
  "logicNotes": "string | null",
  "algorithmNotes": "string | null",
  "diagramContent": "string | null",
  "questionAndReasoning": "string | null",
  "estimatedHours": "number | null",   // NEW — decimal, e.g. 2.5
  "actualHours":    "number | null",   // NEW — decimal, e.g. 1.75
  "storyPoints":    "integer | null"   // NEW — integer, e.g. 3
}
```

**PATCH semantics:**  A field present with a value updates that field. A field absent (or explicitly `null`) leaves the existing DB value unchanged. This matches the existing pattern used by all other nullable fields on `UpdateTaskRequest`.

**Validation (enforced in service layer):**
- `estimatedHours` must be `>= 0` when provided.
- `actualHours` must be `>= 0` when provided.
- `storyPoints` must be `>= 0` when provided.

Returns `400 Bad Request` with a descriptive message on constraint violation.

---

### 4.2 `GET /api/tasks/{id}` — Get Task (response)

**Before:**
```json
{
  "id": "guid",
  "title": "string",
  "status": "string",
  "difficulty": "string | null",
  "pattern": "string | null",
  ...
}
```

**After (additions highlighted):**
```json
{
  "id": "guid",
  "title": "string",
  "status": "string",
  "difficulty": "string | null",
  "pattern": "string | null",
  ...
  "estimatedHours": null,    // NEW — number | null
  "actualHours":    null,    // NEW — number | null
  "storyPoints":    null     // NEW — integer | null
}
```

> Existing tasks will have all three fields as `null`. No breaking change for clients that ignore unknown/new fields.

---

### 4.3 `GET /api/tasks?...` — List Tasks (response)

The same three fields appear on every item in the list response array. Clients that only display summary information can ignore them.

---

## 5. Service Layer Changes

All changes are confined to `RoraQuestService` in `ApiEndpoints.cs`.

### 5.1 `CreateTask` Method

**Before (relevant excerpt):**
```csharp
var task = new TaskItem
{
    Id          = Guid.NewGuid(),
    UserId      = userId,
    Title       = request.Title,
    // ... other fields ...
    Difficulty  = request.Difficulty,
    Pattern     = request.Pattern,
};
```

**After:**
```csharp
var task = new TaskItem
{
    Id             = Guid.NewGuid(),
    UserId         = userId,
    Title          = request.Title,
    // ... other fields ...
    Difficulty     = request.Difficulty,
    Pattern        = request.Pattern,
    // ── NEW: Effort Tracking ──
    EstimatedHours = request.EstimatedHours,
    ActualHours    = request.ActualHours,
    StoryPoints    = request.StoryPoints,
};
```

Add optional pre-save validation:
```csharp
if (request.EstimatedHours is < 0)
    return Results.BadRequest("estimatedHours must be >= 0.");
if (request.ActualHours is < 0)
    return Results.BadRequest("actualHours must be >= 0.");
if (request.StoryPoints is < 0)
    return Results.BadRequest("storyPoints must be >= 0.");
```

### 5.2 `UpdateTask` Method

**Before (relevant excerpt showing existing nullable-field update pattern):**
```csharp
if (request.Difficulty is not null)  task.Difficulty = request.Difficulty;
if (request.Pattern    is not null)  task.Pattern    = request.Pattern;
```

**After:**
```csharp
if (request.Difficulty is not null)  task.Difficulty = request.Difficulty;
if (request.Pattern    is not null)  task.Pattern    = request.Pattern;
// ── NEW: Effort Tracking ──
if (request.EstimatedHours.HasValue) task.EstimatedHours = request.EstimatedHours;
if (request.ActualHours.HasValue)    task.ActualHours    = request.ActualHours;
if (request.StoryPoints.HasValue)    task.StoryPoints    = request.StoryPoints;
```

Add validation before the field assignments:
```csharp
if (request.EstimatedHours is < 0)
    return Results.BadRequest("estimatedHours must be >= 0.");
if (request.ActualHours is < 0)
    return Results.BadRequest("actualHours must be >= 0.");
if (request.StoryPoints is < 0)
    return Results.BadRequest("storyPoints must be >= 0.");
```

> **Design note on null-update semantics:** Using `.HasValue` rather than `is not null` makes the intent explicit — the caller can distinguish "omitted field" (not present in JSON → null on DTO) from "field present with value." Since C# `decimal?` and `int?` are value types, `.HasValue` is equivalent to `is not null`, but the intent is clearer.

### 5.3 `GetTask` Projection / Response Mapping

If `TaskItem` is returned directly as the JSON response (System.Text.Json serialization), no explicit mapping change is needed — the new properties will serialize automatically.

If a projection/response DTO is used, add the three fields to the projection:
```csharp
EstimatedHours = task.EstimatedHours,
ActualHours    = task.ActualHours,
StoryPoints    = task.StoryPoints,
```

### 5.4 `InMemoryRoraQuestStore`

No changes needed. The in-memory store stores `TaskItem` objects by reference. New properties are simply `null` on objects loaded before the feature ships, and set/read normally afterward.

---

## 6. Frontend Component Changes

**File:** `rora-quest/source/apps/web/src/app/tasks/[id]/page.tsx`

### 6.1 TypeScript `TaskItem` Type

Append three nullable fields to the existing inline type definition:

```typescript
// Before (excerpt)
type TaskItem = {
  id: string;
  title: string;
  status: string;
  difficulty?: string | null;
  pattern?: string | null;
  // ... other fields ...
};

// After (additions)
type TaskItem = {
  id: string;
  title: string;
  status: string;
  difficulty?: string | null;
  pattern?: string | null;
  // ... other fields ...
  // ── NEW: Effort Tracking ──
  estimatedHours?: number | null;
  actualHours?: number | null;
  storyPoints?: number | null;
};
```

### 6.2 State Variables

Add three state variables alongside the existing pattern/difficulty states:

```typescript
// Existing pattern:
const [pattern,    setPattern]    = useState<string | null>(null);
const [difficulty, setDifficulty] = useState<string | null>(null);

// NEW: Effort Tracking
const [estimatedHours, setEstimatedHours] = useState<number | null>(null);
const [actualHours,    setActualHours]    = useState<number | null>(null);
const [storyPoints,    setStoryPoints]    = useState<number | null>(null);
```

### 6.3 `applyTaskToView` Function

Hydrate the new state variables from the fetched task:

```typescript
function applyTaskToView(task: TaskItem) {
  // ... existing field assignments ...
  setPattern(task.pattern ?? null);
  setDifficulty(task.difficulty ?? null);
  // ── NEW: Effort Tracking ──
  setEstimatedHours(task.estimatedHours ?? null);
  setActualHours(task.actualHours ?? null);
  setStoryPoints(task.storyPoints ?? null);
}
```

### 6.4 `saveMeta` Function

Include the three new fields in the PATCH body:

```typescript
async function saveMeta() {
  await fetch(`/api/tasks/${task.id}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      // ... existing fields ...
      pattern,
      difficulty,
      // ── NEW: Effort Tracking ──
      estimatedHours,
      actualHours,
      storyPoints,
    }),
  });
}
```

### 6.5 UI Rendering — "Effort Tracking" Card

Add a new card in the task detail layout. Recommended placement: below the existing "Task Info" card on the right column, or as a dedicated third card spanning the full width.

```tsx
{/* ── Effort Tracking Card ─────────────────────────────────── */}
<div className="rounded-lg border border-border bg-card p-4 space-y-4">
  <h3 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">
    Effort Tracking
  </h3>

  <div className="grid grid-cols-3 gap-4">

    {/* Estimated Hours */}
    <div className="flex flex-col gap-1">
      <label className="text-xs text-muted-foreground">Estimated Hours</label>
      <input
        type="number"
        min={0}
        step={0.25}
        placeholder="e.g. 4.0"
        value={estimatedHours ?? ''}
        onChange={(e) =>
          setEstimatedHours(e.target.value === '' ? null : parseFloat(e.target.value))
        }
        onBlur={saveMeta}
        className="rounded border border-border bg-background px-2 py-1 text-sm"
      />
    </div>

    {/* Actual Hours */}
    <div className="flex flex-col gap-1">
      <label className="text-xs text-muted-foreground">Actual Hours</label>
      <input
        type="number"
        min={0}
        step={0.25}
        placeholder="e.g. 2.5"
        value={actualHours ?? ''}
        onChange={(e) =>
          setActualHours(e.target.value === '' ? null : parseFloat(e.target.value))
        }
        onBlur={saveMeta}
        className="rounded border border-border bg-background px-2 py-1 text-sm"
      />
    </div>

    {/* Story Points */}
    <div className="flex flex-col gap-1">
      <label className="text-xs text-muted-foreground">Story Points</label>
      <input
        type="number"
        min={0}
        step={1}
        placeholder="e.g. 3"
        value={storyPoints ?? ''}
        onChange={(e) =>
          setStoryPoints(e.target.value === '' ? null : parseInt(e.target.value, 10))
        }
        onBlur={saveMeta}
        className="rounded border border-border bg-background px-2 py-1 text-sm"
      />
    </div>

  </div>
</div>
```

**UX pattern notes:**
- `onBlur={saveMeta}` matches the existing autosave-on-blur pattern used by pattern/difficulty fields — no explicit "Save" button needed.
- Empty input maps to `null` (field cleared/unset) which the API interprets as "do not update" (PATCH semantics). If the intent is to explicitly clear a field, the service layer `UpdateTask` could be extended to support an explicit null-clear semantic; for now, clearing is handled by setting the value to `null` and relying on the fact that `null` in the PATCH body means "no change."
- `step={0.25}` for hours provides convenient quarter-hour increments.

---

## 7. Postgres Store Changes

All changes are in `PostgresRoraQuestStore` within `ApiEndpoints.cs`.

### 7.1 `HydrateFromDb` SELECT Query

Add the three columns to every SELECT that reads from `task_items`.

**Before (excerpt):**
```sql
SELECT
    id, user_id, title, description, category_id, sub_category_id,
    planned_week_start, planned_date, pattern, difficulty, assigned_to,
    priority, status, due_date, start_at, end_at, calendar_event_id,
    reminder_at, question_and_reasoning, logic_notes, algorithm_notes,
    diagram_content, created_at, updated_at, row_version
FROM task_items
WHERE ...
```

**After:**
```sql
SELECT
    id, user_id, title, description, category_id, sub_category_id,
    planned_week_start, planned_date, pattern, difficulty, assigned_to,
    priority, status, due_date, start_at, end_at, calendar_event_id,
    reminder_at, question_and_reasoning, logic_notes, algorithm_notes,
    diagram_content, created_at, updated_at, row_version,
    estimated_hours,   -- NEW
    actual_hours,      -- NEW
    story_points       -- NEW
FROM task_items
WHERE ...
```

> Apply this change to all query sites: single-task GET, list query, and any sub-query that materialises a full `TaskRow`.

### 7.2 `TaskRow` Struct Mapping

Add the three fields. Dapper maps `estimated_hours` → `EstimatedHours` if `DefaultTypeMap.MatchNamesWithUnderscores = true` is set in `Program.cs` or at the store's constructor. If not, use explicit column aliases in the SELECT (`estimated_hours AS EstimatedHours`).

```csharp
// ── NEW: Effort Tracking ──
public decimal? EstimatedHours { get; init; }
public decimal? ActualHours    { get; init; }
public int?     StoryPoints    { get; init; }
```

**Mapping in `HydrateFromDb` (TaskRow → TaskItem):**
```csharp
EstimatedHours = row.EstimatedHours,
ActualHours    = row.ActualHours,
StoryPoints    = row.StoryPoints,
```

### 7.3 `SaveAsync` — INSERT and UPDATE

**INSERT (CreateTask path):**

```sql
-- Before (excerpt, last columns)
INSERT INTO task_items (
    ..., pattern, difficulty, ...
) VALUES (
    ..., @Pattern, @Difficulty, ...
)
```

```sql
-- After
INSERT INTO task_items (
    ..., pattern, difficulty, ...,
    estimated_hours, actual_hours, story_points   -- NEW
) VALUES (
    ..., @Pattern, @Difficulty, ...,
    @EstimatedHours, @ActualHours, @StoryPoints   -- NEW
)
```

And in the anonymous parameter object passed to Dapper:
```csharp
new {
    // ... existing params ...
    EstimatedHours = task.EstimatedHours,
    ActualHours    = task.ActualHours,
    StoryPoints    = task.StoryPoints,
}
```

**UPDATE (UpdateTask path):**

```sql
-- Before (excerpt)
UPDATE task_items SET
    title            = @Title,
    pattern          = @Pattern,
    difficulty       = @Difficulty,
    ...
    updated_at       = @UpdatedAt
WHERE id = @Id AND row_version = @RowVersion
```

```sql
-- After
UPDATE task_items SET
    title            = @Title,
    pattern          = @Pattern,
    difficulty       = @Difficulty,
    ...
    estimated_hours  = @EstimatedHours,   -- NEW
    actual_hours     = @ActualHours,      -- NEW
    story_points     = @StoryPoints,      -- NEW
    updated_at       = @UpdatedAt
WHERE id = @Id AND row_version = @RowVersion
```

And in the parameter object:
```csharp
new {
    // ... existing params ...
    EstimatedHours = task.EstimatedHours,
    ActualHours    = task.ActualHours,
    StoryPoints    = task.StoryPoints,
    // ...
}
```

> **Important:** The UPDATE statement writes the full current value of each field (whether changed or not). The PATCH semantics (`HasValue` check) happen in the service layer before `SaveAsync` is called — by the time the store's `Save()` is invoked, `task.EstimatedHours` already holds the correct new value (or the unchanged old value). No conditional SQL is required in the store.

---

## 8. Migration Considerations

### 8.1 Zero-Downtime Deployment

| Concern | Mitigation |
|---|---|
| Column addition locks table | PostgreSQL `ADD COLUMN` with `NULL` default (no `NOT NULL` + no `DEFAULT` expression) is a metadata-only operation and does not rewrite the table — effectively instant even on large tables. |
| Old app version + new schema | Old code ignores unknown columns in SELECT (Dapper maps only declared fields). Old INSERT/UPDATE do not include the new columns — NULLs are stored, which is the correct default. |
| New app version + old schema | Will fail to SELECT/INSERT the new columns until migration runs. Standard deploy order: run migration first, then deploy app. |

**Recommended deploy order:**
1. Run `V7__task_effort_tracking.sql` against the production database.
2. Deploy the updated backend (ApiEndpoints.cs changes).
3. Deploy the updated frontend.

Steps 2 and 3 can be swapped — the new frontend fields simply return null/undefined until the backend is deployed.

### 8.2 In-Memory Store

No migration or code path needed. The `InMemoryRoraQuestStore` stores `TaskItem` objects directly. New nullable properties default to `null` on creation; they are set and read like all other properties.

### 8.3 Backward Compatibility

- **GET responses:** All existing clients receive three new nullable fields (`estimatedHours`, `actualHours`, `storyPoints`). Standard REST practice — additive fields are not breaking changes for well-written clients.
- **PATCH requests:** Fields not sent in the JSON body remain unchanged in the database. Existing clients that do not know about the new fields will simply never set them, which is the correct behavior.
- **Existing data:** All existing rows will have `NULL` for the three new columns after migration. This is indistinguishable from a user who simply never filled in effort fields — semantically correct.

### 8.4 Constraint Safety

The `CHECK` constraints on the new columns only apply to `INSERT` and `UPDATE` statements. Since the migration adds them after adding the columns (with `ADD CONSTRAINT`), PostgreSQL validates existing rows at constraint-addition time. Because all existing rows are NULL for these columns and the check is `IS NULL OR value >= 0`, all existing rows pass — no backfill or data cleanup needed.

---

## 9. Testing Strategy

### 9.1 Backend Unit Tests (Service Layer)

Test file pattern: `RoraQuest.Api.Tests` (or equivalent xUnit/NUnit project).

**CreateTask with effort fields:**
```
Given: CreateTaskRequest with estimatedHours=4.5, actualHours=2.0, storyPoints=3
When:  CreateTask is called
Then:  Returned TaskItem has EstimatedHours=4.5, ActualHours=2.0, StoryPoints=3
```

**CreateTask with null effort fields (default):**
```
Given: CreateTaskRequest with no effort fields set
When:  CreateTask is called
Then:  Returned TaskItem has EstimatedHours=null, ActualHours=null, StoryPoints=null
```

**UpdateTask — partial update (only estimatedHours):**
```
Given: Existing task with EstimatedHours=4.0, ActualHours=2.0, StoryPoints=3
  And: UpdateTaskRequest with estimatedHours=6.0 (others absent)
When:  UpdateTask is called
Then:  Task has EstimatedHours=6.0, ActualHours=2.0, StoryPoints=3 (unchanged)
```

**UpdateTask — validation rejects negative values:**
```
Given: UpdateTaskRequest with estimatedHours=-1.0
When:  UpdateTask is called
Then:  Returns 400 Bad Request
```

**UpdateTask — validation rejects negative story points:**
```
Given: UpdateTaskRequest with storyPoints=-5
When:  UpdateTask is called
Then:  Returns 400 Bad Request
```

### 9.2 Backend Integration Tests (Postgres Store)

Using a test PostgreSQL instance (e.g., Testcontainers or a local Docker Postgres):

```
Given: V7 migration has run
When:  A task is created with estimatedHours=2.5, actualHours=1.0, storyPoints=5
Then:  SELECT from task_items returns the correct values
  And: HydrateFromDb maps them correctly to TaskItem
  And: Updating only actualHours to 1.5 via UpdateTask persists only that change
```

Check NULL round-trip:
```
Given: A task created with no effort fields
When:  GET /api/tasks/{id}
Then:  Response JSON contains "estimatedHours": null, "actualHours": null, "storyPoints": null
```

### 9.3 Frontend Manual Smoke Tests

| Step | Expected Result |
|---|---|
| Open any existing task detail page | "Effort Tracking" card appears with all three inputs empty |
| Enter `4.5` in Estimated Hours, click away (blur) | No error; field retains value after save |
| Reload the page | Estimated Hours input shows `4.5` |
| Enter `2.0` in Actual Hours, click away | Saves successfully |
| Enter `3` in Story Points, click away | Saves successfully |
| Reload the page | All three fields show previously saved values |
| Clear Estimated Hours (empty the input), click away | Field becomes null; after reload, input is empty |
| Enter `-1` in Estimated Hours, click away | Backend returns 400; UI shows error (or silently fails to save, depending on error-handling implementation) |
| Open a newly created task | All three fields are empty (null from API) |

### 9.4 API Contract Tests (optional, recommended)

Use a REST client (e.g., `.http` files in VS Code, Postman, or Bruno) to verify:

```
PATCH /api/tasks/{id}
Content-Type: application/json

{ "estimatedHours": 8.0 }

→ 200 OK
→ GET /api/tasks/{id} returns estimatedHours: 8.0
```

```
PATCH /api/tasks/{id}
Content-Type: application/json

{ "storyPoints": -3 }

→ 400 Bad Request
→ Body contains descriptive error message
```

---

## Appendix: Key Files Changed

| File | Change Type | Scope |
|---|---|---|
| `ApiEndpoints.cs` | Additive edit | `TaskItem` class, `TaskRow` struct, `UpdateTaskRequest` DTO, `CreateTaskRequest` DTO, `CreateTask()`, `UpdateTask()`, `HydrateFromDb` SELECT, `SaveAsync` INSERT/UPDATE |
| `Migrations/V7__task_effort_tracking.sql` | New file | PostgreSQL schema |
| `apps/web/src/app/tasks/[id]/page.tsx` | Additive edit | TS type, state, `applyTaskToView`, `saveMeta`, JSX card |

---

## Open Questions

| # | Question | Impact | Resolution Needed By |
|---|---|---|---|
| 1 | Should clearing an effort field (sending `null` explicitly in PATCH) overwrite the existing value, or leave it unchanged? Currently, `null` in the DTO means "no change." Explicit clear would require a separate sentinel (e.g., a `fieldsToNull` array in the request body). | Low — UX edge case | Before implementation |
| 2 | Are effort fields needed in the task **list** response, or only on the detail page? Including them in the list adds minor payload overhead. | Low | Before implementation |
| 3 | Should there be a read-only computed field `effortVariance = actualHours - estimatedHours`? | Low — purely additive if yes | Post-MVP |
| 4 | Does Dapper snake_case mapping (`DefaultTypeMap.MatchNamesWithUnderscores`) need to be explicitly enabled, or is it already set in `Program.cs`? | Implementation detail | During development |

---

*End of Technical Design: Task Effort Tracking*
