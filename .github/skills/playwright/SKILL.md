# Playwright CLI Skill

## Purpose
Use this skill when you need to operate Playwright CLI itself (installing, opening pages, interacting, collecting artifacts, and cleaning up).

This skill does **not** define app-specific assertions.

## Use This Skill When
- You need to run browser automation from terminal
- You need screenshots, console logs, network logs, or traces
- You need a repeatable headless check harness

## Do Not Use This Skill For
- Project-specific pass/fail criteria
- ModViewer diagnostics interpretation
- Startup health assertions

Use `.github/skills/modviewer-page-checks/SKILL.md` for those.

## Prerequisites
- Node.js and npm available
- Target app URL known (for local run, typically `https://localhost:5001`)
- Linux runtime dependencies for Chromium installed in devcontainer image

## Install
```bash
npm install -g @playwright/cli@latest
playwright-cli install --skills
```

Fallback (no global install):
```bash
npx @playwright/cli@latest install --skills
```

## Session Flow (Generic)
1. Open page/session
2. Wait for page to stabilize
3. Capture required artifacts
4. Run requested DOM/script inspections
5. Close and clean up

## Core Commands (Examples)
Open:
```bash
playwright-cli -s=session-name open https://localhost:5001
```

Capture snapshot:
```bash
playwright-cli -s=session-name snapshot
```

Capture screenshot:
```bash
playwright-cli -s=session-name screenshot
```

Dump console messages:
```bash
playwright-cli -s=session-name console
```

Dump network activity:
```bash
playwright-cli -s=session-name network
```

Run page evaluation:
```bash
playwright-cli -s=session-name eval "() => ({ title: document.title, url: location.href })"
```

Optional tracing:
```bash
playwright-cli -s=session-name tracing-start
playwright-cli -s=session-name tracing-stop
```

Close:
```bash
playwright-cli -s=session-name close
```

## Artifact Requirements
For any automation task, collect at minimum:
- 1 screenshot
- Console output
- Network output

If debugging flaky behavior, also capture trace.

## Reporting Template
- URL tested
- Commands used
- Artifacts generated (paths)
- Tool/runtime failures (if any)

## Notes
- Prefer explicit session names so repeated runs are easy to audit.
- Keep command output in the run report to make failures reproducible.
- If browser launch fails with missing shared libraries (for example `libglib-2.0.so.0`), rebuild the devcontainer so image-level dependencies from `.devcontainer/Dockerfile` are applied.
