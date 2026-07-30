# Agent Playbook

This repository's application code lives under `rora-quest/`. Treat that as the project root for most tasks.

## Project layout

- `rora-quest/source/apps/api` - .NET 8 backend (`RoraQuest.sln`)
- `rora-quest/source/apps/web` - Next.js 14 frontend
- `rora-quest/infra` - deployment/infrastructure assets
- `rora-quest/docs/runbooks` - operational documentation

## Local setup

1. Backend prerequisites: .NET SDK 8.x
2. Frontend prerequisites: Node.js 20+

```powershell
# from repository root
Set-Location rora-quest\source\apps\web
npm ci

Set-Location ..\api
dotnet restore .\RoraQuest.sln
```

## Build and validation commands

```powershell
# frontend
Set-Location rora-quest\source\apps\web
npm run lint
npm run build

# backend
Set-Location rora-quest\source\apps\api
dotnet build .\RoraQuest.sln -c Release
```

## Run commands

```powershell
# backend api
Set-Location rora-quest\source\apps\api\src\RoraQuest.Api
dotnet run

# frontend dev server
Set-Location rora-quest\source\apps\web
npm run dev
```

## Guardrails

- Keep changes scoped to the relevant app (`api`, `web`, or `infra`).
- Do not commit editor-local artifacts (for example `.vs/`, user-specific files).
- Prefer minimal, focused patches; avoid broad refactors unless requested.
