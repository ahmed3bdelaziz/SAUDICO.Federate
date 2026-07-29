# SAUDICO Federate v1.0.8 — Consolidated Build

This is the consolidated source package for Revit 2024 and Revit 2025.

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

## Scope
Local, workshared-local, and central RVT models. ACC is deferred. NWC Exporter matching each Revit release is required.
