# PRD: Manual DSA Task Creation

## Problem

DSA tasks can only be created through the checklist import flow today. That makes it hard to add a single ad-hoc DSA problem without building a bulk import first.

## Goal

Allow users to create DSA tasks manually from the task form while keeping the existing DSA workflow rules after creation.

## Scope

### In scope

- Allow manual creation of tasks in DSA categories.
- Keep automatic DSA sub-step seeding on create.
- Keep existing DSA restrictions on manual status changes and sub-step structure edits after creation.
- Update the task creation UI so DSA categories are selectable.
- Update tests to cover manual DSA creation and the existing DSA restrictions.

### Out of scope

- Changing the DSA checklist import flow.
- Relaxing the existing DSA post-creation workflow restrictions.
- Changing duplicate-title import behavior.

## Acceptance criteria

- A user can create a task in a DSA category from the task form.
- The created task gets the standard DSA sub-step template.
- The created task still cannot be manually moved to arbitrary statuses or have sub-step structure edited.
- The task creation UI no longer blocks DSA categories.
