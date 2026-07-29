# SAUDICO Federate — Claude Project Instructions

## 1. Product scope
SAUDICO Federate is a Revit add-in for controlled Navisworks NWC federation export.

Supported model sources:
- Local RVT
- Workshared local RVT
- Central RVT
- ACC/Revit Cloud Models only through the approved APS model-source module

Target hosts:
- Revit 2024: .NET Framework 4.8, compilation symbol `REVIT2024`
- Revit 2025: .NET 8 Windows, compilation symbol `REVIT2025`

## 2. Absolute source-model safety rule — non-negotiable
The source RVT or Revit central/cloud model must never be changed, saved, synchronized, published, transmitted, renamed, deleted, upgraded, compacted, relinquished, or have ownership modified.

### Forbidden operations
Never call or introduce:
- `Document.Save()`
- `Document.SaveAs(...)`
- `Document.SaveCloudModel()`
- `Document.SaveAsCloudModel(...)`
- `Document.SynchronizeWithCentral(...)`
- Publish Model commands or APIs
- Relinquish operations
- Worksharing ownership changes
- File move, rename, overwrite, or delete against a source RVT
- Direct read/write use of Revit Collaboration Cache files
- Any operation that writes back to ACC, BIM 360, Autodesk Docs, or a central model

### Required opening and closing behavior
- Local non-workshared RVT: open normally, read/export only.
- Workshared local RVT: open all worksets, read/export only.
- File-based central RVT: open using `DetachAndPreserveWorksets`; never open the central for write-back.
- ACC/Revit Cloud Model: `DetachFromCentralOption.DoNotDetach`; read/export only; never save, sync, publish, or relinquish.
- Every successfully opened document must be closed in a `finally` block with `document.Close(false)`.
- If preparation or export fails, close with `Close(false)` and continue to the next queued model.
- Temporary view creation, link unloading, category changes, filters, and overrides are allowed only in the opened in-memory/detached document and must never be saved.

### Source integrity validation
For file-based sources, record before and after processing:
- Full source path
- File size
- Last-write UTC timestamp
- Read-only attribute

If file size or last-write timestamp changes, mark the job `Failed — Source Integrity Violation`, stop processing that source, and write a critical log entry.

## 3. Frozen federation defaults
Defaults are starting values only. All supported export-content and exclusion controls remain user-selectable.

Defaults:
- Export Revit links: OFF
- Export Element IDs: ON
- Export Element Properties: ON
- Parameters: All
- Coordinates: Shared
- Find Missing Materials: ON
- Divide File Into Levels: ON
- Construction Parts: OFF
- Linked CAD: OFF
- Rooms as Attributes: OFF
- Room Geometry: OFF
- Lights: OFF
- URLs: OFF
- Faceting Factor: 1.00
- Detail Level: Fine
- Overwrite existing NWC: OFF
- CSV report: ON
- Export log: ON

Default exclusions:
- Room Separation Lines
- Space Separation Lines
- Area Boundary Lines
- All Model Lines
- Annotation Categories
- Analytical Categories
- Coordination Models
- IFC Links

## 4. Export-view rules
- Create a new unique temporary isometric 3D view for every job.
- Never reuse or modify an existing user view.
- Remove any inherited View Template from the temporary view.
- Regenerate before setting Detail Level.
- Discipline is fixed to Coordination.
- Section Box OFF; Crop OFF; Scope Box none where applicable.
- Remove view filters and view-specific overrides from the temporary view.
- Apply only the user-selected federation exclusions.
- Never delete source elements.

## 5. ACC/APS architecture
- APS is a model-source provider only.
- The NWC exporter must remain independent of APS.
- Use OAuth Authorization Code with PKCE; never embed a client secret.
- Never log access tokens, refresh tokens, authorization codes, passwords, or PKCE verifiers.
- Data Management API browses hubs, projects, folders, items, and versions.
- User Profile API may show the signed-in Autodesk user.
- Forma API may be enabled, but call it only for a specifically implemented Forma feature.
- ACC browser supports multi-selection, persistent selection across folders/search, current-folder search, whole-project search, pagination using `links.next`, cancellation, caching, duplicate prevention, and Revit-version compatibility checks.
- Ordinary uploaded RVTs lacking cloud Project GUID/Model GUID metadata are not treated as openable live Revit Cloud Models.

## 6. Quick audit rule
At the beginning of a new Claude session:
1. Read this `CLAUDE.md`.
2. Read `PROJECT_AUDIT.md`.
3. Read only the files listed under **Authoritative Files** in `PROJECT_AUDIT.md` that are relevant to the current task.
4. Inspect `git status`, the solution/project list, and the newest build log only when needed.
5. Do not recursively review the entire repository unless the user explicitly requests a full audit or `PROJECT_AUDIT.md` is missing/stale.
6. Never inspect generated or copied output folders as source of truth.

Always ignore unless specifically required:
- `**/bin/**`
- `**/obj/**`
- `.vs/**`
- `.git/**`
- installed Revit Addins folders
- Collaboration Cache
- NuGet package cache
- old ZIP packages
- PDB/DLL files
- archived build logs

## 7. Audit maintenance
After a meaningful architectural or workflow change, update `PROJECT_AUDIT.md` with:
- Date and project version
- Current architecture
- Authoritative files
- Implemented features
- Deferred features
- Known build/runtime issues
- Last successful Revit 2024 build result
- Last successful Revit 2025 build result
- Last runtime validation result
- Exact next task

Do not rewrite the full audit for a trivial formatting edit. Keep it concise and factual.

## 8. Coding rules
- Preserve the established Core, Export, Logging, Shared, UI, Revit2024, Revit2025, tests, docs, build, and publish architecture.
- Make the smallest safe change required.
- Do not replace the solution with a smaller scaffold.
- Do not delete tests, logging, documentation, or scripts unless explicitly approved.
- Keep Revit API calls on the valid Revit API execution context/ExternalEvent.
- APS HTTP calls may run asynchronously with cancellation and must not freeze Revit.
- Build Revit 2024 first, then Revit 2025.
- Fix the first root-cause compiler error before addressing cascading errors.
- After source changes, clean stale `bin` and `obj`, build, and report the exact result.

## 9. Required response after every coding task
Report:
1. Root cause
2. Files changed
3. Safety impact on source/central models
4. Revit 2024 build result
5. Revit 2025 build result
6. Tests/runtime validation performed
7. Remaining risks
8. Exact next action
