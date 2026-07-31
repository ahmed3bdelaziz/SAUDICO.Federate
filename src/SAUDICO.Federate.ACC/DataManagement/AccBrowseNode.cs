using System;
using System.ComponentModel;

namespace SAUDICO.Federate.ACC.DataManagement;

/// <summary>
/// A single browsable/searchable entry — a hub, project, folder, or RVT
/// file — as returned by <see cref="IAccDataManagementClient"/>. One shared
/// shape is used at every navigation level and for flat search results, so
/// the browser UI can bind a single list regardless of depth or mode.
/// Never carries anything beyond what APS Data Management's GET responses
/// supply. The search-context fields (<see cref="Region"/> through
/// <see cref="ExtensionType"/>) are null/empty during normal single-level
/// hierarchy browsing, where they add no value, and populated for
/// recursive/entire-project search results, which have no single active
/// breadcrumb to supply that context implicitly.
/// </summary>
public sealed class AccBrowseNode : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public AccNodeKind Kind { get; set; }
    public DateTime? LastModifiedUtc { get; set; }
    public int? VersionNumber { get; set; }

    public string? Region { get; set; }
    public string? HubId { get; set; }
    public string? HubName { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? FolderId { get; set; }

    /// <summary>The item's relative path from the project root folder, as returned by the API (<c>attributes.pathInProject</c>) — never constructed/guessed locally.</summary>
    public string? FolderPath { get; set; }

    public string? ItemId { get; set; }
    public string? VersionId { get; set; }
    public string? ExtensionType { get; set; }

    /// <summary>
    /// Raw, unparsed <c>attributes.extension.data.projectGuid</c>/<c>.modelGuid</c>
    /// from the tip version resource — present only when the version's
    /// <c>attributes.extension.type</c> is the official Revit Cloud Model
    /// marker (<c>versions:autodesk.bim360:C4RModel</c>). Never guessed or
    /// constructed; null whenever the API did not supply them. See
    /// <see cref="AccCloudModelClassifier"/> for how these are verified
    /// before being trusted as real cloud-model identifiers.
    /// </summary>
    public string? CloudProjectGuid { get; set; }

    public string? CloudModelGuid { get; set; }

    public bool IsNavigable => Kind == AccNodeKind.Hub || Kind == AccNodeKind.Project || Kind == AccNodeKind.Folder;

    /// <summary>Only RVT file rows may be selected for the federation queue — folders/hubs/projects remain navigation-only.</summary>
    public bool IsSelectable => Kind == AccNodeKind.RvtFile;

    private bool isSelected;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
