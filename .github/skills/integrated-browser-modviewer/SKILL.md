# Integrated Browser ModViewer Skill

## Purpose
Use this skill to run and validate ModViewer directly in the VS Code integrated browser.

This is the single supported local browser-validation workflow for ModViewer.

## Use This Skill When
- You need to run ModViewer locally and validate startup behavior.
- You need to inspect console output and page state in VS Code without switching tools.
- You need repeatable pass/fail checks for diagnostics, status, and troubleshooting sections.

## Prerequisites
- VS Code desktop on a build that includes the integrated browser (v1.109+).
- ModViewer app running locally (default URL: `https://localhost:5001`).
- Integrated browser enabled for local links:
  - `workbench.browser.openLocalhostLinks`
  - `simpleBrowser.useIntegratedBrowser`

## Launch Flow
1. Start ModViewer from the workspace:
   - `dotnet run --project AssetStudio.ModViewer/AssetStudio.ModViewer.csproj`
2. Open the integrated browser:
   - Run `Browser: Open Integrated Browser`
   - Navigate to `https://localhost:5001`
3. Open browser DevTools from the integrated browser toolbar.

## Required Checks (Order)
1. Page loaded:
   - Root page renders.
   - Viewer container/canvas exists.
2. Console health:
   - No uncaught exceptions.
   - No fatal JS module/init errors.
3. Diagnostics section:
   - Diagnostics panel exists.
   - Messages are populated.
   - Startup sequence is logical (init -> load -> extract/render).
4. Package/load status:
   - Status section exists.
   - Status reflects terminal state (ready or explicit failure).
5. Troubleshooting sections:
   - All troubleshooting blocks currently in UI are present.
   - Warnings/errors are recorded.

## Evidence Requirements
- URL tested and timestamp.
- Console summary (and full error details if failures occur).
- Startup status summary from diagnostics and package status.
- Overall pass/fail decision with reason.

## Pass/Fail Rules
Pass if all are true:
- Page loads and viewer area exists.
- No fatal console errors.
- Diagnostics section is present and contains startup messages.
- Status section exists and is consistent with diagnostics.

Fail if any are true:
- Page fails to load.
- Fatal console error appears.
- Diagnostics or status sections are missing.
- Signals conflict (for example status says ready while diagnostics report extraction failure).

## Reporting Template
- URL and timestamp
- Checks run (1-5)
- Pass/fail per check
- Overall result
- Evidence summary
- Recommended next debug target (if failed)
