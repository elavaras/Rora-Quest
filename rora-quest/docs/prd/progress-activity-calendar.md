# PRD: Progress Activity Calendar

**Phase:** Product requirements baseline; implementation evidence and outstanding release gates are recorded in the [runbook](../runbooks/progress-activity-calendar.md) and [acceptance handoff](../../source/apps/web/tests/progress-acceptance.md).

**Date:** 2026-10-06

**Effective project root:** `rora-quest\`

**Canonical destination:** `/progress`

## Problem

Scorecard and Streak & Consistency split a user's progress across two destinations
without reliably answering: "When did I make progress, what did I complete, what
is left in my current weekly plan, and where are the recorded activity gaps?"
Current task state and schedule are useful, but neither is a history of work.
In particular, a task's last edit must not masquerade as its completion date.

Replace these destinations with one trustworthy, neutral **Progress** screen.
Separate **recorded completion activity** from **the current weekly schedule**.
Do not imply that absence of recorded completions means no effort, a missed
commitment, low productivity, or lost mastery.

### Source-grounded context

Paths below are relative to the effective project root. These are observed
constraints, not a proposed implementation.

| Source | Relevant current behavior and implication |
|---|---|
| Repository-root `AGENTS.md` | PRD precedes design, implementation includes tests, and a tester validates acceptance criteria. No auth/security changes or file deletion without explicit design approval. |
| `source\apps\web\src\app\layout.tsx`, `scorecard\page.tsx`, `tracking\page.tsx` | Separate navigation entries exist. Scorecard shows binary completion/carry-over and a Notes card; Tracking shows streak, consistency, and adaptive color recommendations. These do not belong in the new Progress UX. |
| `source\apps\web\src\app\dashboard\page.tsx` | Dashboard still consumes scorecard and report APIs. Keep those contracts and Dashboard behavior; do not repurpose their metrics as activity. |
| `source\apps\api\src\RoraQuest.Api\ApiEndpoints.cs`: `GetScorecard`, `GetStreaks`, `GetConsistency`, `FilterByWindow` | Scorecard uses current progress and planned-week membership; streaks use current completed tasks' `UpdatedAt.Date`; consistency averages current progress. None supplies trustworthy completion history. |
| Same file: `UpdateTaskStatus`, `UpdateSubstep`, `TaskStatusEvent`, `TaskSubStep` | Status transitions have a ledger, but substeps only retain current `IsDone`/`CompletedAt`; reopening clears the latter. Same-state status writes can append status events. Final substep completion can automatically finish the parent. These are not sufficient historical activity records. |
| Same file: `GetTaskProgress`, `GetTasks`; web `tasks\page.tsx`, `tasks\[id]\page.tsx` | Substep weights determine progress, with count-based fallback when total weight is zero. Without substeps, only Done gives 100%. Week membership uses `PlannedWeekStart` or a `PlannedDate` in that week. Preserve these semantics. Backend progress rounds to two decimals; task tiles use integer display rounding. |
| `source\apps\api\tests\RoraQuest.Api.Tests\TaskWorkflowPolicyTests.cs` | Existing policy covers DSA restrictions, automatic parent completion/reopening, substep no-ops, and retaining Skipped/Cancelled parent status. Progress must not change this policy. |
| `source\apps\api\src\RoraQuest.Api\Persistence\IRoraQuestStore.cs`, `Program.cs` | Both default `InMemoryRoraQuestStore` and configured Postgres are supported. The default store is currently process-memory only. Durable Progress history in this mode is a new release requirement, not an existing capability. |
| `Persistence\PostgresRoraQuestStore.cs`, `infra\sql\V1__baseline.sql` | Postgres hydrates a cached aggregate, replaces task/child collections on aggregate saves, and supports targeted task deletion. Existing status events cascade with task deletion. New Progress history and coverage must survive these operations. The users table defaults timezone to `Asia/Kolkata`. |
| `Persistence\DatabaseMigrator.cs`, `infra\sql\V1__baseline.sql` through `V8__task_ai_review_feedback.sql` | Startup applies versioned `V<number>__<description>.sql` migrations, records versions, and uses transactions. Follow this convention; do not rewrite applied migrations. |
| `docs\prd\task-effort-tracking.md`, `docs\prd\manual-dsa-task-creation.md`, `docs\task-week-navigation.md`, web `tasks\week-dates.ts` | Preserve optional effort fields, manual DSA creation and subsequent restrictions, Monday-Sunday weeks, native date controls, week navigation and responsive behavior. Task navigation currently uses date-only local values, bounded by `0001-01-01` and `9999-12-26`; it does not initialize its selected week from a URL parameter. |

## Target users

- Individual learners and developers reviewing their own task/substep completions.
- Users returning after a break who want neutral context and a useful task to resume.
- Weekly planners checking the current state of scheduled work without confusing it
  with a historical commitment or a daily attendance target.

Existing owner/access boundaries apply. Cross-user, team, and comparative reporting
are not included.

## User stories

1. **See participation:** As a learner, I can see recent days with recorded progress
   without interpreting darker colors as more effort or mastery.
2. **Recall work:** I can select a day and read the completed task/substep names and
   counts, then open a surviving task.
3. **Review the current plan:** I can see how many tasks in a selected week are
   currently complete, recognize partial weighted progress, and open unfinished work.
4. **Understand gaps:** I can locate the longest recorded inactive interval in a
   range and see my current gap and last recorded progress without a streak reset.
5. **Trust the evidence:** Unknown dates, partial tracking, future plans, failed
   requests, edits, and application restarts do not create a false history.

## Scope

### First release

One screen with all five required areas:

| Area | Minimum shippable outcome |
|---|---|
| Activity calendar | Readable four-week default, bounded range navigation, visible timezone/date bounds, one positive activity level, and honest date states. |
| Selected-day detail | Pointer/touch/keyboard selection; completion names and counts; useful task links; chronological accessible list. |
| Recent participation | Number of days with recorded progress in the selected range, with coverage context; no participation percentage. |
| Weekly plan review | One selected Monday-Sunday week, current task completion count, weighted partial progress, unfinished-task links, and Tasks by Week handoff. |
| Activity gaps | Longest full inactive run within the selected range, selectable grid highlight, current gap through the latest elapsed day, and last recorded activity. |

The minimum supporting work includes durable completion records **and durable
coverage** in both stores, bounded/validated reporting inputs, compatibility
redirects, additive migration, and tests. A cosmetic replacement backed by
`UpdatedAt`, current completed flags, or process-only records is not shippable.

### Explicit release assumptions / decisions

- **A1 — Conservative completion identity:** Count the first reliably recorded
  completion of a stable task/substep identity only. Reopening/recompletion does
  not earn another recorded activity day. This is a product choice to avoid
  rewarding repeated toggles; it is not a recurring-work tracker.
- **A2 — Manageable range:** Default to the current Monday-Sunday week plus the
  preceding three weeks. Custom ranges contain 1-84 inclusive calendar dates.
- **A3 — Reporting timezone:** Explicit default `Asia/Kolkata`; supported named
  timezone contexts include `UTC` and `America/New_York`. No new timezone-settings
  editor is required. Validated configuration/report context may choose another
  supported named zone; browser/server local timezone is never an implicit default.
- **A4 — Historical identity:** Keep minimal completion-time task/substep names
  even after renaming/deletion, under the same owner's existing data boundaries.
  Deleting a task is not deletion of its historical Progress record.
- **A5 — Rollout, not reconstruction:** Begin at a provable reliable tracking
  instant per owner. Older dates are unknown; no inferred year or imported history.
- **A6 — Narrow integration:** Dashboard remains a separate, unchanged destination.
  Only obsolete Progress-related navigation/copy and a week-specific Tasks by Week
  handoff may require changes outside the new screen; no broader redesign.

These decisions provide a buildable baseline. Changing them requires updating this
PRD before design is approved, not silently choosing different behavior in code.

## Functional requirements

### FR-1 — One destination and compatible navigation

1. `/progress` is the only primary destination for the five areas. Replace both
   Scorecard and Streak & Consistency navigation items with one **Progress** item.
2. `/scorecard` and `/tracking` remain reachable as compatibility redirects, without
   redirect loops or duplicate legacy dashboards. Preserve valid legacy date-only
   `from`/`to` bounds that satisfy the new range contract. Unsupported, incomplete,
   or invalid legacy filters must produce a visible explanation and the default
   range, not silently display a different range as if it were requested.
3. Remove the old scorecard Notes card, consistency metric, streak/reset language,
   and adaptive Green/Yellow/Red recommendation from Progress. Do not add XP,
   productivity, mastery, carry-over scores, or completion-rate competition here.
4. Preserve existing scorecard, tracking, planning-recommendation, and report APIs
   and consumers. New reporting must not change the meaning of old payloads.
   Existing Dashboard/task workflow is out of scope, except the narrow handoff
   and obsolete-entry/copy cleanup above. Do not delete legacy files in this phase.

### FR-2 — Trustworthy qualifying completions

**Activity unit:** one task without substeps, or one specific substep within a task.
A day is active when at least one qualifying unit was completed on that date in the
reporting timezone. A hundred units and one unit use the same positive intensity.

| Accepted action or later change | Progress history outcome |
|---|---|
| Existing substep changes `IsDone: false -> true` | Record one qualifying completion if that substep identity has no previous reliable completion record. Include parent and substep identity/name. |
| Existing task with no substeps changes from a non-Done status to Done through an allowed completion action | Record one qualifying completion if that task identity has no previous reliable task-completion record. |
| Final substep completes and parent automatically becomes Done | Record the substep only. Parent completion is context, not a second unit, second count, or separate activity event. |
| Parent with substeps is manually set to Done, including override of incomplete substeps | No extra activity. Do not fabricate substep completions; preserve existing allowed/blocked workflow. |
| Same-state write, repeated click, response-loss retry, concurrent duplicate, conflict, rejected or failed mutation | No extra qualifying record. A failed completion must not produce a success-looking history or leave an acknowledged but unrecorded completion. |
| Reopen task / uncheck substep | Retain the original recorded completion/date; add no positive activity and do not erase history. Current weekly progress may decrease. |
| Recomplete the same recorded identity, on the same or a later date | No new qualifying record, date, or last-activity change under A1. Explain "First recorded completions; reopening and rechecking do not add activity." |
| Pre-rollout item with no reliable completion record is genuinely completed after tracking begins | Its first observed qualifying transition may count now, including after a reopen. Do not claim this was its first-ever lifetime completion or infer its earlier date. |
| Set status to Skipped or Cancelled | No completion activity, including when changing from Done. Existing historical records remain. |
| Complete a substep while parent remains Skipped/Cancelled | Count the newly completed substep if allowed by current workflow and not previously recorded. Do not change the parent status to satisfy this feature. |
| Create/import task already marked Done; import checkbox; seed data; metadata update | No completion activity. Creation/import is not an observed completion transition. |
| Edit notes, title, estimates/actual hours/story points, schedule, due date, weight/structure, links, confidence checkbox; login or page view | No activity. Even an edit that makes current progress reach 100% is not a qualifying completion action. |
| Delete task/substep, bulk delete, or existing duplicate cleanup | No new activity and no erasure/recount of recorded completions. Retain minimal historical identity; remove unavailable task links. |

Use the authoritative successful mutation's timestamp as an instant, not a client
backdated timestamp or parent `UpdatedAt`. Minimum durable evidence is owner,
stable completion identity, kind, occurrence instant, task/substep identity and
completion-time name, plus what is needed to prevent duplicate recognition.
Do not copy task notes, attachments, or effort fields into activity history.

Both successful mutation and history durability must agree. A persistence failure
must not acknowledge an unrecorded success or publish a phantom event; retries
after a commit whose response was lost must still yield one record. Existing
concurrency/version and DSA policy outcomes remain intact.

### FR-3 — Honest coverage, independent of visits

1. Persist the instant from which recording is reliably enabled for each owner,
   and any interval in which completeness cannot be guaranteed. If rollout only
   establishes reliability on first access, use that actual later instant; never
   backdate it to deployment, account creation, first task, or an old status event.
   Absence of a completion is not evidence of when tracking started.
2. Coverage is capability to record all qualifying mutations, not daily login,
   page visits, or presence of events. No task/day is needed to maintain coverage.
3. A **fully tracked calendar day** has reliable coverage for its entire local
   midnight-to-next-midnight interval and has fully elapsed at the report's
   authoritative as-of instant. A tracking start partway through a day excludes
   that day from full inactivity; an exact-midnight start can cover that whole day.
4. Unknown, interrupted, or partial coverage breaks an inactive run. Do not bridge
   such intervals, treat them as zeroes, or fill them from current task state,
   `CompletedAt`, `UpdatedAt`, status events, imports, or schedules.
5. A recorded positive completion remains positive evidence even on a partially
   tracked day; show the coverage qualification. Today may be active already,
   but an incomplete today cannot be a full inactive day.
6. Ordinary restart must preserve events, deduplication evidence, and coverage.
   Preserve continuity only where no unrecorded mutation could have succeeded.
   A rollback or capture outage with uncertain writes is explicitly untracked.
   Missing/corrupt coverage storage is an error, not a new empty tracking history.

### FR-4 — Calendar, reporting time, and range navigation

1. The default displays 28 dates: Monday three weeks before the current week
   through Sunday of the current week, using the reporting timezone's today.
   Current-week future dates remain visible as future, never inactive.
2. Show inclusive range start/end, year where needed, reporting timezone, and
   report freshness/as-of context. **Previous range** and **Next range** shift by
   the displayed number of dates without overlap; **Current four weeks** restores
   the default. Provide native date-only custom bounds for 1-84 dates.
3. Initially select today. On range navigation retain the selection only if still
   inside the new range; otherwise select today if included, else the range's
   first date. Day detail and weekly review must follow the resulting selection.
4. Date keys and bounds are strict `YYYY-MM-DD` calendar dates, inclusive. Support
   the existing complete-week date domain `0001-01-01` through `9999-12-26`; prevent
   navigation past these bounds. Reject impossible dates, reversed bounds, omitted
   single bounds, datetime strings, and ranges longer than 84 dates. Both bounds
   absent means the documented default. Direct invalid report requests return a
   descriptive validation error, not a success with empty/default data.
5. Validate supported named timezone identifiers on the server and in any selector.
   Invalid/unsupported/oversized zone values are errors, not fallback to local
   time or UTC. Bound timezone identifier input to 100 characters. Expose the
   resolved zone consistently across calendar, detail, participation, and gaps.
6. Map completion instants to local calendar dates in that zone. Do not serialize
   a browser-local midnight with `toISOString()` to obtain a date key, parse a
   date-only key as a UTC instant, or add fixed 24-hour durations for local days.
   UTC boundary crossings and 23/25-hour DST days must work.
7. A supported timezone-context change reprojects immutable completion instants
   and coverage boundaries; it does not rewrite events, reset tracking, or shift
   date-only task schedules. Refresh all affected areas together. An already-open
   view crossing local midnight must refresh/reclassify before showing current
   metrics; backgrounded views do so on return, without changing an explicit
   historical range selection.

Calendar states must be understandable without color:

| Evidence/date state | Required meaning | Gap eligibility |
|---|---|---|
| At least one qualifying completion on an elapsed date or today | Active; one positive level, count in detail; partial-coverage annotation if relevant | Never inactive |
| Fully tracked elapsed day with no qualifying completion | **No recorded progress** | Eligible |
| Untracked or partly tracked elapsed day with no completion | Unknown / partially tracked, with reason | Ineligible; breaks runs |
| Today with no completion | **No recorded progress yet today**; day in progress | Ineligible |
| Future date | Future; current plans may exist separately | Ineligible |
| Loading, missing required data, or error | Pending/unavailable, not a successful date state | No calculation from missing data |

Any padding dates outside custom bounds are non-selected context, not silently
included in counts/gaps.

### FR-5 — Selected-day detail and recent participation

1. Every in-range date is selectable by tap, click, and keyboard, including
   inactive, unknown, and future dates. Show selected date, reporting zone,
   coverage state, and number of qualifying units.
2. For recorded completions, show completion-time task names and substep names,
   with counts separated as, for example, "1 task without substeps and 2 substeps
   across 2 other tasks." A parent name grouping is not another completion.
   List in occurrence-time order with a stable identity tie-break; any multi-day
   text alternative is date-ascending, then occurrence-time ascending.
3. Link accessible surviving tasks to their existing task detail pages. If a
   task/substep was removed, show a historical/removed label, preserve the
   recorded name, and avoid a broken link. Renames do not rewrite recorded names;
   the existing task link may open the task under its current name.
4. Reopened/Skipped/Cancelled/current task states must not relabel the historical
   event as if it happened today. Identify current state separately when shown.
5. Day detail need not duplicate scheduling in v1. If it does, label a separate
   section **Currently scheduled for this date — not completion history**; never
   blend those rows/counts into recorded outcomes.
6. Participation reads **"Recorded progress on X days in [selected range]"**.
   Count distinct local dates, including positive partial days/today. Explain
   fully tracked elapsed days, unknown/partial elapsed days, today-in-progress,
   and future dates where present. Do not divide by all displayed dates or show
   a percentage, target, grade, reset, or scheduled-day attendance measure.
7. An entirely unknown historical range says records/coverage are unavailable for
   that period, not "0 days worked." An empty but reliably tracked elapsed period
   can show zero recorded active days and **No recorded progress**. No-task,
   no-plan, unknown-history, and request-failure states remain distinguishable.

### FR-6 — Weekly review is current schedule, not historical adherence

1. Review the full Monday-Sunday week containing the selected calendar date;
   initially this is the current week. Always show that week's exact bounds and
   **Current schedule, as of [report time]**. If a custom activity range clips the
   week, explicitly state that this section still reviews the full week.
2. Use the same current membership as Tasks by Week: a task belongs when its
   `PlannedWeekStart` equals the selected Monday **or** its `PlannedDate` is in
   that week. Count each task identity once in that week. Include tasks assigned
   to the week with no day; label them **No day selected**, not historical work.
   Preserve this existing membership even for inconsistent legacy schedule fields;
   do not silently repair schedules or add cross-week totals in this feature.
3. Show, for example, **"2 of 3 currently scheduled tasks complete"**, using the
   existing backend progress-based completion rule (`GetTaskProgress >= 100`),
   not status alone and not a new rounding rule.
4. Preserve progress: with substeps, use completed weight / total weight when
   total weight is positive; otherwise completed substep count / total count.
   Without substeps, Done is 100% and other statuses are 0%. Do not weight tasks
   by effort hours, story points, priority, or activity count.
5. Show each task's useful weighted partial progress and current status. A single
   weekly partial-progress summary, if presented, is the equal-task mean of the
   existing task progress values, labeled **Current plan progress**, not
   participation or adherence. Avoid additional competing percentages. Display
   enough precision not to label backend-incomplete progress as complete; existing
   task pages' display rounding need not change.
6. Skipped/Cancelled tasks stay in the current schedule's denominator. A task with
   complete substeps can count as progress-complete while remaining Skipped or
   Cancelled; display both facts. Such a task without substeps is 0%. Incomplete
   terminal tasks are identified by status, not phrased as an obligation to finish.
   A manual Done override with incomplete substeps remains partially complete
   under the existing progress rule.
7. Provide unfinished-task links and **Open this week in Tasks by Week**. That
   handoff must open the specified week, including an empty week, rather than
   auto-jumping elsewhere. Any small entry-context addition must preserve existing
   default auto-jump, date-picker, Grid/List, move, bulk-selection, and task flows.
8. Rescheduling, deleting, reopening, or editing progress updates this **current**
   review after refresh but never moves historical completion dates. Work
   completed outside its planned week appears on its actual recorded date.
   Future scheduled tasks do not create future or current activity.
9. With no currently scheduled tasks, show that fact and the week link; do not
   divide by zero or show 100% adherence. Never describe this section as original
   plan adherence, work actually performed in that week, daily consistency, or
   commitments kept/missed.

### FR-7 — Activity gaps with explicit scopes

**Inactive date:** an elapsed, fully tracked local calendar date with no qualifying
completion. Consecutive means adjacent calendar dates in the reporting timezone,
not working days or 24-hour durations. Weekends and rest days count, with copy
explaining that gaps are absence of recorded completions, **not missed commitments**.

**Longest gap in selected range**

- Find the longest contiguous run of inactive dates wholly inside the selected
  inclusive bounds. Unknown/partial days, active days, today, and future dates
  break runs or bound them; never skip across them.
- Show length plus inclusive start/end dates. For equal lengths, choose the run
  with the **most recent end date**; this remains stable across reloads.
- Clip at selected-range boundaries. Explicitly label a boundary-clipped result
  **Within selected range; the gap may extend outside it** rather than presenting
  it as a lifetime record. A coverage boundary similarly explains that earlier/
  later completeness is unknown.
- Selecting the result highlights exactly those dates in the grid and accessible
  list, distinct from selected-day focus and using more than color. Select/open
  its first date's detail; provide a way to clear the highlight. Range changes
  must not leave an old gap highlighted in a new report.
- If no fully tracked elapsed dates exist, say **Not enough fully tracked days**.
  If eligible dates exist but none are inactive, say **No full inactive-day gap
  in this range**. Neither condition invents a gap/date pair.

**Current gap — all reliable recorded history, not selected range**

- Evaluate as of the reporting zone's today, regardless of the selected calendar
  window. A qualifying completion today ends the current gap: zero full days.
- Otherwise count the contiguous inactive suffix ending yesterday. Today is
  explicitly excluded because it is incomplete. Show length, exact date span,
  and **Through yesterday; today is in progress**.
- If yesterday was active, the count is zero; no positive-length date range.
  If yesterday is unknown/partial, or no full elapsed day exists yet, the current
  gap is unavailable, not zero. If current recording completeness cannot be
  established, explain that limitation rather than assert current inactivity.
- Stop at an active day or coverage boundary. At a boundary, label the result
  **At least N fully tracked days; earlier history unknown** (or **Since reliable
  tracking began; no earlier activity recorded**). Do not extend through unknown
  time. This number may exceed the selected-range longest gap, with the different
  scope stated beside both values.

**Last recorded activity — all recorded history through as-of**

- Show latest qualifying completion's local date/time, task/substep description,
  reporting zone, and link where available. Use the latest occurrence instant;
  equal-instant records use stable identity order for the description.
- Explicitly label scope **Since reliable tracking began; not limited to selected
  range**. Offer **View date** to select a valid calendar range containing it.
- When none exists, say **No recorded progress since tracking began [date/time]**,
  not "never worked." If coverage is unavailable, show unavailable instead.
- Reopening, recompletion suppressed by A1, note edits, and deletion do not advance
  last activity. No "streak broken" or failure language.

## Non-functional requirements

1. **Durability and consistency:** Qualifying records, retained identity for
   deduplication, and coverage survive process restart in BOTH supported stores.
   For Postgres, validate a fresh process/cache hydration, not only reuse of a
   service instance. Normal aggregate saves, bulk deletion, duplicate cleanup,
   and targeted settings writes must not erase or duplicate them. For the current
   non-Postgres mode, durable backing for this minimum Progress state is required;
   persisting the entire application is not automatically in scope. A surviving
   record whose task no longer exists uses the unavailable-link behavior.
2. **Owner isolation:** Apply existing user-scope and access checks to every
   summary, date, event, coverage boundary, and link. No identity/authentication/
   authorization redesign, cross-user totals, or third-party telemetry.
3. **Honest asynchronous states:** Loading, absent/null required payloads,
   malformed data, and failures are not empty successes. Show retryable errors.
   Independent sections may succeed separately, but incomplete activity/coverage
   cannot produce participation/gap totals. Ignore stale responses after
   day/range/zone/user changes; do not relabel previous data as the new selection.
4. **Accessibility:** Meet WCAG 2.2 AA for the new UI: accessible names including
   full date, count and date state; keyboard access to every day, navigation,
   links and gap selection; visible focus and selection; no hover-only detail;
   announced loading/error/selection changes. A chronological text/list alternative
   conveys the same evidence and highlighted interval. Legend and state labels
   distinguish active, inactive, unknown/partial, today, future, and highlighted
   gaps independently of color in light and dark themes.
5. **Responsive use:** Usable at 320px width and 200% zoom without page-level
   horizontal overflow or hidden controls. Four-week day selection must remain
   practical by touch; target controls are at least 24x24 CSS px with compliant
   spacing, with 44px preferred for primary navigation. Long names wrap. If a
   larger range needs scrolling, preserve date labels and the list alternative.
6. **Bounded work / existing tools:** Use native date/formatting controls and
   existing Next/React/.NET tooling; no large calendar, chart, habit, or analytics
   dependency. Bound a report to 84 dates and do not fetch a year/full ledger into
   the browser to render four weeks. Detail may be bounded/paged, but counts must
   remain exact and all records reachable without silent truncation. On an agreed
   local fixture of 1,000 tasks/10,000 recorded units, the default and 84-day views
   should reach usable success within 2 seconds at p95 over 20 warm requests;
   record environment and baseline in later test evidence. This is a release
   validation target, not a promise about arbitrary network latency.

## Acceptance criteria

All numbered criteria gate release. They are requirements for subsequent engineers
and the tester agent, not claims that tests or implementation exist now. Tests use
a controllable as-of instant and explicit timezone; tests must not depend on the
developer machine's date, locale, or stored personal data.

1. **AC-01 — Navigation/compatibility.** Exactly one primary Progress entry opens
   `/progress`; both old destinations redirect without loops. Valid bounded legacy
   dates survive; invalid/unsupported filters explain the default. Legacy APIs
   retain existing status/payload semantics; Dashboard still works.
2. **AC-02 — Four-week default.** With today fixed at `2026-10-06` in
   `Asia/Kolkata`, show `2026-09-14` through `2026-10-11` inclusive, select October 6,
   and identify October 7-11 as future. Previous shows August 17-September 13;
   Next returns to September 14-October 11; Current four weeks restores the default.
3. **AC-03 — Bounded validation.** One-day and 84-day ranges succeed. An 85-day
   range, reversed dates, `2026-02-30`, one omitted bound, a datetime in a date
   field, and dates outside `0001-01-01`..`9999-12-26` are rejected in direct
   requests with descriptive errors. UI invalid input does not activate a false
   range; navigation cannot overflow at either boundary. Both omitted bounds
   resolve to the explicit default.
4. **AC-04 — Timezone contract.** Omitted zone resolves explicitly to
   `Asia/Kolkata`; `UTC` and `America/New_York` work. Unknown identifiers and inputs
   over 100 characters fail validation without fallback. Browser zones different
   from the reporting zone produce identical report keys/metrics.
5. **AC-05 — UTC/local boundary.** In Kolkata, an event at
   `2026-10-05T18:29:59Z` belongs to October 5; one at `18:30:00Z` belongs to October 6.
   In UTC both belong to October 5. Counts, detail, last activity and gaps agree;
   schedule date `2026-10-06` remains that date in either context.
6. **AC-06 — Calendar/DST boundaries.** New York March 8, 2026 (23 hours) and
   November 1, 2026 (25 hours) each count as one calendar day. Both occurrences of
   the repeated 01:30 belong to November 1; no duplicate/missing day appears.
   Month/year transitions and February 29, 2028 navigate correctly. A coverage
   interval covering each full local day qualifies it irrespective of hour count.
7. **AC-07 — Qualifying unit/intensity.** First false-to-true substep completion
   and a no-substep task's first allowed non-Done-to-Done transition each count once.
   Days with 1 and 10 such units have the same positive intensity; detail exposes
   the actual counts and names. Distinct units on one date create only one active day.
8. **AC-08 — Final substep/override.** Finishing a parent's final substep creates
   exactly one unit and preserves automatic Done behavior. Manual Done override
   on a task with unfinished substeps fabricates neither a parent activity unit
   nor completed substeps; partial progress remains visible.
9. **AC-09 — Non-activity matrix.** Independently exercise note/title/effort/schedule/
   due-date/link edits, imports including already-Done items, creation as Done,
   seed loading, confidence toggling, weight/structure edits, login and page views.
   None changes activity count/date, last activity, or emits qualifying history,
   even if `UpdatedAt` or progress changes.
10. **AC-10 — No-ops/retries/concurrency.** Repeated same-state completion, two
    concurrent submissions, rejected DSA/status requests, and version conflicts
    create at most the one legitimate first record. Retry after lost success
    response still yields one; rejected attempts yield none.
11. **AC-11 — Reopening/recompletion.** Complete an identity on day A, reopen on
    B, and recomplete on C. Only A remains active because of this identity; its
    last-activity instant stays A, and current weighted progress follows current
    state. Repeat across restart and across a coverage interruption with the same
    result. A legacy identity without a reliable record can count its first
    genuinely observed completion now, without backdating.
12. **AC-12 — Cancel/skip and DSA.** Status-only Cancelled/Skipped changes generate
    no activity or erasure. An allowed first substep completion under a terminal
    parent is recorded while retaining that status. Manual DSA creation, seeded
    weights, prohibited DSA manual-status/structure edits, and existing substep
    completion/reopen workflow remain unchanged.
13. **AC-13 — Rename/deletion.** Rename a completed item, delete its substep/task,
    bulk-delete, and exercise existing duplicate cleanup. Recorded date/count
    and completion-time names survive; surviving links work and removed targets
    are labeled without a broken link. A new task with the same title but a new
    identity does not inherit the old completion record.
14. **AC-14 — No backfill.** Upgrade a populated store containing old Done tasks,
    substep `CompletedAt` values, status events, imports and edits. Before reliable
    capture begins, dates remain unknown and none of these fields seeds calendar
    activity, inactivity, last activity or a synthetic year of coverage.
15. **AC-15 — Tracking-start boundary.** Start tracking at 10:00 local on October 1.
    With no events, October 1 remains partially tracked and never becomes a full
    inactive day; October 2 can become one after it ends. A completion after 10:00
    on October 1 makes that date positively active with a partial-coverage label.
    Repeat with an exact-midnight start: that elapsed day can be fully tracked.
16. **AC-16 — Continuity/unknown breaks.** An elapsed day without visits/events
    remains fully tracked when capture was reliable throughout it. An uncertain
    capture interval splits coverage and inactive runs, including when its start
    or end falls mid-day. No unknown interval is bridged as inactivity.
17. **AC-17 — Today/future.** No-completion today is "No recorded progress yet
    today," never a full inactive day; a qualifying event today makes it active.
    Future dates and scheduled tasks generate no active or inactive dates. An
    open view crossing local midnight refreshes today's identity and eligibility
    without silently replacing an explicitly selected historical range.
18. **AC-18 — Selection/detail/order.** Tap, mouse and keyboard can select active,
    inactive, unknown and future dates. Detail's date, state, task/substep names,
    counts and task links match selection; chronological ordering and stable ties
    hold on reload. If current scheduled work is shown, it has a separate labeled
    section and cannot alter historical counts.
19. **AC-19 — Participation/empty distinctions.** Positive units on three distinct
    dates yield "Recorded progress on 3 days" even if one is partial or today.
    Unknown/future dates are explained, not a denominator. Fully tracked empty,
    all-unknown historical, no-plan, loading and failure states are distinguishable;
    none shows a participation percentage or streak reset.
20. **AC-20 — Current weekly membership.** The selected day's full Monday-Sunday
    week is labeled with date bounds/as-of. Include a task matching either existing
    week-membership condition, counting it once if both match. Include week-only
    tasks as No day selected. Custom ranges clipping a week do not imply that the
    weekly count uses only clipped dates. No read silently reschedules legacy data.
21. **AC-21 — Weighted review example.** Given three currently scheduled tasks with
    backend progress 100%, 100%, and 25% (one completed weight out of four total),
    show "2 of 3 currently scheduled tasks complete" and the 25% partial task.
    If a weekly mean is shown, it is 75% Current plan progress. A zero-total-weight
    task with one of four substeps done is also 25%; no substeps plus Done is 100%.
22. **AC-22 — Status versus progress.** All-substeps-complete Skipped and Cancelled
    tasks remain those statuses and count as progress-complete. Their no-substep
    counterparts stay 0% and remain in the denominator. An incomplete terminal
    task is not called a missed commitment. A value below the backend completion
    threshold does not become counted complete due to UI-only rounding.
23. **AC-23 — Current schedule changes independently.** Move a task to another
    week, reopen it, or delete it and refresh. Weekly review follows the latest
    schedule/state, while the original recorded completion date/count is unchanged.
    A completion outside its planned week is activity only on its observed date.
    Zero scheduled tasks produce a no-plan state, not 100% or division by zero.
24. **AC-24 — Useful workflow handoff.** Each available unfinished-task link opens
    that task; Open this week opens Tasks by Week at the specified Monday, even
    for an empty week. Existing direct `/tasks` initialization, native week/date
    editing, Grid/List, bulk selection and move flows retain their behavior.
25. **AC-25 — Longest gap, ties and weekends.** With October 1-5 elapsed and fully
    tracked, only October 3 active, and range October 1-5, choose October 4-5
    (2 days) over October 1-2. Sunday counts normally. Label the range boundary
    rather than assert a lifetime record; refresh returns the same winner.
26. **AC-26 — Gap clipping/exclusions.** A longer observed run spanning October
    1-5 yields only October 3-5 for selected range October 3-5, explicitly clipped.
    Inserting unknown/partial coverage or an active date splits a run; future dates
    and incomplete today never extend it. No eligible elapsed dates yields Not
    enough fully tracked days; eligible dates all active yields no full gap.
27. **AC-27 — Gap interaction.** Selecting a longest-gap result highlights exactly
    its inclusive dates in both grid and text alternative, selects its first day,
    and announces range/length. Highlight and day focus remain distinguishable
    without color. Clearing/changing range removes the stale highlight.
28. **AC-28 — Current gap scope.** As of noon October 6, with October 2 last active
    and October 3-5 fully tracked and empty, current gap is 3 days, October 3-5,
    through yesterday. Choosing a September activity range does not change it.
    A qualifying completion on October 6 sets it to zero; active October 5 and
    empty October 6 also yield zero full inactive days, with today excluded.
29. **AC-29 — Current gap coverage/no history.** Unknown or partially tracked
    yesterday makes current gap unavailable, not zero. A run ending yesterday
    that reaches a coverage boundary is explicitly a known lower bound/since
    tracking began, not lifetime inactivity. First-day rollout with no full
    elapsed days states that limitation. No recorded activity before the boundary
    is invented to anchor the gap.
30. **AC-30 — Last activity scope.** Last activity shows the latest qualifying
    instant's local date/time, meaningful task/substep description and all-recorded-
    history scope, even outside the selected range. View date reveals and selects
    it. Ties have stable descriptions; removed targets do not break navigation.
    With no recorded event, show the tracking-start-qualified empty message.
31. **AC-31 — Both-store restart.** In each supported store, persist a known
    coverage start, an interruption, completions and deduplication identities.
    Terminate/restart the API and load fresh state. All history/coverage and
    subsequent deduplication agree, including a user with coverage but zero
    completions. Postgres verification must use the database, not an old cache.
32. **AC-32 — Failure atomicity.** Inject a persistence failure during qualifying
    completion and during initial coverage establishment. Do not acknowledge
    unrecorded success, leak a phantom event/state, or show zero-history success.
    Retrying after recovery or a committed-but-lost response produces the correct
    single record. Missing/corrupt durable state is explicitly unavailable.
33. **AC-33 — Persistence paths.** Save unrelated metadata/settings and perform
    aggregate replacement, targeted deletion, bulk deletion and duplicate cleanup.
    In both stores, restart afterward and verify no history/coverage loss,
    duplication, or reassignment across owners.
34. **AC-34 — Migration and rollback.** The next convention-compliant additive
    migration works on clean and populated Postgres, records its version, and is
    safe on repeated startup. Existing task data/API consumers remain valid.
    UI rollback retains capture; backend rollback/resume preserves recorded
    evidence and marks the untrusted interval/partial boundary days unknown.
    An older aggregate writer must not erase newly stored Progress evidence.
35. **AC-35 — Loading/errors/races.** Slow, failed, missing/null or malformed
    activity/coverage data never yields a successful empty calendar or zero gap.
    Retry recovers. After rapid range/day/zone/user changes, a delayed prior
    response cannot replace the new selection's data. Partial section failures
    are labeled rather than used to compute incomplete summaries.
36. **AC-36 — Ownership.** Two owners with overlapping dates/titles receive only
    their own events, coverage, counts, gaps and task links. Existing forbidden/
    not-found behavior applies to unavailable targets; changing reporting bounds
    or timezone cannot widen access.
37. **AC-37 — Accessible/mobile presentation.** Validate keyboard-only and
    screen-reader day labels, focus, navigation, gap selection, status/error
    announcements, equivalent date-ordered list and legend. At 320px and 200% zoom
    in light/dark themes, controls and long names remain usable; active/unknown/
    future/gap states remain distinct without color and without hover.
38. **AC-38 — Scope/tooling/performance.** Progress contains no Notes scorecard,
    streak, consistency percentage, adaptive color recommendation, XP intensity,
    daily adherence or habit engine. The bounded 1,000-task/10,000-unit fixture
    meets the stated p95 target with no full-ledger browser download, silently
    truncated detail, or large calendar/analytics dependency. Record regression
    evidence using existing tooling plus justified small test-only additions.

## Edge cases

| Case | Required interpretation / criteria |
|---|---|
| Completion exactly at reporting midnight; browser in another zone | Local-date assignment only; AC-04-06. |
| DST missing/repeated hour, leap day, year crossover, date-domain endpoint | Calendar arithmetic and bounded validation, not elapsed-hour counts; AC-03, AC-06. |
| Reliable capture starts/resumes mid-day | Positive evidence may exist; absence cannot certify that whole day; AC-15-17, AC-34. |
| Partial coverage with an actual completion | Active day plus partial annotation; cannot be an inactive gap; AC-15, AC-19. |
| Empty current week, future-only range, all-unknown historical range | Three distinct valid states, not request errors or interchangeable zeroes; AC-17, AC-19, AC-23. |
| Final substep, automatic parent event, same-state status ledger row | One unit; do not reuse every status-event row as activity; AC-08, AC-10. |
| A recorded task is later reopened, removed, or renamed | Historical observation survives; current state/link availability may differ; AC-11-13. |
| Done override but partial substeps, or complete substeps under terminal status | Preserve weighted progress and status as separate facts; AC-08, AC-22. |
| No first activity yet, but several fully tracked empty days | Known inactivity since coverage, not inactivity since account creation; AC-29-30. |
| Equal-length gaps or equal-instant completions | Most recent gap end; stable completion identity order; AC-18, AC-25, AC-30. |
| Selected range excludes last activity/current gap | Explicit all-recorded-history scope; selection does not change those metrics; AC-28-30. |
| Restart without Postgres; current task data is unavailable afterward | Durable minimal Progress evidence remains; historical item has no broken link; AC-13, AC-31-33. |
| Store failure, uncertain rollback interval, late request response | Error/unknown, never successful empty history; AC-32, AC-34-35. |

## Non-goals

- Dashboard redesign, replacing Dashboard charts, task-detail redesign, or changes
  to task creation, scheduling, effort tracking, weights, DSA policy or statuses.
- Original-plan snapshots, immutable commitment/adherence reporting, daily
  attendance, recurring habits, rest-day calendars, streaks, streak restoration,
  missed-commitment alerts, or a habit/plan commitment engine.
- XP, productivity/mastery scores, effort-based calendar intensity, rankings,
  gamified heat levels, or adaptive color recommendations.
- Reconstructing old activity from mutable task fields/status history, backfilling
  a year, importing historical completion dates, or user-editable activity events.
- Counting repeated practice of the same recorded identity as new activity.
- A new timezone preference-management UI, auth/security model, external analytics
  service, general persistence rewrite, or deletion of old APIs/files.
- Design artifacts, implementation code, schema/API designs, commits, PR actions,
  or branch/worktree changes during this phase.

## Migration impact, rollout, rollback, and risks

### Migration impact

- Existing task/status/schedule/effort semantics and public legacy report contracts
  remain intact. Progress reporting and its durability are additive.
- Postgres needs the next unused `V<number>__<description>.sql` migration under
  `infra\sql\` (currently V8 is latest). Follow the existing runner/versioning and
  safe re-run conventions; do not edit `V1__baseline.sql` or other applied scripts.
- Persist new history/coverage so that aggregate replacement or task cascades
  cannot destroy retained evidence. Preserve it through old-client mutations,
  settings writes, targeted deletes and rollback. Physical schema/API choices
  belong to design.
- The default store currently cannot survive process termination. Design must
  supply durable backing for Progress events, coverage and deduplication evidence
  before claiming both-store support; volatile singleton state is insufficient.
  Document storage location/lifecycle/recovery requirements in the later design/
  runbook without widening this into whole-application persistence by default.
- Establish honest coverage independently of task data. No migration-generated
  events, retrospective "inactive" dates, or claimed historical adherence.

### Rollout requirements

1. Complete design against this PRD, explicitly covering every completion mutation
   path, both stores, first-start coverage, old writer compatibility, and durable
   failure behavior. Then implement with tests; tester agent validates all ACs.
2. Validate additive migration and backing-store readiness on empty and populated
   data. If durable capture/coverage is unavailable, do not expose Progress as a
   successful report.
3. Enable reliable capture at a durable, truthful per-owner start before exposing
   the new calendar/nav. Existing clients using normal completion endpoints must
   participate in capture; history must not depend on visiting Progress.
4. Show "Tracking began [local date/time, zone]; earlier dates are unknown" and
   explain first-recorded-completion semantics. Partial initial days remain partial.
5. Verify both-store restart, replay, owner isolation, timezone/DST, longest/current
   gaps, weekly workflow and old-route/API compatibility before general exposure.
   Check errors/latency with existing diagnostics; do not add third-party tracking.

### Rollback requirements

- Prefer reverting/hiding the new UI while keeping compatible durable capture
  enabled. Do not drop history, coverage, migration records or old API support.
- If rollback disables capture, durably mark the end of trusted capture before old
  writers can accept changes. On resume, establish a new trusted start. If exact
  boundaries cannot be proved, conservatively mark the whole uncertain interval
  unknown, including partial boundary days; never silently stitch continuity.
- Verify that old application saves cannot cascade-delete Progress evidence and
  that non-Postgres backing remains recoverable. Re-enabling Progress must read
  retained evidence, not create a fresh tracking start that hides loss.

### Product risks and release gates

| Risk | Required mitigation / gate |
|---|---|
| Existing default store is volatile | Both-store durability is a hard release gate. Design may choose the backing mechanism, not waive the requirement. |
| Postgres aggregate replacement/deletion removes evidence | Explicit survival and fresh-hydration tests; do not treat existing task status ledger as sufficient. |
| Users expect reopen/recomplete to count | State first-recorded-completion policy in UI help; no hidden repeated-work or streak semantics. |
| Retained names after task deletion surprise users | Describe historical-record retention; retain only minimum identity/name/time and obey existing owner/data boundaries. |
| Sparse rollout history looks like poor participation | Unknown/partial states and visible coverage start; no fabricated zeroes or past-year calendar. |
| Weekly completion conflicts with terminal status | Show status and progress separately, preserve denominator and existing progress rules. |
| Dashboard legacy figures are mistaken for new activity | Do not migrate their meaning; label Progress activity versus current plan explicitly and remove duplicate Progress destinations. |
| Coverage interruption/timezone change yields false gaps | Use complete local-day coverage, explicit as-of/zone and interruption tests; unavailable beats false precision. |
| New async view regresses workflow/navigation | Race/error tests and narrow Tasks by Week handoff regression coverage. |

## Open questions

**No unresolved product tradeoff blocks design under A1-A6.** The request expressly
requires durable support in both stores; delivering a Postgres-only or
session-only version is not an alternative authorized by this PRD.

| Follow-up | Owner / disposition |
|---|---|
| What durable backing and deployment storage lifecycle support non-Postgres Progress state? | Architect, in the next phase. This is a design/release gate, not grounds to claim the existing memory store is durable. If no supported durable medium is available, escalate that concrete blocker before implementation. |
| How are event recognition, mutation durability, coverage interruptions, and old-writer compatibility guaranteed? | Architect/engineers. Select mechanics in design; product semantics and AC-10, AC-31-34 are fixed here. |
| How does the small week-specific handoff initialize Tasks by Week without changing existing direct-entry behavior? | Architect. Current page lacks URL initialization; satisfy AC-24 without unrelated workflow redesign. |
| Should future releases recognize deliberate repeated practice or allow historical corrections? | Deferred; v1 uses A1 and has no habit/correction engine. |

## Success metrics

- **Trust:** 100% of AC-01-38 pass in tester evidence; zero known false inactive
  dates, duplicated qualifying units, or phantom empty-success states at release.
- **Durability:** Identical event/coverage results after fresh restarts in both
  stores; zero known loss across documented save/delete/rollback paths.
- **Comprehension:** In a small moderated check, at least 4 of 5 users can identify
  what made a chosen day active, explain an unknown day, distinguish current plan
  from historical activity, and locate the longest gap without calling it a
  missed commitment. Use existing/manual feedback, not a new analytics platform.
- **Usefulness/accessibility:** All five users in that check can open a surviving
  completed item or an unfinished weekly task; dedicated keyboard/mobile checks
  pass AC-37. Record unavailable deleted-task links as expected, not failure.
- **Regression/performance:** Existing relevant task/workflow tests remain green;
  bounded reports meet NFR-6 with environment and evidence recorded. No unrelated
  Dashboard/task redesign or expanded application dependency footprint.

**Handoff:** Design follows this PRD, engineers follow the approved design, and
the tester agent validates the numbered criteria. This document does not claim
implementation, testing, migration execution, deployment, or product-owner approval.
