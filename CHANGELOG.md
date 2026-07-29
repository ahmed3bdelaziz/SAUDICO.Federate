# Changelog
## 1.0.0 Corrected Full Package
- Preserved modular Core, Export, Logging, Shared/UI and versioned Revit host architecture.
- Removed ACC as a current feature.
- Added selectable NWC content settings and selectable federation exclusions.
- Revit links default OFF; Divide into Levels default ON.
- Added Navy/Gold interface and temporary ribbon icon.
- Added settings persistence, presets, CSV reporting, validation tests and workflow checks.

## 1.0.4
- Corrected Revit API visibility check to use Element.CanBeHidden(View).
- Removed cascading curve-list Count compiler error.
- Cleared nullable and obsolete ElementId warnings in the affected sources.

## 1.0.5
- Escaped the ampersand in Window.xaml so WPF markup compiles.
- Qualified Revit UI Result types in version hosts to prevent namespace ambiguity.

## 1.0.6
- Rewrote both Revit host entry points as formatted, compiler-safe C#.
- Corrected the ribbon label to use an escaped newline inside the string literal.
- Replaced compressed tuple-based host code with explicit request classes and FIFO queue handling.

## 1.0.7
- Replaced the unreadable mixed dark/light theme with an explicit accessible light workspace.
- Added explicit foreground/background styles for DataGrid, headers, rows, cells, TextBox, ComboBox, CheckBox and buttons.
- Retained Navy and Gold branding in the application header and primary actions.

## 1.0.8
- Embedded 16 px and 32 px ribbon icons directly in both Revit host assemblies.
- Replaced cross-assembly image URIs with version-specific host-assembly pack URIs.
- Retained the accessible light UI from v1.0.7.
