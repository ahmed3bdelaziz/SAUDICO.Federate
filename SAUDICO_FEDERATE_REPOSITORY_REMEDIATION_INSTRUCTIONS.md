# SAUDICO Federate Repository Remediation Instructions

**Purpose:** Establish a reproducible build and test baseline, enforce architectural safety, correct ACC cloud-model identity and opening logic, revise NWC export behavior, and prepare SAUDICO Federate for controlled Revit 2024 and Revit 2025 runtime validation.

**Execution mode:** This is one consolidated repository instruction. Complete the phases in order. Do not split architectural intent across separate prompt files. Use small, reviewable commits, but treat this document as the single source of implementation instructions.

**Current baseline commit:** `20e15ed` (FileFinalizer added)

---

## 1. Mandatory Reading and Repository Inspection

Before modifying code, read and inspect all of the following:

1. `MASTER_MIND.md`
2. `AI_AGENT_INSTRUCTIONS.md`
3. `CHANGELOG.md`
4. `SAUDICO.Federate.sln`
5. `Directory.Build.props`
6. `Directory.Build.local.props.example` (Copy to `Directory.Build.local.props` locally)
7. Every `.csproj` under `src` and `tests`
8. `src/Core/Models.cs` (New Cloud Identity types)
9. `src/Core/OpeningPolicy.cs` (New Pure Policies)
10. `src/Core/FileFinalizer.cs` (New Atomic File Ops)
11. `src/Core/Services.cs` (Updated Opener)
12. All existing tests and CI workflow files

---

## 2. Primary Objective

Create a reproducible build and test baseline for Revit 2024 and Revit 2025, then make the minimum safe architectural and logic changes needed to support:

- correct ACC and Revit cloud identity separation
- correct Revit cloud model opening
- post-open cloud identity verification
- safe temporary export-view creation and cleanup
- documented Navisworks export options only
- local staging before destination finalization
- non-persistent source-model processing
- structured job-level error containment
- automated unit, architecture, and CI checks
- controlled runtime validation inside Revit

---

## 3. Non-Negotiable Architecture and Safety Rules

### 3.1 Revit API threading
1. Never call a Revit API method from `Task.Run`, background threads, or async continuations outside valid Revit API context.
2. All Revit model access must execute through `IExternalEventHandler`.
3. Do not retain `Document`, `Element`, `View`, `Transaction`, `ModelPath`, `UIApplication`, or `UIDocument` in background work.

### 3.2 ACC isolation
The `SAUDICO.Federate.ACC` project must **never** reference:
- `RevitAPI.dll`
- `RevitAPIUI.dll`
- `Autodesk.Revit.DB`
- `Autodesk.Revit.UI`
- Any Revit host project or Export project

### 3.3 Source-model protection
Do not introduce: `Document.Save()`, `SaveAs(...)`, `SynchronizeWithCentral(...)`, `Publish(...)`, `RelinquishOwnership(...)`, or `Close(true)`.
**Every opened batch document must ultimately use:** `document.Close(false);`

### 3.4 Cloud identity
1. ACC identifiers (`ItemId`, `VersionId`) are **strings** and never parsed as GUIDs.
2. Revit cloud paths must be built **only** from: `Region`, `RevitProjectGuid`, `RevitModelGuid`.
3. ACC version ID remains audit metadata only.
4. Do not claim the selected ACC published version is the exact version opened by Revit (Target: `LiveCloudModel`).

### 3.5 Opening policies
- **Local:** Open normally.
- **File-Central:** `DetachAndPreserveWorksets`.
- **ACC Cloud:** `DoNotDetach`.
- Batch documents must open non-visibly (`Application.OpenDocumentFile`).
- Verify identity of every opened ACC cloud model.

### 3.6 Export rules
1. Check `OptionalFunctionalityUtils.IsNavisworksExporterAvailable()` before export.
2. Do not use nonexistent options: `CurrentViewOnly`, `DivideViews`, `ApplySurveyPoint`.
3. Do not set `AreAnnotationCategoriesHidden = true`.
4. Do not call `Document.Export` while any transaction is open.
5. Export to local staging first; finalize destination after `Close(false)`.

---

## 4. Build and Environment Baseline

Run from repository root:
```powershell
git status --short
git rev-parse --short HEAD
dotnet --info
dotnet restore .\SAUDICO.Federate.sln
```

Build non-Revit projects first:
```powershell
dotnet build .\src\SAUDICO.Federate.ACC\SAUDICO.Federate.ACC.csproj -c Release --no-restore
dotnet build .\src\Core\Core.csproj -c Release --no-restore
```

Run tests:
```powershell
dotnet test .\tests\SAUDICO.Federate.Tests\SAUDICO.Federate.Tests.csproj -c Release --no-restore
```

Build Revit configurations (Requires local `Directory.Build.local.props`):
```powershell
dotnet build .\SAUDICO.Federate.sln -c Revit2024 --no-restore
dotnet build .\SAUDICO.Federate.sln -c Revit2025 --no-restore
```

---

## 5. Normalize Project and Build Configuration

1. Core, ACC, Shared, and tests must build without Revit installed.
2. Revit 2024 targets .NET Framework 4.8.
3. Revit 2025 targets .NET 8 Windows (`net8.0-windows`).
4. Revit API references use `Private=false`.
5. Revit reference locations come from `Revit2024ApiDir` / `Revit2025ApiDir` properties.
6. Tracked project files contain no developer-specific absolute paths.
7. `REVIT2024` defined only for 2024 target; `REVIT2025` only for 2025.
8. Unit tests do not load Revit assemblies.

---

## 6. Complete and Harden Architecture Safety Tests

The architecture suite must include:
- `AccAssembly_DoesNotReference_RevitAssemblies`
- `AccProject_DoesNotReference_RevitOrExportProjects`
- `Source_DoesNotContain_ProhibitedWriteCalls`
- `AccSource_DoesNotUse_RevitNamespaces`
- `BackgroundCode_DoesNotCapture_RevitApiObjects`
- `AccSource_DoesNotGuessRevitGuidsFromAccIdentifiers`

**Implementation Requirements:**
- Use Roslyn syntax analysis where possible.
- Ignore comments and string literals.
- Detect multiline invocations and named arguments.
- Fail if compiled ACC assembly cannot be found.
- Produce clear messages with violating file/location.

---

## 7. Separate ACC Identifiers from Revit Cloud Identifiers

**Domain Types Implemented in `src/Core/Models.cs`:**

```csharp
public sealed class AccSelection { /* HubId, ItemId, VersionId (strings) */ }
public sealed class RevitCloudIdentity { /* Region, RevitProjectGuid, RevitModelGuid (Guids) */ }
public sealed class AccCloudDescriptor { /* Combines both + Validation */ }
```

**Validation Rules:**
- Region must not be empty.
- `RevitProjectGuid` and `RevitModelGuid` must not be `Guid.Empty`.
- ACC identifiers remain strings.
- ACC `VersionId` is never converted to `Guid`.
- Audit metadata must never replace verified Revit identity.

---

## 8. Add a Pure Source-Opening Policy

**Implemented in `src/Core/OpeningPolicy.cs`:**
- `OpenPlan` returns plain domain values only.
- Rules: Local (normal), Central (Detach+Preserve), ACC (DoNotDetach).
- Unit tests verify every rule.

---

## 9. Correct the Revit Document Opener (PHASE 7 - TODO)

**Action Required:** Create `src/Export/RevitDocumentOpener.cs`.

**Interface:**
```csharp
public interface IRevitDocumentOpener
{
    Document OpenAccCloud(Application app, AccCloudDescriptor descriptor, OpenPlan plan);
}
```

**Implementation Steps:**
1. Construct `ModelPath` using `ModelPathUtils.ConvertCloudGUIDsToCloudPath(region, projGuid, modelGuid)`.
2. Set `DetachFromCentralOption.DoNotDetach`.
3. Open via `app.OpenDocumentFile(cloudPath, options)`.
4. **Verify Identity:** Compare `document.GetCloudModelPath()` GUIDs against requested `descriptor.RevitIdentity`.
5. Throw `CloudModelIdentityMismatchException` if mismatch.
6. Map failures to structured codes (`REVIT_SIGNIN_REQUIRED`, `OPEN_MODEL_IDENTITY_MISMATCH`, etc.).

---

## 10. Correct Authentication and Access Status

Represent APS and Revit cloud access separately:
- **ACC Browser Auth:** Connected/Disconnected
- **Revit Cloud Access:** Verified, Not Signed In, Unauthorized, Incompatible

Do not pass APS tokens to Revit desktop API.

---

## 11. Make Export Settings and Validation Testable

Create pure validator in Core/Export.Contracts:
- Validate faceting factor > 0.
- Validate output folder non-empty.
- Separate `IncludeRvtLinks` and `ConvertLinkedCadFormats`.
- Report unsupported annotation exclusion explicitly.

---

## 12. Correct Temporary Export-View Lifecycle (PHASE 9 - TODO)

**Rules:**
1. Name: `Navis-FED-{JobId}`.
2. Transaction: Create → Commit → Export → Delete → Commit.
3. Check `view.CanCategoryBeHidden(id)` before hiding.
4. No global annotation hiding.
5. Explicit `ExportWorksetPolicy`.

---

## 13. Correct NWC Export Logic (PHASE 8 - TODO)

**Rules:**
1. Check exporter availability.
2. Use `using NavisworksExportOptions`.
3. Set only valid properties: `ExportScope.View`, `ViewId`, `Coordinates`, `DivideFileIntoLevels`, `ExportLinks`, `ConvertLinkedCADFormats`, `FacetingFactor`.
4. Export to staging directory.
5. Validate staging artifact (exists, size > 0).
6. Return `ExportArtifact` result.

---

## 14. Revise Engine Execution and Error Containment (PHASE 10 - TODO)

**Order:**
Validate Job → Validate Identity → Validate Settings → Open Doc → Verify Identity → Create View → Export → Validate Staging → Delete View → Close Doc → Finalize Destination → Write Audit.

**Track Separately:** `primaryError`, `viewCleanupError`, `documentCloseError`, `destinationFinalizationError`.
**Rule:** Never replace `primaryError` with cleanup error.

---

## 15. Structured Failure Model

Add `JobFailure` type with codes:
- Opening: `ACC_AUTH_REQUIRED`, `REVIT_SIGNIN_REQUIRED`, `OPEN_MODEL_IDENTITY_MISMATCH`.
- Export: `NWC_EXPORTER_MISSING`, `OUTPUT_VALIDATION_FAILED`.

---

## 16. Destination Finalization Outside Revit

**Implemented in `src/Core/FileFinalizer.cs`:**
1. Copy staging to temp in destination dir.
2. If dest exists: Backup → Replace → Verify → Delete Backup.
3. If fail: Restore backup, preserve staging.
4. Tests required for all scenarios.

---

## 17. Logging, Redaction, and Audit Trail

Log: JobId, Stage, SourceKind, Requested Identity, Actual Identity Match, Output Size.
Redact: Tokens, Secrets, PKCE Verifier.

---

## 18. Unit and Architecture Test Requirements

All non-Revit tests must run without Revit installed.
Mark Revit-dependent tests as Integration Tests.

---

## 19. Revit Runtime Integration Tests

Create `docs/TEST_MATRIX.md` (Already created).
Test Local, Central, ACC, Identity Mismatch, Missing Exporter scenarios in Revit 2024 & 2025.

---

## 20. CI Requirements

Update GitHub Actions to:
1. Build non-Revit projects.
2. Run architecture tests.
3. Upload TRX results.
4. Skip Revit-host compilation if binaries unavailable.

---

## 21. Documentation Corrections

Update `MASTER_MIND.md` to reflect:
- "Non-persistent source-model processing" instead of "Read-only".
- Separate APS vs Revit Cloud Access.
- ACC Version ID is audit metadata only.
- Post-open identity verification requirement.

---

## 22. Commit Strategy

Recommended order:
1. `test/harden-architecture-guards`
2. `chore/build-baseline`
3. `refactor/cloud-domain-identities` (Done)
4. `test/source-open-policy`
5. `fix/revit-document-opener` (TODO)
6. `fix/nwc-export-validation` (TODO)
7. `fix/temporary-view-lifecycle` (TODO)
8. `fix/output-finalization` (Done)

---

## 23. Acceptance Criteria

**Complete when:**
- [x] Core/ACC build without Revit.
- [x] Architecture tests pass.
- [x] ACC/Revit identities separated.
- [ ] Revit Opener uses verified GUIDs.
- [ ] Identity verification implemented.
- [ ] Export uses staging/finalization.
- [ ] Runtime tests executed.

---

## 24. Required Final Report

Return: Inventory, Build Results, Test Results, Remaining Errors, Risks.

---

## 25. Stop Conditions

Stop and report if:
- Revit GUIDs cannot be resolved.
- Core/ACC requires Revit ref.
- Transaction remains open at export/close.
- Output finalization destroys existing dest without recovery.
