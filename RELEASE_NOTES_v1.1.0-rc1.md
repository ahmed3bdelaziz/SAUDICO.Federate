# SAUDICO Federate v1.1.0-rc1 Release Notes

## Release Candidate - Not for Production Use

**Release Date:** 2024  
**Tag:** v1.1.0-rc1  
**Previous Version:** v1.0.8  

---

## 🎯 Major Features

### ACC (Autodesk Construction Cloud) Integration
- **Full PKCE OAuth 2.0 Authentication** - Secure public client flow without client secrets
- **Cloud Model Discovery** - Recursive browsing across ACC projects and folders
- **Verified Cloud GUID Resolution** - Accurate mapping of ACC items to Revit cloud paths
- **Multi-Region Support** - US and EMEA region handling with validation
- **Cloud Model Opening** - Safe DoNotDetach workflow for cloud documents
- **Direct NWC Export from Cloud** - No download required, export directly from cloud models

### Multi-Source Federation
- **Local RVT Files** - Standard single-user models
- **File-Central Models** - Workshared models with DetachAndPreserveWorksets
- **ACC Cloud Models** - Autodesk Construction Cloud hosted models
- **Mixed Batch Processing** - Combine all source types in single federation job

---

## 🔧 Technical Improvements

### Revit API Compliance
- Corrected `NavisworksExportOptions` properties to match Revit 2024/2025 API
- Fixed `DivideFileIntoLevels` handling (user-configurable, not forced off)
- Proper `ExportScope.View` with valid `ViewId` usage
- Removed non-existent API properties (`CurrentViewOnly`, `ApplySurveyPoint`, etc.)
- Added `OptionalFunctionalityUtils.IsNavisworksExporterAvailable()` preflight check

### View Building Safety
- **Denylist-Based Visibility** - Start with all geometry visible, exclude known unwanted categories
- **Annotation Handling** - Global annotation hiding disabled due to runtime validation failures
- **Model Curve Exclusion** - Individual element hiding via `HideElements()` instead of category-wide
- **Analytical Models** - Optional exclusion via `AreAnalyticalModelCategoriesHidden`
- **Import CAD Control** - Coordinated `AreImportCategoriesHidden` + `ConvertLinkedCADFormats`

### Error Handling & Robustness
- **Exception Hierarchy Preservation** - Primary errors preserved even if cleanup fails
- **Source-Type Integrity** - ACC sources bypass file snapshot (marked NotApplicableCloudSource)
- **Atomic File Operations** - Backup/rollback strategy for NWC output replacement
- **Narrow Dialog Suppression** - Only explicitly whitelisted dialog IDs auto-dismissed
- **Batch Continuation** - Single job failure does not stop subsequent jobs

### Thread Safety
- **Revit API Context Enforcement** - All Revit operations confined to ExternalEvent handler
- **WPF UI Thread** - Modeless window runs on Revit's Dispatcher (no cross-thread API access)
- **Thread-Safe Queue** - `ConcurrentQueue<T>` for job requests with proper synchronization

---

## 🛡️ Security Enhancements

### Authentication
- **PKCE Implementation** - RFC 7636 compliant with SHA-256 code challenge
- **No Client Secret** - Public client architecture eliminates secret storage risk
- **DPAPI Token Encryption** - Refresh tokens encrypted with CurrentUser scope
- **Access Token Memory-Only** - Never persisted to disk
- **State Parameter Validation** - CSRF protection with cryptographically random state

### Data Protection
- **Logging Sanitization** - Tokens, codes, and sensitive data excluded from logs
- **Settings Isolation** - Developer overrides (apssettings.local.json) excluded from releases
- **Secret Scanning** - GitHub secret scanning enabled for repository
- **CodeQL Analysis** - Automated C# security vulnerability detection

### Access Control
- **Read-Only Source Policy** - No Save, SaveAs, SynchronizeWithCentral, or Publish operations
- **Document.Close(false)** - All sources closed without saving changes
- **Company APS Registration** - Production Client ID owned by organization (not personal account)

---

## 📊 Audit & Traceability

### CSV Report Enhancements
- Source Type (LocalFile, FileCentral, AccCloudModel)
- ACC Account/Hub Name
- ACC Project Name
- Folder Path
- Model Display Name
- Version Number (for cloud models)
- Region (US/EMEA)
- Resolution Status
- Integrity Status (NotApplicableCloudSource for ACC)
- Export Settings Preset
- Revit Version
- Add-in Version

### Logging Improvements
- Job correlation IDs for batch tracing
- Stage-level progress tracking
- Warning collection per job
- Structured JSON logging option
- Safe error messages without sensitive data

---

## 🐛 Bug Fixes

### Critical (P0)
- Fixed CI/CD pipeline rejecting ACC references (obsolete policy removed)
- Fixed unconditional file integrity capture causing ACC job failures
- Fixed exception masking when Close(false) fails after export error
- Finalized annotation exclusion behavior (documented as no-op for NWC export)

### High Priority (P1)
- Fixed potential cross-thread Revit API access in WPF layer
- Added cloud identity validation after opening (GUID matching)
- Centralized region mapping with unknown region blocking
- Corrected Navisworks options to use only valid API properties
- Implemented atomic file replacement with backup strategy
- Added exporter availability preflight check
- Narrowed dialog suppression to explicit whitelist

### Medium Priority (P2)
- Archived V1 and V2 legacy versions to separate branch
- Centralized version metadata in Directory.Build.props
- Eliminated empty catch blocks with proper logging
- Enhanced CSV schema with ACC traceability fields
- Improved settings names to align with Revit API terminology

---

## ⚠️ Known Limitations

### Annotation Exclusion
The "Exclude Annotation Categories" setting is currently a **compatibility no-op** for NWC View export. Runtime testing revealed that global annotation hiding causes the Navisworks exporter to report "No suitable geometry found" in certain workshared models. The setting is retained for future compatibility but does not currently modify view visibility.

**Workaround:** Manually hide annotation categories in the source model before exporting, or use discipline-specific view templates.

### IFC Exclusion Ambiguity
The "Exclude IFC" setting does not have a universal implementation because IFC content may exist as:
- Native converted Revit elements
- DirectShape objects
- Revit links
- Imported instances
- Coordination models

**Recommendation:** Use explicit controls for "Include Revit Links", "Include Linked CAD", and "Include Coordination Models" instead of relying on generic IFC exclusion.

### Faceting Factor
Default faceting factor remains at Revit API default (1.0). No ISO 19650 evidence supports specific values (0.5, 0.2, etc.). Projects requiring specific geometry fidelity should configure this via preset settings.

### DivideFileIntoLevels
This setting remains user-configurable (default: ON per Revit API). It does NOT create multiple NWC files; it structures the single NWC file with level information. Changing this default requires BEP/federation standard approval.

---

## 📋 Requirements

### Software
- **Revit 2024** (net48 build) OR **Revit 2025** (net8.0-windows build)
- **Navisworks Exporter** - Must be installed (optional Revit component)
- **Windows 10/11** - Required for DPAPI and WPF
- **.NET Framework 4.8** (Revit 2024) OR **.NET 8.0 SDK** (Revit 2025)

### APS Configuration
- **APS Application** - Registered with OAuth 2.0 PKCE public client
- **Redirect URI** - `http://localhost:8080/` (exact match required)
- **Scopes** - `data:read`, `user-profile:read`, `openid`
- **No Client Secret** - Public client architecture

### ACC Access
- **Autodesk Construction Cloud Account** - Valid subscription
- **Project Permissions** - Read access to models for federation
- **Multi-Region Support** - US and EMEA regions validated

---

## 🚀 Installation

### Quick Install (Both Revit Versions)
```powershell
.\INSTALL-ALL.ps1
```

### Specific Version
```powershell
.\publish.ps1 -RevitVersion 2024
.\publish.ps1 -RevitVersion 2025
```

### Manual Installation
1. Build solution in Visual Studio 2022
2. Copy `.addin` and DLL files to:
   - `%APPDATA%\Autodesk\REVIT\Addins\2024\` (Revit 2024)
   - `%APPDATA%\Autodesk\REVIT\Addins\2025\` (Revit 2025)
3. Copy `config` folder to add-in directory
4. Update `apssettings.json` with your APS Client ID

---

## 🧪 Testing Checklist

### Revit 2025 Validation
- [ ] Local RVT exports successfully
- [ ] File-central RVT opens detached and exports
- [ ] ACC EMEA cloud model opens and exports
- [ ] ACC US cloud model opens and exports (if applicable)
- [ ] Annotation option regression test (no geometry loss)
- [ ] ExportLinks=false produces single-model NWC
- [ ] ExportLinks=true includes linked models correctly
- [ ] Mixed batch (local + central + ACC) completes
- [ ] Blocked/invalid item isolation (one failure doesn't stop batch)

### Revit 2024 Validation
- [ ] net48 build succeeds
- [ ] Revit 2024 API references resolved
- [ ] Navisworks exporter for Revit 2024 detected
- [ ] Local RVT exports successfully
- [ ] File-central RVT opens and exports
- [ ] ACC compatible cloud model (Revit 2024 version) exports

### Safety Validation
- [ ] No Document.Save() calls in codebase
- [ ] No Document.SaveAs() calls
- [ ] No SaveCloudModel/SaveAsCloudModel calls
- [ ] No SynchronizeWithCentral calls
- [ ] No RelinquishOwnership calls
- [ ] No Publish calls
- [ ] All documents close via Close(false)
- [ ] One failure continues batch processing

---

## 📦 Package Contents

### Release Artifacts
- `SAUDICO.Federate.Revit2024.zip` - Revit 2024 build
- `SAUDICO.Federate.Revit2025.zip` - Revit 2025 build
- `SHA256_checksums.txt` - Cryptographic hashes
- `SBOM_v1.1.0-rc1.json` - Software Bill of Materials
- `RELEASE_NOTES_v1.1.0-rc1.md` - This document

### Excluded from Release
- `apssettings.local.json` - Developer overrides
- `aps-token.dat` - User authentication tokens
- `logs/` - Debug logs
- `V1/`, `V2/` - Archived legacy versions
- Test credentials and sample data

---

## 🔙 Rollback Instructions

If issues occur with v1.1.0-rc1:

1. **Uninstall**: Delete add-in files from `%APPDATA%\Autodesk\REVIT\Addins\[year]\`
2. **Restore Previous Version**: Download and install v1.0.8 from GitHub Releases
3. **Clear Cache**: Delete `%LOCALAPPDATA%\SAUDICO\Federate\` folder
4. **Reset Settings**: Remove `Settings.json` from roaming profile
5. **Report Issue**: Create GitHub issue with logs and reproduction steps

---

## 🆘 Support

### Documentation
- `README.md` - Installation and usage guide
- `CHANGELOG.md` - Version history
- `REVIT_API_FIXES.md` - Technical API corrections
- `PROJECT_AUDIT.md` - Architecture documentation
- `APS_AUTHENTICATION_CONTRACT.md` - OAuth 2.0 PKCE specification

### Reporting Issues
- **GitHub Issues**: https://github.com/ahmed3bdelaziz/SAUDICO.Federate/issues
- **Include**: Revit version, OS version, error logs, reproduction steps
- **Security Issues**: Contact maintainer directly (do not post publicly)

---

## ⚖️ Licensing

**Copyright © 2024 SAUDICO**. All rights reserved.

This software is proprietary and confidential. Distribution requires explicit authorization from SAUDICO.

### Third-Party Components
- **Serilog** - Apache License 2.0
- **Newtonsoft.Json** - MIT License
- **Autodesk Revit API** - Autodesk License Agreement
- **Autodesk Platform Services SDK** - Autodesk License Agreement

See `SBOM_v1.1.0-rc1.json` for complete dependency list and licenses.

---

## 🎯 Next Steps

### Before Production Release (v1.1.0)
1. Complete runtime validation matrix (all checkboxes above)
2. Gather feedback from RC1 testers
3. Resolve any critical bugs discovered in RC1
4. Update documentation based on real-world usage
5. Obtain company APS production registration
6. Enable GitHub branch protection and require PR reviews
7. Promote from `-rc1` to stable release

### Future Roadmap (Post-v1.1.0)
- Annotation exclusion refinement (per-category runtime testing)
- IFC exclusion clarity (explicit source-type controls)
- Quality presets (Standard/Enhanced/High detail faceting)
- Design Automation for Revit integration (cloud batch processing)
- BCF export for clash coordination
- Enhanced reporting (Power BI dashboard integration)

---

**This is a Release Candidate (RC1) intended for testing and feedback only. Do not deploy to production environments until v1.1.0 stable is released.**
