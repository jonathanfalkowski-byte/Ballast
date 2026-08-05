# ─────────────────────────────────────────────────────────────────────────────
# Ballast — ship.ps1
#
# Stage, commit and push the Ballast repo in one action.
#
# Why this exists: Claude can write files to this machine but cannot push. The
# bridge it works through is a Linux shell over a mounted folder, and the
# GitHub credentials live in Windows Credential Manager, which that shell
# cannot see. A push from there hangs on an auth prompt - and a hung git is
# what leaves .git\index.lock behind and blocks your own git for hours.
#
# So the push stays here, where the credentials are:
#
#     powershell -ExecutionPolicy Bypass -File .\scripts\ship.ps1 -Message "..."
#     powershell -ExecutionPolicy Bypass -File .\scripts\ship.ps1 -DryRun
#
# Nothing about the rule book version is written into this script. It is read
# out of ballast-rules.txt every run - the first version of this script had
# "v12" typed into its closing line and was still saying so at v14.
# ─────────────────────────────────────────────────────────────────────────────

param(
    [string]$Message = "",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$repo = "C:\Users\Admin\Downloads\ballast-src\ballast"

if (-not (Test-Path (Join-Path $repo ".git"))) {
    Write-Host "No git repository at $repo" -ForegroundColor Red
    exit 1
}
Set-Location $repo

# A lock left by an interrupted git blocks everything until it is cleared, and
# git's own error message does not say so plainly.
$lock = Join-Path $repo ".git\index.lock"
if (Test-Path $lock) {
    Write-Host "There is a stale .git\index.lock." -ForegroundColor Yellow
    Write-Host "If no other git is running, delete it and try again:" -ForegroundColor Yellow
    Write-Host "    Remove-Item '$lock'" -ForegroundColor Yellow
    exit 1
}

# Which branch. Committing onto a topic branch and wondering why the site has
# not changed has already cost an afternoon once.
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
Write-Host "`nBranch: $branch" -ForegroundColor Cyan
if ($branch -ne "main") {
    Write-Host "  NOT main. Vercel deploys main, so this will not reach the site" -ForegroundColor Yellow
    Write-Host "  until it is merged. Ctrl+C now if that is not what you meant." -ForegroundColor Yellow
}

# The rule book version, read from the file rather than remembered.
$version = ""
$rulesPath = Join-Path $repo "ninja\Ballast\ballast-rules.txt"
if (Test-Path $rulesPath) {
    $line = Select-String -Path $rulesPath -Pattern '^VERSION\|' | Select-Object -First 1
    if ($line) { $version = ($line.Line -split '\|')[1].Trim() }
}

# The site ships its own copy. If the two disagree the add-on will pull a rule
# book that does not match the one on the page, and nothing will say so.
$webPath = Join-Path $repo "apps\web\lib\propFirmRules.ts"
if ((Test-Path $webPath) -and $version) {
    $webLine = Select-String -Path $webPath -Pattern 'RULES_VERSION\s*=\s*(\d+)' | Select-Object -First 1
    if ($webLine -and $webLine.Matches[0].Groups[1].Value -ne $version) {
        Write-Host "`n  Rule book versions disagree:" -ForegroundColor Red
        Write-Host "    add-on : v$version" -ForegroundColor Red
        Write-Host "    website: v$($webLine.Matches[0].Groups[1].Value)" -ForegroundColor Red
        Write-Host "  Fix that before shipping, or the add-on pulls a different book." -ForegroundColor Red
        exit 1
    }
}

Write-Host "`n== What has changed ==" -ForegroundColor Cyan
git status --short apps/web ninja

git add apps/web ninja

$staged = git diff --cached --name-only
if (-not $staged) {
    Write-Host "`nNothing staged under apps/web or ninja. Nothing to ship." -ForegroundColor Yellow
    exit 0
}

Write-Host "`n== Staged ==" -ForegroundColor Cyan
$staged | ForEach-Object { Write-Host "   $_" }

# No canned message. A default that describes last month's work is worse than
# no default at all - it puts a confident, wrong description on the commit.
if (-not $Message) {
    $files = ($staged | ForEach-Object { Split-Path $_ -Leaf }) -join ", "
    $Message = "Ballast update" + $(if ($version) { " (rule book v$version)" } else { "" }) + "`n`n$files"
    Write-Host "`nNo -Message given, so this commit will say:" -ForegroundColor Yellow
    Write-Host "   $Message" -ForegroundColor Yellow
    Write-Host "Ctrl+C now if you want to write a real one." -ForegroundColor Yellow
    Start-Sleep -Seconds 4
}

if ($DryRun) {
    Write-Host "`n== Dry run, nothing committed ==" -ForegroundColor Yellow
    Write-Host $Message
    git reset | Out-Null
    exit 0
}

Write-Host "`n== Committing ==" -ForegroundColor Cyan
git commit -m $Message
if ($LASTEXITCODE -ne 0) { Write-Host "Commit failed - nothing pushed." -ForegroundColor Red; exit 1 }

Write-Host "`n== Pushing ==" -ForegroundColor Cyan
git push
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nPush failed. The commit is safe on this machine; nothing is lost." -ForegroundColor Red
    Write-Host "Run 'git push' again once you know why." -ForegroundColor Red
    exit 1
}

Write-Host "`nPushed." -ForegroundColor Green
if ($branch -eq "main") {
    Write-Host "Vercel will pick it up in a minute or two." -ForegroundColor Green
    if ($version) {
        Write-Host "Then hit 'Check for updates' in Ballast's Setup tab to pull rule book v$version." -ForegroundColor Green
    }
} else {
    Write-Host "This is branch '$branch'. Merge it into main before the site changes." -ForegroundColor Yellow
}
