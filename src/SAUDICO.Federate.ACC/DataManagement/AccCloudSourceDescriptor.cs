namespace SAUDICO.Federate.ACC.DataManagement;

/// <summary>
/// A queue-safe, immutable description of an ACC-sourced RVT selected for
/// the federation queue. Carries only metadata already returned by APS
/// Data Management — never opens, downloads, or writes anything. IDs are
/// preserved exactly as the strings APS returned; nothing here is
/// constructed or guessed. <see cref="ProjectGuid"/>/<see cref="ModelGuid"/>
/// are populated only when <see cref="AccCloudModelClassifier"/> has
/// verified them (<see cref="ResolutionStatus"/> == <see cref="AccResolutionStatus.CloudModelVerified"/>);
/// otherwise both stay null. Note that a verified <see cref="ResolutionStatus"/>
/// still does not mean this model can be opened yet — ACC model opening/
/// export is a separate, not-yet-implemented step; the federation queue
/// continues to refuse every ACC job regardless of resolution status.
/// </summary>
public sealed class AccCloudSourceDescriptor
{
    public string Region { get; init; } = "";
    public string HubId { get; init; } = "";
    public string HubName { get; init; } = "";
    public string ProjectId { get; init; } = "";
    public string ProjectName { get; init; } = "";
    public string FolderId { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public string ItemId { get; init; } = "";
    public string VersionId { get; init; } = "";
    public int? VersionNumber { get; init; }
    public string DisplayName { get; init; } = "";
    public string? ExtensionType { get; init; }

    /// <summary>Only set once an identifier-resolution step (not implemented in this increment) has officially verified it.</summary>
    public string? ProjectGuid { get; init; }

    /// <summary>Only set once an identifier-resolution step (not implemented in this increment) has officially verified it.</summary>
    public string? ModelGuid { get; init; }

    public AccResolutionStatus ResolutionStatus { get; init; } = AccResolutionStatus.Unresolved;
    public string ResolutionMessage { get; init; } = "";

    /// <summary>
    /// Builds a descriptor from a search-result node. Classification never
    /// invents <see cref="AccResolutionStatus.CloudModelVerified"/> — see
    /// <see cref="AccCloudModelClassifier"/>.
    /// </summary>
    public static AccCloudSourceDescriptor FromSearchResult(AccBrowseNode node)
    {
        AccResolutionStatus status = AccCloudModelClassifier.Classify(node.ExtensionType, node.CloudProjectGuid, node.CloudModelGuid);
        bool verified = status == AccResolutionStatus.CloudModelVerified;

        string message = status switch
        {
            AccResolutionStatus.UploadedFile =>
                "Uploaded RVT file — not cloud-openable through ModelPath.",
            AccResolutionStatus.CloudModelVerified =>
                "Revit Cloud Model — identifiers verified. ACC model opening/export is not yet implemented.",
            _ => "The selected ACC model has not yet been resolved to a Revit cloud model path.",
        };

        return new AccCloudSourceDescriptor
        {
            Region = node.Region ?? "",
            HubId = node.HubId ?? "",
            HubName = node.HubName ?? "",
            ProjectId = node.ProjectId ?? "",
            ProjectName = node.ProjectName ?? "",
            FolderId = node.FolderId ?? "",
            FolderPath = node.FolderPath ?? "",
            ItemId = node.ItemId ?? node.Id,
            VersionId = node.VersionId ?? "",
            VersionNumber = node.VersionNumber,
            DisplayName = node.Name,
            ExtensionType = node.ExtensionType,
            // Only ever populated once genuinely verified — never guessed.
            ProjectGuid = verified ? node.CloudProjectGuid : null,
            ModelGuid = verified ? node.CloudModelGuid : null,
            ResolutionStatus = status,
            ResolutionMessage = message,
        };
    }
}
