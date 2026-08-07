# SAUDICO Federate — Project Audit

## Date / version
2026-08-07. Installed Revit 2024 add-in assembly version: 1.0.0.0.

## Current architecture
Multi-target solution: `Core`, `Export`, `Shared`, `SAUDICO.Federate.Logging`, `SAUDICO.Federate.ACC`, `UI` (all `net48`/`net8.0-windows` dual-targeted via `RevitYear`), plus per-host `Revit2024` (net48) and `Revit2025` (net8.0-windows) entry-point projects. `Directory.Build.props` conditions `TargetFramework`/`DefineConstants` (`REVIT2024`/`REVIT2025`) and RevitAPI/RevitAPIUI references on `RevitYear`.

## Authoritative files
- `src/Export/Engine.cs` — job pipeline (open → prepare temp view → export NWC → close → integrity check); `Nwc.Export` is the Document.Export call site.
- `src/Core/Services.cs` — `Opener` (Local/WorksharedLocal/Central/Acc opening policies), `Detector`.
- `src/Core/Models.cs` — `Job`, `ExportSettings`, `AccCloudSource`, `Result`.
- `src/Shared/Types.cs` — enums, `C` constants, `DialogWatch` (new — see below).
- `src/Revit2024/Host.cs`, `src/Revit2025/Host.cs` — `IExternalApplication`/`IExternalCommand`/`RequestHandler` per host.

## Known issue on `master` — Revit2025/Host.cs
Commit `20c3de4` ("new host file", a merge of `543bf77` into `master`) left **unresolved git conflict markers checked directly into both `src/Revit2024/Host.cs` and `src/Revit2025/Host.cs`**. This was discovered mid-session (not caused by this task) — the file had clean content when first read, then reappeared with `<<<<<<< HEAD`/`=======`/`>>>>>>>` markers baked into HEAD. One side of the conflict references `Job.SourceKind` / `Job.LocalFilePath` / a `SourceKind` enum that **do not exist anywhere in the current codebase** (confirmed via `git grep` across HEAD) — that side cannot compile against the current `Job` model in `src/Core/Models.cs`.
- `src/Revit2024/Host.cs` was resolved in this task (kept the side consistent with the current `Job`/`Core` API — the only side that compiles) and built/tested/published successfully.
- `src/Revit2025/Host.cs` was **left untouched** (still contains raw conflict markers, will not compile) — out of scope per this task's explicit instruction not to touch the Revit 2025 host. **This blocks any future Revit 2025 build until resolved.**
- Nothing was committed by this task — the resolution exists only in the working tree.

## Implemented this session — ACC NWC export diagnostics (Revit 2024 only)
Root cause of the reported failure (`Failed`, empty NWC, `NWC missing or empty after export.`, `NotApplicableCloudSource`) could not be conclusively isolated from the existing logs, because the failing run (2026-08-07 22:04:11–22:06:06, per `SAUDICO-Federation-Report-20260807-220643.csv`) had **Export Log off**, so nothing was recorded in `federate-20260807.log` for that job — `Document.Export` returned normally (no exception was ever logged for it) and the output file simply never materialized. To close this blind spot:
- `src/Export/Engine.cs` (`Nwc.Export`): added unconditional (not gated by `job.Settings.Log`) diagnostic logging around `Document.Export` — before-call snapshot (folder, export name, expected path, `OptionalFunctionalityUtils.IsNavisworksExporterAvailable()`, export scope, view id/validity, ExportLinks, DivideFileIntoLevels) and after-call snapshot (returned/threw, expected file exists/length/last-write, every `*.nwc` newly written into the output folder during the run). Confirmed via reflection against the installed `RevitAPI.dll` that `Document.Export(string,string,NavisworksExportOptions)` returns `void` and `IsNavisworksExporterAvailable()` is a static, parameterless method — code was written to match exactly, no Boolean result is read from Export.
- Added a distinct `catch` per exception type Document.Export can throw (order verified by reflection against the Revit exception hierarchy: `OptionalFunctionalityNotAvailableException` derives from `Autodesk.Revit.Exceptions.InvalidOperationException`; `InvalidPathArgumentException` derives from `Autodesk.Revit.Exceptions.ArgumentException` — more specific catches ordered first): `OptionalFunctionalityNotAvailableException`, `InvalidPathArgumentException`, `ArgumentException`, `OperationCanceledException`, `InvalidOperationException`, generic `Exception`.
- Temp export filename is now GUID-suffixed for uniqueness; still a single `.nwc` extension, no double-extension risk.
- Added `Shared.DialogWatch` (POCO, no Revit-API dependency) as an ambient single-threaded handoff. `Revit2024/Host.cs` (`RequestHandler.Execute`) subscribes to `UIApplication.DialogBoxShowing` only around the single `Engine.Run` call for the dequeued job, and — **purely observationally, never calling `OverrideResult`** — records the dialog id/message only when the message text contains "No suitable geometry" (case-insensitive). `Nwc.Export` checks `DialogWatch.Matched` after `Document.Export` returns and reports that as the job's specific failure cause instead of the generic message, when it fired. No dialog is ever auto-dismissed, matched or not — the real Revit 2024 Navisworks exporter has not been observed yet, so a hardcoded dialog ID was deliberately avoided.

## Implemented this session, round 2 — A/B export-scope diagnostic + dialog logging widened
A manual retest of round 1's build (log: 2026-08-07 22:41:19–22:41:43, 41.1s, `ExportScope=View`, `NwcExportReturned=true`, `ExpectedFileExists=false`) confirmed `Document.Export` returns normally with no exception, but produced **no `DialogId`/`DialogBoxShowing` log line at all** even though the user visually observed the "No suitable geometry found" dialog — proving round 1's dialog handler (which only recognized `TaskDialogShowingEventArgs` and only logged on a text match) either never received the event or the dialog is a different args subtype. To close this:
- `src/Shared/Types.cs`: added `DiagnosticScopeMode`/`DiagnosticSettings.ExportScopeMode` — a compile-time-only A/B switch (`View` default = production behavior; `Model` = diagnostic probe, `ExportScope=Model`/`ViewId=Invalid`). Never both in one export; flip and rebuild between tests.
- `src/Export/Engine.cs`: `Nwc.Export` now builds `NavisworksExportOptions` from the diagnostic switch instead of a hardcoded View scope. Added `LogPreExportDiagnostics` (read-only, no document modification) logging `Document.IsModelInCloud`/`IsWorkshared`, view valid/template/type/id, and total-vs-view-visible element counts (FamilyInstance, Wall, Floor, Roof, Ceiling, DirectShape via `FilteredElementCollector.GetElementCount()`) plus RevitLinkInstance/loaded-link counts, logged unconditionally immediately before every `Document.Export` call. The dialog-matched failure message is now the exact required string: "Navisworks rejected the selected export scope because no suitable geometry was found."
- `src/Revit2024/Host.cs`: `OnDialogBoxShowing` now logs **every** `DialogBoxShowing` event (DialogId, argument type, message where available) — not just matches — and recognizes both `TaskDialogShowingEventArgs` and `MessageBoxShowingEventArgs` (confirmed via reflection against `RevitAPIUI.dll`: both derive from `DialogBoxShowingEventArgs` and expose `Message`). Only the exact verified "No suitable geometry" match is dismissed, via `OverrideResult((int)TaskDialogResult.Ok)` (confirmed `Ok == 1` via reflection, matches Win32 `IDOK` too). No other dialog is ever touched or suppressed; the document is never modified from this handler.

## Deferred / not investigated this session
- Root cause still not conclusively isolated: Test A (View scope) was published with full diagnostics but not yet re-run against the ACC model; Test B (Model scope) has not been run at all — needs `DiagnosticSettings.ExportScopeMode` flipped to `Model` and republished as its own controlled build, only once Test A's fresh log is reviewed.
- Whether `ExportLinks=true` changes the result — untested, unchanged from user settings.
- ACC authentication, cloud GUID resolution, region mapping, cloud opening — untouched, per this task's explicit instructions.
- Revit2025/Host.cs merge-conflict repair — untouched, out of scope, still blocks any Revit 2025 build.

## Build / test / publish results (this session, Revit 2024 only, round 2)
- **Build**: succeeded, 0 warnings, 0 errors (one transient `CS8602` nullable warning in the new dialog handler was fixed and reverified clean).
- **Tests**: 255/255 passed.
- **Publish**: Old `%APPDATA%\Autodesk\Revit\Addins\2024\SAUDICO.Federate\` removed and replaced. Installed `SAUDICO.Federate.Revit2024.dll` last-write time 2026-08-07 22:49:27, FileVersion 1.0.0.0.
- **Revit 2025**: not built, not tested, not published, not modified.
- **Runtime status**: awaiting manual Revit 2024 Test A retest (View scope, current default) with the new per-dialog logging and element-count diagnostics, then Test B (Model scope) as a separate controlled build.

## Exact next task
Manually reproduce the failing ACC export in Revit 2024 (Test A, View scope, already published), then read the newest `%APPDATA%\Roaming\SAUDICO\Federate\logs\federate-<date>.log` for `NwcExportDiagnostics`/`DialogBoxShowing`/`NwcExportFailedDialog` lines. If the dialog still isn't captured, the "no suitable geometry" dialog is not raised through `UIApplication.DialogBoxShowing` at all and a different capture mechanism is needed. Once Test A's element counts are reviewed, flip `DiagnosticSettings.ExportScopeMode` to `Model`, rebuild/republish, and run Test B — then apply the fix per the A/B interpretation the user specified (view-config bug vs. no native geometry vs. linked-model container). Separately: resolve the `Revit2025/Host.cs` merge conflict before any 2025 work resumes.
