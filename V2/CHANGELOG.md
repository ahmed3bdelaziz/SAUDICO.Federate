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
