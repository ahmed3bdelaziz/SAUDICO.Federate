using System;

namespace SAUDICO.Federate.ACC.DataManagement;

/// <summary>
/// A single browsable entry — a hub, project, folder, or RVT file — as
/// returned by <see cref="IAccDataManagementClient"/>. One shared shape is
/// used at every navigation level so the browser UI can bind a single list
/// regardless of depth. Never carries anything beyond what APS Data
/// Management's GET responses supply; <see cref="LastModifiedUtc"/> and
/// <see cref="VersionNumber"/> are null when the API did not supply them
/// (e.g. hubs and projects never carry either).
/// </summary>
public sealed class AccBrowseNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public AccNodeKind Kind { get; set; }
    public DateTime? LastModifiedUtc { get; set; }
    public int? VersionNumber { get; set; }

    public bool IsNavigable => Kind == AccNodeKind.Hub || Kind == AccNodeKind.Project || Kind == AccNodeKind.Folder;
}
