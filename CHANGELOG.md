# Changelog

All notable changes to SAUDICO Federate are documented in this file.

## [v1.1.0-rc1] - 2025-01-XX

### Added
- **ACC Cloud Model Support**: Full OAuth 2.0 PKCE authentication flow for Autodesk Construction Cloud
- **SourceKind Enum**: Discriminated union for LocalFile, FileCentral, and AccCloudModel source types
- **AccCloudDescriptor**: Structured cloud model identification with Region, ProjectGuid, ModelGuid, VersionGuid
- **Region Validation**: Explicit support for US (autodesk.io) and EMEA (emea.autodesk.io) regions; unknown regions blocked
- **Cloud Model Opening**: Correct `DoNotDetach` option for ACC models, `DetachAndPreserveWorksets` for file centrals
- **Workset Visibility**: All user worksets explicitly set Visible in temporary export views
- **Source Integrity for Cloud**: Marked as NotApplicable for cloud sources, validated for file sources
- **CSV Traceability**: Extended reports include ACC account, project, folder, version information

### Changed
- **Annotation Handling**: Global annotation hiding disabled due to Navisworks View exporter incompatibility with workshared/ACC models (checkbox preserved as UI no-op)
- **ViewBuilder Architecture**: Denylist-based selective exclusions instead of unsafe global category hiding
- **ModelCurve Handling**: Individual element hiding via `HideElements()` instead of category-wide OST_Lines exclusion
- **Export Options**: Only valid Revit 2024/2025 NavisworksExportOptions properties used (removed nonexistent properties)
- **Error Logging**: All previously swallowed exceptions now logged with stage, type, message, and job context
- **NWC Validation**: Output file existence and size validation before marking job Success
- **Documentation**: README fully updated to reflect ACC capabilities and known limitations

### Fixed
- **Critical**: ACC cloud models now open with `OpenOptions.DoNotDetach` (previously invalid Detach option)
- **Critical**: File central models correctly use `DetachAndPreserveWorksets`
- **Critical**: Removed all nonexistent NavisworksExportOptions properties (CurrentViewOnly, DivideViews, ApplySurveyPoint, etc.)
- **Critical**: Empty catch blocks replaced with detailed exception logging
- **Critical**: NWC exporter availability validated via `OptionalFunctionalityUtils.IsNavisworksExporterAvailable()`
- **Medium**: Analytical model categories now use `AreAnalyticalModelCategoriesHidden` property correctly
- **Medium**: Imported CAD handling coherent with `AreImportCategoriesHidden` and `ConvertLinkedCADFormats`
- **Medium**: Atomic file operations include backup/rollback strategy for overwrite safety
- **Medium**: IFC exclusion setting accurately defined (no universal ExcludeIfc property exists)

### Security
- **Token Storage**: Access tokens memory-only, refresh tokens encrypted via DPAPI CurrentUser scope
- **No Source Writes**: Verified zero occurrences of Save(), SaveAs(), SynchronizeWithCentral(), Relinquish(), Publish()
- **Packaging**: Excluded apssettings.local.json, aps-token.dat, logs, and test credentials from distribution

### Known Issues
- Annotation exclusion checkbox is currently non-functional for workshared/ACC models due to Navisworks exporter rejecting views with `AreAnnotationCategoriesHidden=true`
- Only US and EMEA regions validated; other APS regions will be blocked pending testing
- Revit 2024 and 2025 cloud models must match respective Revit versions (no cross-version cloud access)

## [v1.0.8] - Previous Release

### Summary
- Local and file-based central model support
- Basic NWC export with configurable settings
- WPF UI with batch processing
- CSV reporting

---

**Note:** v1.1.0-rc1 is a release candidate requiring runtime validation on Revit 2024 and 2025 with local, central, and ACC cloud models before production deployment.
