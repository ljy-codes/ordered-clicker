# 2.2.0 Implementation and Progress

Date: 2026-11-06 (Asia/Shanghai)
Baseline: 2203695

## Scope and Compatibility

The user approved all eleven reviewed defects, rehearsal/step execution,
optional target-window binding, a custom screen-stability region, and a
version increase. Implement in the existing checkout. Preserve v4 profiles;
new optional fields default to the existing behavior. No database, cache,
queue, network service, or production data is involved.

## Shared Contracts

- `TargetWindowBinding`: `Platform`, `ApplicationId`, `WindowTitle` strings.
  A null binding disables the guard. Bindings are platform-specific.
- `ClickProfile.TargetWindow` and `ExecutionPlan.TargetWindow`: optional
  `TargetWindowBinding`.
- `ScreenStabilitySettings.Region`: optional `ScreenBounds`. Null retains
  cloud-region / virtual-desktop detection.
- `ExecutionPlan.SourceProfile`: optional original profile snapshot, used
  to restore the exact editing state together with the checkpoint.
- `ExecutionRunMode`: `Normal`, `Rehearsal`, `Step`.
- `ExecutionRunOptions`: `Mode` and optional
  `Func<ExecutionPlanPoint, string?> SafetyCheck`; null/empty means safe,
  otherwise pause before sending input and show the reason.
- Append optional `ExecutionRunOptions? options = null` to the existing
  five-argument engine `ExecuteAsync` overload.
- Rehearsal moves once per enabled point, never sends clicks (including
  cleanup mouse-up), never overwrites normal checkpoints, and reports zero
  delivered clicks. Step executes one point including all its clicks and
  waits, then pauses before the next point. Stop remains available.
- Optional `IMouseController.IsAtPosition(x,y)` allows drift detection.
  Production adapters implement it; a default preserves existing adapters.

## Tasks

- [x] Shared execution: per-click positioning, drift pause, safety guard,
  rehearsal and step modes; region/binding validation, fingerprints,
  profile snapshots, backward-compatible models; focused engine tests.
- [x] Persistence: atomic profile replacement and orphan recovery, settings
  access-error fallback, bounded log pruning; isolated fault regressions.
- [x] Windows: restart recovery integration, profile-scoped undo, current
  display validation, resume countdown, window capture/guard, rehearsal,
  step controls and custom-region capture; UI regressions.
- [x] macOS: coordinate editing/draft recovery, ordered hotkey capture,
  HUD lifecycle, safe resume, equivalent new controls/platform guards;
  managed contract and UI tests. Native Mac validation remains unexecuted.
- [x] Release/docs: precise attachment replacement, 2.2.0 metadata,
  README/user-guide changes and matching installer tests.
- [x] Verification: Windows suite, installer contracts, shared/Mac managed
  builds where supported, desktop screenshots. Record
  actual results and unexecuted macOS native cases in `docs/test.md`.
- [x] Package the Windows 2.2.0 installer and portable application into
  `D:\专用工具\连点器\交付产品`, retaining the documented deliverable layout.

## Safety and Verification

Use fake mouse controllers for execution tests. Do not click the user's
desktop. Tests use isolated temporary directories and fake window probes.
Retain regression source and evidence in `docs/test.md`; remove only
disposable outputs when useful. Do not assert macOS native validation on a
Windows host. Git publication is a separate final integration action.

## Progress

- Baseline checkout is clean; Windows SDK is .NET 10.0.400.
- The existing product version is 2.1.0, profile format is v4.
- Design accepted through the user's explicit instruction to implement the
  reviewed fixes and all three proposed user-experience improvements.
- Windows implementation is wired in: checkpoint-first startup restores the
  profile snapshot; matching profile loads preserve checkpoints; undo is
  scoped to a profile; resume uses a cancellable three-second countdown.
- Windows execution tools now expose rehearsal/step, delayed foreground-window
  binding, and custom stability-region capture/preview/clear. The window
  guard checks both foreground identity and the actual top-level hit window;
  the clicker itself is always excluded. Display/session changes stop a run.
- First integration attempt reached expected compile failures because the
  new shared-model tests landed before their model implementation. This is
  not a passing baseline. Re-run after all shared contracts are present.
- Added Windows regressions for cross-profile undo, snapshot/checkpoint
  restoration, matching-profile reload, local stability settings, and pure
  window-guard checks. Execution tests must continue to use fake input.
- Shared engine, persistence, macOS, and release/documentation work proceeds
  in disjoint file scopes. No Git commit or publication has been performed.
- Windows review added guards for actual cursor hits within the movement
  tolerance, a scoped catch for damaged drafts during legacy-checkpoint
  recovery, and WTS remote-disconnect event 4. Focused UI regressions pass.
- The persistence fault regressions reproduced all three remaining defects:
  locked oversized logs, missing-file orphan recovery, and temporarily
  unreadable settings. The intermediate suite is 20/23, not a release pass;
  the persistence implementation is still in progress.
- Avalonia 11.3.7 restore first failed with TLS EOF; retry through the existing
  local proxy succeeded. No package-version or repository feed change was
  made. The first managed Mac build found a missing CapturedPoint import.
- Version 2.2.0 HTML/PDF guides were generated and verified. All ten PDF
  pages were rendered and reviewed, including the new execution and region
  pages. Refresh screenshots and guides after the final button-contrast fix.
- Final Windows suite passes 23/23; shared managed groups pass 20/20;
  Mac managed build and headless base plus nine regression groups pass.
  The guides include refreshed screenshots and pass the 2.2.0 verifier.
- Final persistence review fixed missing UTF-16 termination in the native
  rename buffer and added extended Windows paths. Regressions check new
  contents, previous-reader/backup preservation, continuous name visibility
  and long Unicode paths. Unsupported extended rename flags fall back to a
  normal same-directory move; non-NTFS filesystems remain untested.
- Windows 2.2.0 local delivery is built and verified: exactly five files,
  EXE file/product versions 2.2.0.0 / 2.2.0 and all four SHA-256 entries
  match. Removed three disposable guide/probe directories; see `docs/test.md`.

## Changed Areas

- Execution/models: `ClickExecutionEngine`, `AsyncPauseGate`,
  `ExecutionPlanService`, `ExecutionPlanFingerprintService`, `ProfileValidator`,
  `ScreenStabilityDetector`, optional run/window/region model fields.
- Persistence: `ProfileService`, `ProfileLoadResult`, `SettingsService`,
  `LogRetentionService` and their isolated regression tests.
- Windows: `MainForm` execution/profile/cloud-desktop partials and the new
  execution-tools partial, `WindowsExecutionEnvironment`, native declarations,
  mouse adapter and plan/status dialogs.
- Mac: `MainWindow` capture/execution/profile partials, `PointRow`,
  `MacNative`, `MacPlanValidation` and native window/hotkey hooks.
- Release/docs: version metadata, installer defaults, build/package scripts,
  `ci-release.yml`, guide generators, README, generated HTML/PDF and screenshots.

The profile format remains v4. New optional fields retain previous defaults;
old applications cannot enforce new window guards. No database, Redis or MQ
migration is required. Native Mac, real desktop input and non-NTFS verification
limits are recorded in `docs/test.md`.

## Publication Follow-Up

The feature commit is `ec3216b` and tag `v2.2.0`. Main CI passed. The first
tag-release build passed application tests but exposed strict-mode XML
adaptation in the installer test. The follow-up uses XPath and enables
strict mode in the test itself; the full local packaging entry re-ran
Windows 23/23 and all installer contracts successfully. Publish verified
local artifacts; retain the original tag and unchanged product sources.
