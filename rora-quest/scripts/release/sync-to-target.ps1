param(
    [string]$SourceRoot = "C:\Projects\Code\ScanAndConnectors\copilot-worktrees\workspace\elcg-microsoft-super-disco\rora-quest",
    [string]$TargetRoot = "C:\Personal\workspace\rora-quest"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $SourceRoot)) {
    throw "Source path not found: $SourceRoot"
}

if (-not (Test-Path $TargetRoot)) {
    New-Item -ItemType Directory -Path $TargetRoot -Force | Out-Null
}

$exclude = @(".git", "node_modules", ".next", "bin", "obj")

Write-Host "Syncing scaffold from:" $SourceRoot
Write-Host "To target location:" $TargetRoot

robocopy $SourceRoot $TargetRoot /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP /XD $exclude | Out-Null

if ($LASTEXITCODE -ge 8) {
    throw "Sync failed. Robocopy exit code: $LASTEXITCODE"
}

Write-Host "Sync completed."

