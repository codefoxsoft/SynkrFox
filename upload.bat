@echo off
setlocal enabledelayedexpansion

:: ── Cleanup stale locks ──────────────────────────────────────────────────────
if exist ".git\index.lock" del /f /q ".git\index.lock"

set REMOTE_URL=https://github.com/codefoxsoft/SynkrFox.git
set BRANCH=main

:: ── Check if this is already a git repo ──────────────────────────────────────
if not exist ".git" (
    echo [SETUP] No git repo found. Initializing...
    git init
    git remote add origin %REMOTE_URL%
    git checkout -b %BRANCH%
    goto :do_commit
)

:: ── Repo exists - ensure remote is set ───────────────────────────────────────
git remote get-url origin >nul 2>&1
if errorlevel 1 (
    echo [SETUP] Adding missing remote...
    git remote add origin %REMOTE_URL%
)

:: ── Ensure we are on main branch ─────────────────────────────────────────────
git checkout %BRANCH% >nul 2>&1
if errorlevel 1 (
    git checkout -b %BRANCH% >nul 2>&1
)

:: ── Check for uncommitted changes ────────────────────────────────────────────
for /f %%i in ('git status --porcelain') do set HAS_CHANGES=1

if not defined HAS_CHANGES (
    echo [OK] No local changes. Pushing any unpushed commits...
    git push origin %BRANCH%
    echo [DONE] Up to date.
    goto :end
)

:do_commit
:: ── Stage and commit ─────────────────────────────────────────────────────────
echo.
echo [STAGE] Staging all changes...
git add -A

echo.
echo [STATUS] Files staged:
git status --short

echo.
set /p COMMIT_MSG="Enter commit message (press Enter for 'Update'): "
if "!COMMIT_MSG!"=="" set COMMIT_MSG=Update

echo.
echo [COMMIT] !COMMIT_MSG!
git commit -m "!COMMIT_MSG!"

echo.
echo [PUSH] Pushing to %REMOTE_URL% ...
git push -u origin %BRANCH%

if errorlevel 1 (
    echo.
    echo [WARN] Push failed. If this is the first push, GitHub may need
    echo        you to authenticate. Try running: git push -u origin main
) else (
    echo.
    echo [DONE] Pushed successfully!
)

:end
endlocal
echo.
pause
