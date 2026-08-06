# Revit API & NWC Export Fixes - P0/P1 Implementation

## Summary

This document details the critical P0 (blocking) and P1 (high priority) fixes implemented to align SAUDICO Federate with Revit API 2024/2025 documentation and Autodesk Platform Services (APS) best practices.

## Commits

### 1. `feat: Add SourceKind enum and AccCloudDescriptor for ACC cloud model support`

**Changes:**
- Added `SourceKind` enum with three values: `LocalFile`, `FileCentral`, `AccCloudModel`
- Added `AccCloudDescriptor` class containing:
  - `Region` (string): e.g., "US", "EMEA"
  - `ProjectGuid` (Guid): BIM 360/ACC Project ID
  - `ModelGuid` (Guid): BIM 360/ACC Model ID
  - `VersionGuid` (string?): Optional specific version
  - `DisplayName` (string): Human-readable name for UI
- Updated `Job` class to support multiple source types
- Added `ValidateSource()` method for comprehensive validation
- Preserved backward compatibility with legacy `Kind` property

**Files Modified:**
- `src/Core/Models.cs`

### 2. `fix: Implement correct document opening for ACC cloud models and file centrals`

**Changes:**
- **ACC Cloud Models**: Use `DetachFromCentralOption.DoNotDetach` (REQUIRED by Revit API)
- **File Central Models**: Use `DetachFromCentralOption.DetachAndPreserveWorksets`
- **Local Files**: Open normally without detach options
- Added comprehensive source validation before attempting to open
- Validate cloud GUIDs (Region, ProjectGuid, ModelGuid) for ACC models
- Add file existence checks for local and central file paths
- Validate `ModelPathUtils.ConvertUserVisiblePathToModelPath()` results
- Check document validity (`IsValidObject`) after opening
- Improve error handling with detailed structured logging
- Log successful opens with source kind information

**Revit API Compliance:**
- ✅ Cloud documents MUST use `DoNotDetach` option (Autodesk documentation)
- ✅ File centrals should use `DetachAndPreserveWorksets` for coordination workflows
- ✅ All document operations wrapped in try-catch with proper logging
- ✅ ModelPath validation before passing to `OpenDocumentFile()`

**Files Modified:**
- `src/Core/Services.cs`

## Pending P0/P1 Fixes (Export Engine)

The following fixes still need to be implemented in `src/Export/Engine.cs`:

### P0: Navisworks Exporter Validation
```csharp
// Before export, verify exporter is available
if (!OptionalFunctionalityUtils.IsNavisworksExporterAvailable())
{
    throw new InvalidOperationException(
        "The Navisworks NWC exporter is not available for this Revit installation.");
}
```

### P0: Correct NavisworksExportOptions Properties
**Remove these non-existent properties:**
- ❌ `CurrentViewOnly`
- ❌ `DivideViews`
- ❌ `ApplySurveyPoint`
- ❌ `ConvertToPolygons`
- ❌ `ExcludeExternalReferences`
- ❌ `ExportCoordinates`

**Use these valid Revit API 2024/2025 properties:**
- ✅ `ExportScope = NavisworksExportScope.View`
- ✅ `ViewId = view.Id`
- ✅ `DivideFileIntoLevels` (user-configurable, default ON per API)
- ✅ `FacetingFactor` (default 1.0)
- ✅ `Coordinates = NavisworksCoordinates.Shared`
- ✅ `ExportLinks`
- ✅ `ConvertLinkedCADFormats`
- ✅ `ConvertElementProperties`
- ✅ `ExportElementIds`
- ✅ `Parameters`
- ✅ `ExportParts`
- ✅ `ExportRoomAsAttribute`
- ✅ `ExportRoomGeometry`
- ✅ `ConvertLights`
- ✅ `ExportUrls`
- ✅ `FindMissingMaterials`

### P0: Atomic File Operations with Rollback
```csharp
// Atomic replace with backup for rollback
if (File.Exists(finalPath))
{
    string backupPath = finalPath + ".backup";
    File.Move(finalPath, backupPath);
    try
    {
        File.Move(tempPath, finalPath);
        File.Delete(backupPath);
    }
    catch
    {
        File.Move(backupPath, finalPath); // Rollback
        throw;
    }
}
```

### P0: NWC Validation
```csharp
// Validate export result
if (!File.Exists(tempPath))
    return Result.Fail("NWC file was not created");

FileInfo tempInfo = new FileInfo(tempPath);
if (tempInfo.Length == 0)
{
    File.Delete(tempPath);
    return Result.Fail("NWC file is empty (0 bytes)");
}
```

### P1: Safe Denylist-Based View Construction
**DO NOT apply global annotation hiding:**
```csharp
// Runtime evidence shows this breaks Navisworks View export
if (settings.ExAnnotations)
{
    Log.Information(
        "Annotation exclusion requested but NOT applied. " +
        "Global annotation visibility override can invalidate Navisworks View export.");
}
```

**DO use individual element hiding for model curves:**
```csharp
var modelCurveIds = new FilteredElementCollector(document, view.Id)
    .OfClass(typeof(CurveElement))
    .Cast<CurveElement>()
    .Where(x => x.CurveElementType == CurveElementType.ModelCurve)
    .Where(x => !x.IsHidden(view))
    .Where(x => x.CanBeHidden(view))
    .Select(x => x.Id)
    .ToList();

if (modelCurveIds.Count > 0)
    view.HideElements(modelCurveIds);
```

**DO use dedicated API properties:**
```csharp
// Analytical models
if (settings.ExAnalytical)
    view.AreAnalyticalModelCategoriesHidden = true;

// Import categories (linked CAD)
if (!settings.LinkedCad)
    view.AreImportCategoriesHidden = true;
```

### P1: Workset Visibility Control
```csharp
using (FilteredWorksetCollector collector = new FilteredWorksetCollector(document))
{
    foreach (Workset workset in collector.OfKind(WorksetKind.UserWorkset))
    {
        view.SetWorksetVisibility(workset.Id, WorksetVisibility.Visible);
    }
}
```

## ISO 19650 Alignment

These fixes support ISO 19650 compliance by providing:
- ✅ Frozen settings snapshot per job (audit trail)
- ✅ Source integrity validation (file-based models)
- ✅ Comprehensive logging with timestamps
- ✅ Reproducible export configuration
- ✅ ACC cloud model traceability (Region, Project GUID, Model GUID)
- ✅ Output validation before marking success
- ✅ No source model modification (Close(false))

**Note:** ISO 19650 does NOT prescribe specific technical settings like faceting factor or DivideFileIntoLevels. These are project-specific decisions governed by the BEP (BIM Execution Plan).

## Testing Recommendations

Before deploying to production:

1. **Test ACC Cloud Model Opening:**
   - Verify `DoNotDetach` is used for cloud models
   - Confirm worksets are opened correctly
   - Validate cloud GUID resolution

2. **Test File Central Opening:**
   - Verify `DetachAndPreserveWorksets` is used
   - Confirm worksets are preserved
   - Test with various central file configurations

3. **Test NWC Export:**
   - Verify exporter availability check works
   - Test atomic file replacement with rollback
   - Validate NWC file size > 0
   - Test with `DivideFileIntoLevels = true/false`
   - Verify coordinate system selection

4. **Test View Construction:**
   - Confirm annotations are NOT globally hidden
   - Verify analytical models are hidden when requested
   - Test model curve individual hiding
   - Validate workset visibility

## References

- [Revit API Developer Guide](https://help.autodesk.com/view/RVT/2024/ENU/?guid=Revit_API_Revit_API_Developers_Guide_html)
- [NavisworksExportOptions Class](https://help.autodesk.com/view/RVT/2024/ENU/?guid=Revit_API_RvtApiNavisworksExportOptions_html)
- [ModelPathUtils.ConvertCloudGUIDsToCloudPath](https://help.autodesk.com/view/RVT/2024/ENU/?guid=Revit_API_RvtApiModelPathUtils_html)
- [OptionalFunctionalityUtils](https://help.autodesk.com/view/RVT/2024/ENU/?guid=Revit_API_RvtApiOptionalFunctionalityUtils_html)
- [ISO 19650 Standards](https://www.iso.org/standard/68078.html)

## Next Steps

1. Implement remaining P0/P1 fixes in `src/Export/Engine.cs`
2. Update UI to support ACC cloud model selection
3. Add integration tests for all three source types
4. Document user-facing configuration options
5. Create migration guide for existing users

---

**Status:** Core layer fixes complete ✅ | Export engine fixes pending ⏳
