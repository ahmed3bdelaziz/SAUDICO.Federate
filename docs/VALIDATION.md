# Runtime Validation
1. Build Revit 2024 and Revit 2025 with zero errors.
2. Confirm no ACC assembly is produced or installed.
3. Test one local, one workshared-local and one central model copied to a test location.
4. Confirm all worksets open; central opens detached preserving worksets.
5. Confirm a unique template-free Coordination 3D view is created in memory.
6. Confirm selected checkboxes change the actual NavisworksExportOptions and view exclusions.
7. Confirm NWC exists and is non-empty.
8. Confirm the source RVT is never saved and closes with Close(false).
9. Confirm CSV report and application log are created.
