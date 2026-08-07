using System;
using System.Collections.Generic;
using SAUDICO.Federate.Shared;

namespace SAUDICO.Federate.Core;

/// <summary>
/// Specifies the type of source model for proper opening strategy
/// </summary>
public enum SourceKind
{
    LocalFile,      // Regular local RVT file
    FileCentral,    // File-based central/workshared model
    AccCloudModel   // Autodesk Construction Cloud cloud model
}

/// <summary>ACC browsing and audit identifiers. These values are strings and are never parsed as GUIDs.</summary>
public sealed class AccSelection
{
    public string HubId { get; init; } = string.Empty;
    public string AccProjectId { get; init; } = string.Empty;
    public string FolderId { get; init; } = string.Empty;
    public string ItemId { get; init; } = string.Empty;
    public string VersionId { get; init; } = string.Empty;
    public string StorageUrn { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string VersionNumber { get; init; } = string.Empty;
    public DateTimeOffset? VersionModifiedAt { get; init; }
}

/// <summary>Verified identity used exclusively to construct a Revit cloud ModelPath.</summary>
public sealed class RevitCloudIdentity
{
    public string Region { get; init; } = string.Empty;
    public Guid RevitProjectGuid { get; init; }
    public Guid RevitModelGuid { get; init; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Region)
        && RevitProjectGuid != Guid.Empty
        && RevitModelGuid != Guid.Empty;
}

/// <summary>Combines ACC audit metadata with a separately verified Revit cloud identity.</summary>
public sealed class AccCloudDescriptor
{
    public AccSelection Selection { get; init; } = new();
    public RevitCloudIdentity RevitIdentity { get; init; } = new();
    public string IdentityResolutionMethod { get; init; } = string.Empty;
    public string IdentityResolutionEvidence { get; init; } = string.Empty;

    public string DisplayName => Selection.DisplayName;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RevitIdentity.Region))
            throw new InvalidOperationException("ACC cloud descriptor is missing Region.");
        if (RevitIdentity.RevitProjectGuid == Guid.Empty)
            throw new InvalidOperationException("ACC cloud descriptor is missing a verified Revit Project GUID.");
        if (RevitIdentity.RevitModelGuid == Guid.Empty)
            throw new InvalidOperationException("ACC cloud descriptor is missing a verified Revit Model GUID.");
    }

    public override string ToString() => $"{DisplayName} ({RevitIdentity.RevitModelGuid:N})";
}

public sealed class FileSourceDescriptor
{
    public string Path { get; init; } = string.Empty;
    public bool IsCentral { get; init; }
}

public sealed class SourceDescriptor
{
    public SourceKind Kind { get; init; }
    public FileSourceDescriptor? File { get; init; }
    public AccCloudDescriptor? Cloud { get; init; }
}

/// <summary>
/// Export settings snapshot for reproducible NWC generation
/// ISO 19650-aligned: frozen per job for audit traceability
/// </summary>
public sealed class ExportSettings
{
    // === Output Policy ===
    public bool Overwrite { get; set; } = false;       // Allow overwriting existing NWC
    public bool Log { get; set; } = true;              // Enable structured logging
    public bool Csv { get; set; } = true;              // Generate CSV audit report
    
    // === Geometry & Quality ===
    public double Faceting { get; set; } = 1.0;        // Faceting factor (1.0 = default)
    public DetailMode Detail { get; set; } = DetailMode.Fine;  // View detail level
    
    // === Scope Control ===
    public bool ExportLinks { get; set; } = false;     // Include linked Revit models
    public bool LinkedCad { get; set; } = false;       // Convert linked CAD formats
    public bool DivideLevels { get; set; } = true;     // Divide file into levels (API default)
    public bool Parts { get; set; } = false;           // Export parts
    
    // === Element Properties ===
    public bool ElementIds { get; set; } = true;       // Export element IDs
    public bool Properties { get; set; } = true;       // Export element properties
    public ParamMode Parameters { get; set; } = ParamMode.All;  // Parameter export mode
    public bool RoomAttributes { get; set; } = false;  // Export rooms as attributes
    public bool RoomGeometry { get; set; } = false;    // Export room geometry
    public bool Lights { get; set; } = false;          // Convert lights
    public bool Urls { get; set; } = false;            // Export URLs
    public bool FindMaterials { get; set; } = true;    // Find missing materials
    
    // === Coordinate System ===
    public CoordMode Coordinates { get; set; } = CoordMode.Shared;  // Shared/Project coordinates
    
    // === Selective Exclusions (Denylist Approach) ===
    public bool ExRoom { get; set; } = true;           // Exclude room separation lines
    public bool ExSpace { get; set; } = true;          // Exclude space separation lines
    public bool ExArea { get; set; } = true;           // Exclude area boundaries
    public bool ExModelLines { get; set; } = true;     // Exclude model curves
    public bool ExAnnotations { get; set; } = true;    // Request annotation exclusion (compatibility no-op)
    public bool ExAnalytical { get; set; } = true;     // Exclude analytical model categories
    public bool ExCoordination { get; set; } = true;   // Exclude coordination categories
    public bool ExIfc { get; set; } = true;            // Exclude IFC content (precise definition required)
    
    /// <summary>
    /// Creates a shallow copy for job snapshot
    /// </summary>
    public ExportSettings Copy() => (ExportSettings)MemberwiseClone();
    
    /// <summary>
    /// Returns default production settings
    /// </summary>
    public static ExportSettings Default() => new();
}

/// <summary>
/// Job definition supporting multiple source types (local, central, ACC cloud)
/// </summary>
public sealed class Job
{
    // === Source Identification ===
    public SourceKind SourceKind { get; set; } = SourceKind.LocalFile;
    public string? LocalFilePath { get; set; }         // For LocalFile and FileCentral
    public AccCloudDescriptor? CloudDescriptor { get; set; }  // For AccCloudModel
    
    // Legacy compatibility property - returns LocalFilePath or CloudDescriptor.DisplayName
    public string Source
    {
        get => LocalFilePath ?? CloudDescriptor?.DisplayName ?? "";
        set
        {
            LocalFilePath = value;
            CloudDescriptor = null;
            SourceKind = SourceKind.LocalFile;
        }
    }
    
    // === Output Configuration ===
    public string OutputFolder { get; set; } = "";
    
    // === Legacy Kind Property (deprecated but kept for compatibility) ===
    public ModelKind Kind 
    { 
        get 
        {
            return SourceKind switch
            {
                SourceKind.LocalFile => ModelKind.Local,
                SourceKind.FileCentral => ModelKind.Central,
                SourceKind.AccCloudModel => ModelKind.Central,
                _ => ModelKind.Local
            };
        }
        set 
        {
            SourceKind = value == ModelKind.Central ? SourceKind.FileCentral : SourceKind.LocalFile;
        }
    }
    
    // === Execution State ===
    public State State { get; set; } = State.Queued;
    public string? Output { get; set; }
    public string? Error { get; set; }
    public string? Warnings { get; set; }
    
    // === Timing ===
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    
    // === Settings Snapshot (frozen at job creation) ===
    public ExportSettings Settings { get; set; } = ExportSettings.Default();
    
    // === Derived Properties ===
    public string Name => System.IO.Path.GetFileName(LocalFilePath ?? CloudDescriptor?.DisplayName ?? "");
    
    // === Source Integrity Validation ===
    public SourceFileSnapshot? IntegrityBefore { get; set; }
    public SourceFileSnapshot? IntegrityAfter { get; set; }
    public SourceIntegrityStatus? IntegrityStatus { get; set; }
    public string? IntegrityMessage { get; set; }
    
    /// <summary>
    /// Gets the display name for this job
    /// </summary>
    public string GetDisplayName()
    {
        return SourceKind switch
        {
            SourceKind.AccCloudModel => CloudDescriptor?.DisplayName ?? "Unknown Cloud Model",
            _ => System.IO.Path.GetFileNameWithoutExtension(LocalFilePath ?? "")
        };
    }
    
    /// <summary>
    /// Validates that the job has valid source configuration
    /// </summary>
    public bool ValidateSource(out string errorMessage)
    {
        errorMessage = "";
        
        if (SourceKind == SourceKind.LocalFile || SourceKind == SourceKind.FileCentral)
        {
            if (string.IsNullOrEmpty(LocalFilePath))
            {
                errorMessage = "Local file path is required for file-based sources";
                return false;
            }
            
            if (!System.IO.File.Exists(LocalFilePath))
            {
                errorMessage = $"Source file not found: {LocalFilePath}";
                return false;
            }
        }
        else if (SourceKind == SourceKind.AccCloudModel)
        {
            if (CloudDescriptor == null)
            {
                errorMessage = "Cloud descriptor is required for ACC cloud models";
                return false;
            }
            
            try { CloudDescriptor.Validate(); }
            catch (InvalidOperationException ex) { errorMessage = ex.Message; return false; }
        }
        
        return true;
    }
}

/// <summary>
/// Result object for export operations
/// </summary>
public sealed class Result
{
    public bool Ok { get; set; }
    public bool Skipped { get; set; }
    public string? Path { get; set; }
    public string? Error { get; set; }
    
    public static Result Success(string path) => new() { Ok = true, Path = path };
    public static Result Skip(string path, string reason = "") => new() { Skipped = true, Path = path, Error = reason };
    public static Result Fail(string error) => new() { Error = error };
}
