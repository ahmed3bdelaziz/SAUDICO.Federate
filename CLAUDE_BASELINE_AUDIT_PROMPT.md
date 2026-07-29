# One-time Claude baseline audit prompt

Open this repository as a single project.

1. Read `CLAUDE.md` completely and treat its central/source model safety rules as non-negotiable.
2. Perform a quick targeted audit only:
   - solution file and project files
   - top-level source directory names
   - build/publish scripts
   - core job/settings/document-opening files
   - export view/exporter files
   - Revit 2024/2025 hosts
   - ACC module files, if present
   - test project names
3. Do not inspect `bin`, `obj`, `.vs`, `.git`, DLLs, PDBs, installed Addins, Collaboration Cache, NuGet cache, old ZIP packages, or archived logs.
4. Verify that no source-writing calls exist: Save, SaveAs, SaveCloudModel, SaveAsCloudModel, SynchronizeWithCentral, Publish, relinquish, or source-file overwrite/delete.
5. Verify central-model opening and `Close(false)` behavior. Do not change code yet unless a source-safety violation is found.
6. Populate `PROJECT_AUDIT.md` with concise factual findings and an authoritative-file list.
7. Report any central/source safety violation first and stop before feature work if one exists.
8. After the audit, use `PROJECT_AUDIT.md` in later sessions instead of rescanning the repository.
