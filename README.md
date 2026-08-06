# SAUDICO Federate v1.1.0-rc1 — Consolidated Build

This is the consolidated source package for Revit 2024 and Revit 2025 with full ACC (Autodesk Construction Cloud) support.

## One-click build and installation
Close Revit, open PowerShell in this folder, and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL-ALL.ps1
```

For one Revit release only:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL-ALL.ps1 -Revit2024Only
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL-ALL.ps1 -Revit2025Only
```

The scripts build against the installed Revit API and publish to `%APPDATA%\Autodesk\Revit\Addins\<year>`.

## Features

### Source Model Support
- **Local RVT files** — Standard single-user models
- **File-based Central/Workshared models** — Opens detached with `DetachAndPreserveWorksets`, all worksets visible
- **ACC Cloud Models** — Full OAuth 2.0 PKCE authentication, verified cloud GUID resolution, `DoNotDetach` opening

### Export Capabilities
- Single-file or level-divided NWC export (user-configurable)
- Selective category exclusions (analytical models, imported CAD, model curves, rooms/areas/spaces)
- Shared or Project coordinates (user-selectable)
- Linked model inclusion toggle
- Faceting factor control (0.1–10.0)
- Element IDs, properties, parameters, URLs, room geometry export options

### Safety & Compliance
- **Read-only guarantee:** Never saves, syncs, publishes, or relinquishes source models
- **Temporary views:** All export views are created per-job and discarded on document close
- **Source integrity validation:** Compares file metadata before/after processing for local files
- **Atomic file operations:** Temp file export with validated atomic move to prevent corruption
- **ISO 19650-aligned audit trail:** CSV reports with timestamps, source identity, settings snapshot, success/failure status

## Requirements

- **Revit 2024 or Revit 2025** with latest updates
- **Navisworks NWC Exporter** matching your Revit version (must be installed separately)
- **.NET Framework 4.8** (Revit 2024) or **.NET 8.0** (Revit 2025)
- **APS Client ID** configured in `config/apssettings.json` for ACC features

## Configuration

### APS Settings (for ACC)
Edit `config/apssettings.json` or create `config/apssettings.local.json`:
```json
{
  "clientId": "YOUR_CLIENT_ID",
  "enabled": true
}
```

**Important:** Use a company-owned APS application with PKCE-enabled public client registration. Redirect URI must match exactly: `http://localhost:8080/`

### Default Export Settings
Default settings can be modified in the UI and saved via "Save as Default". Settings are stored in `%APPDATA%\SAUDICO\Federate\Settings.json`.

## Usage

1. Launch Revit
2. Click **SAUDICO Federate** tab → **Federate** button
3. Add RVT files (local, central, or browse ACC models)
4. Select output folder
5. Configure export settings (annotations, analytical, links, coordinates, etc.)
6. Click **Run** to start batch export
7. Review CSV report in output folder

## Known Limitations

- **Annotation exclusion:** The annotation visibility checkbox is currently a no-op due to Navisworks View exporter incompatibility with global annotation hiding in workshared/ACC models. This preserves geometry export reliability.
- **Cloud model regions:** Only US (autodesk.io) and EMEA (emea.autodesk.io) regions are currently validated. Unknown regions will be blocked.
- **Revit version compatibility:** ACC cloud models must match the Revit version being used (Revit 2024 cannot open Revit 2025 cloud models).

## Build from Source

Requires Visual Studio 2022 with .NET desktop development workload:

```powershell
# Build and test
.\build.ps1 -RevitVersion 2025

# Build and install to Revit Addins folder
.\publish.ps1 -RevitVersion 2025

# Install for both versions
.\INSTALL-ALL.ps1
```

## Support & Issues

Report issues at: https://github.com/ahmed3bdelaziz/SAUDICO.Federate/issues

Include:
- Revit version
- Source model type (local/central/ACC)
- Error messages from log file (`%APPDATA%\SAUDICO\Federate\logs\`)
- CSV report row for failed jobs
