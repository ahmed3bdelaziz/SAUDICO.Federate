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

    /// <summary>
    /// Recursively searches the given folder and all of its subfolders
    /// using the official Data Management folder-search endpoint (returns
    /// tip/latest versions only), following pagination. Only RVT-named
    /// items are returned.
    /// </summary>
    Task<AccSearchOutcome> SearchFolderRecursiveAsync(string projectId, string folderId, CancellationToken cancellationToken);

    /// <summary>
    /// Searches every top folder of the given project (bounded concurrency)
    /// and merges the results, deduplicated by item identity. If one top
    /// folder fails (e.g. 403), the others' results are still returned and
    /// <see cref="AccSearchOutcome.HasPartialFailure"/> is set — cancellation
    /// still aborts the whole operation.
    /// </summary>
    Task<AccSearchOutcome> SearchProjectAsync(string hubId, string projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the tip version's OSS storage URN
    /// (<c>data.relationships.storage.data.id</c>, e.g.
    /// <c>urn:adsk.objects:os.object:{bucket}/{objectKey}</c>) for a plain
    /// uploaded file, or null when the version has no storage relationship
    /// (which is the case for a Revit Cloud Worksharing model — those are
    /// opened by GUID, never downloaded). Read-only GET.
    /// </summary>
    Task<string?> GetVersionStorageUrnAsync(string projectId, string versionId, CancellationToken cancellationToken);

    /// <summary>
    /// Asks OSS for a short-lived, signed S3 download URL for an object.
    /// Read-only GET against
    /// <c>/oss/v2/buckets/{bucketKey}/objects/{objectKey}/signeds3download</c>
    /// (verified against the official aps-sdk-openapi <c>oss.yaml</c> spec;
    /// requires only the already-configured <c>data:read</c> scope).
    /// </summary>
    Task<AccSignedDownload> GetSignedDownloadAsync(string bucketKey, string objectKey, CancellationToken cancellationToken);
}
