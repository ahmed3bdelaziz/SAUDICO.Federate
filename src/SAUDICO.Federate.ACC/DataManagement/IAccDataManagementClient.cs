using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SAUDICO.Federate.ACC.DataManagement;

/// <summary>
/// Read-only APS Data Management browsing. GET requests only — no hub,
/// project, folder, or item is ever created, renamed, moved, deleted, or
/// downloaded. Every method follows the response's pagination "next" link
/// until every page is collected or the caller cancels.
/// </summary>
public interface IAccDataManagementClient
{
    Task<IReadOnlyList<AccBrowseNode>> GetHubsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AccBrowseNode>> GetProjectsAsync(string hubId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccBrowseNode>> GetTopFoldersAsync(string hubId, string projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns only folders and RVT-named items (any other item type is
    /// omitted) from the given folder.
    /// </summary>
    Task<IReadOnlyList<AccBrowseNode>> GetFolderContentsAsync(string projectId, string folderId, CancellationToken cancellationToken);
}
