# Copilot Instructions

Use `rora-quest/` as the effective solution root.

## Build context

- Backend: `rora-quest/source/apps/api` (`RoraQuest.sln`, .NET 8)
- Frontend: `rora-quest/source/apps/web` (Next.js 14)

## Validation expectations for code changes

- Frontend changes: run `npm run lint` and `npm run build` in `source/apps/web`.
- Backend changes: run `dotnet build .\RoraQuest.sln -c Release` in `source/apps/api`.

## Editing guidelines

- Keep edits targeted to the feature/fix requested.
- Do not touch unrelated infra/workflow files unless required by the task.
- Avoid committing local IDE artifacts or generated user files.
