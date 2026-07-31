# PRD: Task Effort Tracking

## Problem

Rora-Quest users — individual developers and students — plan and track tasks by week and
category, but they have no way to record how long a task was expected to take or how long
it actually took. This creates several pain points:

1. **No estimation discipline.** Users cannot practise scoping tasks before they start
   (a core skill in software engineering and study planning).
2. **No retrospective insight.** After a task is done there is no record of whether the
   effort was over- or under-estimated, making it impossible to improve future estimates
   even informally.
3. **Agile workflow gap.** Many users think in story points; today there is no lightweight
   field to capture a relative-size judgment on a task without leaving the app.
4. **Manual workarounds.** Users are forced to annotate effort in free-text notes fields
   (`QuestionAndReasoning`, `LogicNotes`) — these fields are not structured, not
   filterable, and easy to lose.

Adding three optional numeric fields (`estimatedHours`, `actualHours`, `storyPoints`) on
each task gives users a first-class, low-friction way to record and review effort in the
same detail view they already use today — without introducing mandatory fields or changing
the core workflow.

---

## Target Users

| Persona | Description |
|---------|-------------|
| **Student learner** | Tracks LeetCode problems, study chapters, and coding exercises by week. Wants to know whether a "Medium" problem actually takes longer than an "Easy" one so they can allocate weekly time better. |
| **Self-studying developer** | Follows a structured curriculum (books, courses, side projects). Uses story points to think about relative task size during weekly planning, and checks actual vs. estimated hours to calibrate. |
| **Hobbyist planner** | Uses Rora-Quest casually for personal goals. Will use effort fields opportunistically — sometimes on one task, sometimes not at all. |

All target users are **single users** managing their **own data**; there are no team or
reporting cross-user scenarios in scope for this release.

---

## User Stories

### US-1 — Record estimated effort before starting a task

> *As a user planning my week, I want to record how many hours I think a task will take
> (or how many story points I assign it), so that I can make realistic commitments for
> the week.*

**Acceptance criteria:**

```
Given I am on the task detail page for any task (any status)
When I enter a positive numeric value in the "Estimated hours" field and save
Then the task is updated with the new estimatedHours value
 And the value is shown on the task detail page on the next load
 And no other fields are modified by this action

Given I enter a non-numeric or negative value in "Estimated hours"
Then the input is marked invalid and the save is blocked
 And an inline validation message is shown

Given I leave "Estimated hours" blank (or clear a previously saved value)
Then estimatedHours is stored as null (not zero)
 And the field displays as empty on reload
```

---

### US-2 — Record actual effort after completing a task

> *As a user who has just finished a task, I want to record how many hours it actually
> took, so that I can compare with my estimate and improve future planning.*

**Acceptance criteria:**

```
Given I am on the task detail page for a task with any status
When I enter a positive numeric value in the "Actual hours" field and save
Then the task is updated with the new actualHours value
 And the value persists after navigating away and returning
 And the field is editable regardless of the task's current status
   (users may enter actual hours on a Done task, or even while still InProgress)

Given actualHours is set and estimatedHours is also set
Then both values are shown on the task detail page
 And no computed variance or ratio is displayed (out of scope for v1)
```

---

### US-3 — View effort data on the task detail page

> *As a user reviewing a completed task, I want to see estimated hours, actual hours, and
> story points in one place on the task detail page, so that I can quickly assess how the
> task went.*

**Acceptance criteria:**

```
Given a task that has at least one effort field set (estimatedHours, actualHours, or storyPoints)
When I open the task detail page
Then the non-null effort values are displayed in a clearly labelled "Effort" section
 And null values are shown as empty (not zero, not "N/A")
 And the Effort section is visible whether or not all three fields are filled

Given a task where all three effort fields are null
Then the Effort section is still rendered (with empty inputs)
 So that the user can easily discover and fill in the fields

Given I am on the task list / week view (not the detail page)
Then effort values are NOT shown inline on task cards (not in scope for v1)
```

---

### US-4 — Use story points instead of (or alongside) hours

> *As a developer who thinks in relative story-point sizing, I want to assign a story
> point value to a task independently of hours, so that I can size tasks during planning
> without committing to a specific hour estimate.*

**Acceptance criteria:**

```
Given I am on the task detail page
When I enter a positive integer in the "Story points" field and save
Then storyPoints is persisted as a numeric value
 And the value is shown in the Effort section on reload
 And changing storyPoints does not affect estimatedHours or actualHours

Given I enter a decimal value (e.g. 0.5) in the "Story points" field
Then the value is accepted (fractional story points are permitted)

Given I enter a value of 0 in either "Story points" or any hours field
Then 0 is accepted and stored as-is (zero is a valid effort value)
 [Assumption: 0 is meaningful — "this task required no effort" — so it is
  stored, not treated as null.]
```

---

### US-5 — Effort fields survive round-trips through the PATCH API

> *As an API consumer (or future integration), I want the effort fields to be readable
> from GET /api/tasks and writable via PATCH /api/tasks/{id}, so that any client can
> read and update effort without a separate endpoint.*

**Acceptance criteria:**

```
Given a PATCH /api/tasks/{id} request body containing
  { "estimatedHours": 2.5, "actualHours": null, "storyPoints": 3 }
When the request is processed
Then estimatedHours is set to 2.5
 And actualHours is set (or remains) null
 And storyPoints is set to 3
 And the response DTO includes all three fields

Given a PATCH /api/tasks/{id} request body that omits all effort fields
Then existing effort values are unchanged (partial-update semantics)

Given a GET /api/tasks/{id} response
Then the response body always includes estimatedHours, actualHours, and storyPoints
 (with null when not set, never absent from the JSON)
```

---

## Scope

### In scope

| Item | Details |
|------|---------|
| Three new nullable numeric fields on `TaskItem` | `estimatedHours: decimal?`, `actualHours: decimal?`, `storyPoints: decimal?` |
| All fields are optional per task | No validation enforcement; any or all may be null |
| Display and inline edit on the task detail page (`/tasks/[id]`) | New "Effort" section beneath existing metadata fields |
| Backend: `TaskItem` domain model updated | Add the three properties |
| Backend: `UpdateTaskRequest` DTO updated | Accept the three optional fields |
| Backend: `CreateTaskRequest` DTO updated | Accept the three optional fields (optional, defaults null) |
| Backend: GET response DTO always includes fields | All three fields serialized (null when unset) |
| Backend: PATCH `/api/tasks/{id}` persists changes | Existing partial-update handler extended |
| In-memory store: fields persist within session | No extra work; fields live on the `TaskItem` object |
| Postgres store: schema migration adds three nullable columns | `estimated_hours NUMERIC`, `actual_hours NUMERIC`, `story_points NUMERIC` |
| Frontend: `TaskItem` TypeScript type updated | Add the three optional fields |
| Frontend: Effort section UI with three labelled numeric inputs | Show on task detail page; edit in-place with the existing save pattern |
| Input validation: non-negative decimals only | Client-side; server-side returns 400 on negative values |

### Out of scope

- Reporting or analytics (totals, averages, variance) across tasks or weeks
- Time-tracking timers or automatic time capture
- Sprint / iteration / velocity management
- Mandatory enforcement of any effort field
- Displaying effort on task cards in the week/list view
- Effort history or audit log
- Bulk editing effort across multiple tasks

---

## Functional Requirements

### Backend

1. **FR-B1** `TaskItem` class gains three nullable decimal properties:
   `EstimatedHours`, `ActualHours`, `StoryPoints`. All default to `null`.

2. **FR-B2** `UpdateTaskRequest` record gains three nullable parameters:
   `decimal? EstimatedHours = null`, `decimal? ActualHours = null`,
   `decimal? StoryPoints = null`.

3. **FR-B3** `CreateTaskRequest` record gains the same three nullable parameters.

4. **FR-B4** `RoraQuestService.UpdateTask` applies each effort field when present in the
   request using the existing nullable-patch pattern:
   `if (req.EstimatedHours is not null) task.EstimatedHours = req.EstimatedHours;`
   To allow clearing a field, the update mechanism must distinguish "not supplied"
   from "explicitly set to null". An `Optional<T>` or sentinel pattern may be required
   (tracked as open question OQ-1).

5. **FR-B5** All three fields are serialized in GET `/api/tasks` and
   GET `/api/tasks/{id}` responses. Null fields are serialized as JSON `null`, not
   omitted.

6. **FR-B6** The Postgres migration adds three nullable `NUMERIC` columns to the tasks
   table: `estimated_hours`, `actual_hours`, `story_points`. Existing rows receive
   `NULL`.

7. **FR-B7** Server-side validation: any supplied effort value that is negative returns
   `400 Bad Request` with a descriptive message.

### Frontend

8. **FR-F1** The `TaskItem` TypeScript interface in the task detail page gains:
   `estimatedHours?: number | null`, `actualHours?: number | null`,
   `storyPoints?: number | null`.

9. **FR-F2** The task detail page renders an **Effort** section between the existing
   metadata block and the notes/sub-steps block. The section contains three labelled
   numeric inputs: **Estimated hours**, **Actual hours**, **Story points**.

10. **FR-F3** Each input accepts positive decimals (step `0.25` for hours, step `0.5` for
    story points). Zero is accepted; negative values are blocked client-side with an
    inline error.

11. **FR-F4** Effort fields are saved with the same PATCH call used for other task
    metadata edits. No separate save button is required if the page already uses an
    auto-save or unified save pattern.

12. **FR-F5** When all three effort fields are null, the inputs are shown empty — not as
    zero.

---

## Non-Functional Requirements

| ID | Requirement |
|----|------------|
| NFR-1 | Saving effort fields must complete within the same latency budget as existing PATCH task calls (p95 < 500 ms on local dev; no new network round-trips). |
| NFR-2 | The Postgres migration must be additive (no column removed, no existing column altered) and run without downtime on an empty or populated tasks table. |
| NFR-3 | Null effort fields must not appear as `0` anywhere in the UI or API response — they must be `null` / empty. |
| NFR-4 | The three new fields must not break existing PATCH payloads that omit them (backward compatibility). |
| NFR-5 | Input validation error messages must be visible without scrolling on a standard 1280 × 800 viewport. |
| NFR-6 | The Effort section must be accessible: inputs have visible labels, correct `type="number"`, and announce validation errors to screen readers via `aria-describedby`. |

---

## Acceptance Criteria (Feature-level)

These criteria gate the feature as shippable:

1. A task detail page for a new task shows the Effort section with three empty inputs.
2. Entering `2` in Estimated hours, saving, and reloading shows `2` in Estimated hours.
3. Entering `3.5` in Actual hours, saving, and reloading shows `3.5` in Actual hours.
4. Entering `5` in Story points, saving, and reloading shows `5` in Story points.
5. Clearing a previously saved value, saving, and reloading shows the field empty (not `0`).
6. Entering `-1` in any field blocks save and shows an error message.
7. `GET /api/tasks/{id}` for a task with only `storyPoints = 3` returns
   `"estimatedHours": null, "actualHours": null, "storyPoints": 3`.
8. `PATCH /api/tasks/{id}` with `{ "storyPoints": 8 }` updates only `storyPoints`;
   `estimatedHours` and `actualHours` are unchanged.
9. An existing task created before the migration has all three effort fields as `null`
   (no data corruption on migration).
10. The PATCH endpoint rejects `{ "estimatedHours": -2 }` with HTTP 400.

---

## Edge Cases

| # | Scenario | Expected Behaviour |
|---|----------|-------------------|
| EC-1 | User sets `estimatedHours = 0` | Stored as `0`, displayed as `0`. Not treated as null. |
| EC-2 | User clears a previously non-null field | Field stored as `null`; displayed empty on reload. |
| EC-3 | User enters a very large value (e.g. `9999.99`) | Accepted; no upper-bound validation in v1. |
| EC-4 | User enters a non-numeric string in a numeric input | HTML `type="number"` prevents submission; client shows native validation. |
| EC-5 | Concurrent PATCH from two tabs (`IfMatchVersion` mismatch) | Existing `RowVersion` conflict handling applies; HTTP 409 returned. |
| EC-6 | In-memory store (no Postgres) — effort fields set | Fields persist for the lifetime of the server process; behave identically to other fields. |
| EC-7 | Task migrated from Postgres with NULL columns | All three fields deserialize as `null`; no errors. |
| EC-8 | `storyPoints` is a fractional value (e.g. `0.5`) | Accepted and stored. Teams that use only integers are not broken. |

---

## Non-Goals

1. **Effort reporting / analytics.** No charts, totals, averages, or variance dashboards
   across tasks, weeks, or categories.
2. **Time-tracking timers.** No start/stop clock; no automatic time capture.
3. **Sprint or velocity management.** No sprint board, velocity chart, or burndown.
4. **Mandatory effort fields.** No rule, validation, or workflow gate that requires effort
   to be filled in before a task can be moved to Done.
5. **Effort on task cards.** Effort values will not appear on week-view or list-view
   task cards — only on the task detail page.
6. **Multi-user or team effort.** All effort data is personal to the single user.
7. **Effort history / audit.** No changelog or timeline of effort edits.
8. **Server-side rounding.** Values are stored as entered; no forced rounding to the
   nearest half-hour or integer.

---

## Open Questions

| ID | Question | Assumption (if unresolved) |
|----|----------|---------------------------|
| OQ-1 | How should "clear a field" be distinguished from "field not supplied" in the PATCH body? The current `UpdateTaskRequest` uses `null` to mean "do not change". A separate `Optional<T>` wrapper or a JSON-patch-style `null`-means-clear convention is needed. | **Assumption:** Adopt an `Optional<T>` / `JsonOptional<T>` sentinel so that omitted fields are no-ops and explicitly supplied `null` clears the value. Implementation detail deferred to engineer. |
| OQ-2 | Should `storyPoints` be constrained to integers only, or allow decimals (e.g. `0.5` for a half-point)? | **Assumption:** Allow decimals for flexibility. Teams that use only integers are unaffected. |
| OQ-3 | Should the frontend show an "efficiency ratio" (actualHours / estimatedHours) when both fields are set? | **Assumption:** No, deferred to a future analytics feature. |
| OQ-4 | Should `estimatedHours` be editable after the task is marked Done? | **Assumption:** Yes — all three fields are editable regardless of task status. |
| OQ-5 | Is there a max value constraint needed (e.g. cap at 999 hours)? | **Assumption:** No cap in v1; extremely large values are unlikely in a personal tracker. |

---

## Success Metrics

| # | Metric | Target / Measurement Method |
|---|--------|----------------------------|
| SM-1 | **Adoption rate** — % of tasks created after the release date that have at least one effort field set (≥ 1 non-null value). | Measure 30 days post-release via a Kusto/DB query on `tasks` table. Target: ≥ 20% of new tasks. |
| SM-2 | **Completion rate** — % of tasks with `estimatedHours` set that also have `actualHours` set when status = Done. | Indicates the feature is used as a full estimate-then-actual loop. Target: ≥ 40% of those tasks. |
| SM-3 | **Zero data-loss bugs** — No P1/P2 bugs filed within 30 days post-release relating to effort values being lost, corrupted, or shown incorrectly. | GitHub Issues monitor. Target: 0 such bugs. |
| SM-4 | **No regression in save latency** — p95 of PATCH `/api/tasks/{id}` does not increase by more than 50 ms after the release. | Compare baseline (pre-release) vs. post-release p95 in app telemetry. |
| SM-5 | **Story-point usage** — % of tasks with `storyPoints` set (non-null). | Indicates whether users find the story-point mode valuable vs. hours-only. Informational for next iteration. |

---

## Approval

- Product Manager approval: Pending human review
- Human product owner approval: Required before implementation begins
- Approved at: TBD