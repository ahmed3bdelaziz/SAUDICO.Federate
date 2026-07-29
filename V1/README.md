# SAUDICO Federate v1.0.0 RC2

Full source candidate for Revit 2024 and 2025. Features include WPF MVVM, ExternalEvent FIFO queue, local/workshared/central/ACC GUID workflows, ACC URL parsing scaffold, unique temporary export views, NWC export, overwrite policy, logging, tests, manifests, and build/deployment scripts.

## Build and install

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1 -RevitVersion 2025
.\publish.ps1 -RevitVersion 2025
```

Revit-hosted runtime validation is required before production use.
