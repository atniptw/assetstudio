# ModViewer Page Checks Skill

## Purpose
Use this skill to verify what to check on the ModViewer app page, especially launch-time behavior and troubleshooting signals.

This skill does **not** teach Playwright CLI mechanics.

Use `.github/skills/playwright/SKILL.md` to run browser commands.

## Scope
Focused on app-page validation for:
- Base avatar startup load on app launch
- Runtime diagnostics visibility
- Console/runtime error detection
- Troubleshooting section checks as they are added

## Launch Validation Inputs
- App URL (default local: `https://localhost:5001`)
- Expected startup state (for now: base avatar load attempted at launch)

## Required Checks (Order)
1. **Page loaded**
   - Root page renders
   - Viewer container/canvas exists
2. **Console health**
   - No uncaught exceptions
   - No fatal JS module/init errors
3. **Diagnostics section**
   - Diagnostics panel is present
   - Messages are populated
   - Startup sequence messages are in logical order (init → load → extract/render)
4. **Package/load status**
   - Status section exists
   - Status reflects completion path (success or explicit failure)
5. **Troubleshooting sections**
   - Check all troubleshooting blocks currently present in UI
   - Record any warnings/errors shown there

## Evidence Required
- Startup screenshot
- Console output summary (and full capture if errors exist)
- Network summary for startup assets
- Final pass/fail decision with reason

## Pass/Fail Rules
Pass if all are true:
- Page loads and viewer area exists
- No fatal console errors
- Diagnostics section is present and contains startup messages
- Status section exists and is consistent with diagnostics

Fail if any are true:
- Page fails to load
- Fatal console error appears
- Diagnostics/status sections missing
- Signals conflict (e.g., status says ready but diagnostics show extraction failed)

## Troubleshooting Signals Registry
When new troubleshooting UI sections are added, append them here and include in checks.

| Section Name | Expected Signal | Failure Signal | Added In |
|---|---|---|---|
| Diagnostics | Startup messages present | Empty/missing messages | Initial |
| Package Status | `Ready` or explicit terminal failure | Missing/stuck/loading forever | Initial |

## Reporting Template
- URL and timestamp
- Checks run (1-5)
- Pass/fail per check
- Overall result
- Artifacts list
- Recommended next debug target (if failed)
