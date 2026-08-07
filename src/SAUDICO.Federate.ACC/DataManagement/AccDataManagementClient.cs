using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SAUDICO.Federate.ACC.Authentication;
using SAUDICO.Federate.ACC.Errors;
using SAUDICO.Federate.ACC.Http;

namespace SAUDICO.Federate.ACC.DataManagement;

/// <summary>
/// Read-only APS Data Management client (hubs → projects → top folders →
/// folder contents). GET only; never POST/PUT/PATCH/DELETE. Uses the
/// existing <see cref="IApsAuthenticationService"/> abstraction for the
/// delegated access token — never reads or stores a token itself. On a 401
/// it attempts the existing refresh-token flow exactly once and retries the
/// same GET exactly once; if that still fails it signs out locally via the
/// same abstraction. Endpoint paths, JSON:API field names, and pagination
/// shape verified against the official
/// <c>autodesk-platform-services/aps-sdk-net</c> generated Data Management
/// SDK source (see docs/APS_DATA_MANAGEMENT_CONTRACT.md).
/// </summary>
public sealed class AccDataManagementClient : IAccDataManagementClient
{
    private const string BaseUrl = "https://developer.api.autodesk.com";
    private const string AllowedPaginationHost = "developer.api.autodesk.com";
    private const int MaxConcurrentTopFolderSearches = 3;

    private readonly IApsHttpTransport transport;
    private readonly IApsAuthenticationService authenticationService;

    public AccDataManagementClient(IApsHttpTransport transport, IApsAuthenticationService authenticationService)
    {
        this.transport = transport;
        this.authenticationService = authenticationService;
    }

    public async Task<IReadOnlyList<AccBrowseNode>> GetHubsAsync(CancellationToken cancellationToken)
    {
        Log.Information("ACC Data Management marker: HubLoadStarted");

        List<AccBrowseNode> hubs;
        try
        {
            hubs = await CollectPagesAsync(
                $"{BaseUrl}/project/v1/hubs",
                root => ParseSimpleNodes(root, "hubs", AccNodeKind.Hub),
                cancellationToken).ConfigureAwait(false);
        }
        catch (AccDataManagementException ex)
        {
            ApsApiException? apiEx = ex.InnerException as ApsApiException;
            Log.Warning(
                "ACC Data Management marker: HubLoadFailed StatusCode={StatusCode} ErrorCode={ErrorCode}",
                apiEx?.StatusCode, apiEx?.ApsErrorCode ?? ex.InnerException?.GetType().Name);
            throw;
        }

        if (hubs.Count == 0)
        {
            Log.Information("ACC Data Management marker: HubLoadReturnedEmpty");
        }

        Log.Information("ACC Data Management marker: HubLoadCompleted Count={Count}", hubs.Count);
        return hubs;
    }

    public async Task<IReadOnlyList<AccBrowseNode>> GetProjectsAsync(string hubId, CancellationToken cancellationToken)
    {
        List<AccBrowseNode> projects = await CollectPagesAsync(
            $"{BaseUrl}/project/v1/hubs/{Uri.EscapeDataString(hubId)}/projects",
            root => ParseSimpleNodes(root, "projects", AccNodeKind.Project),
            cancellationToken).ConfigureAwait(false);

        Log.Information("ACC Data Management: fetched {Count} project(s) for hub", projects.Count);
        return projects;
    }

    public async Task<IReadOnlyList<AccBrowseNode>> GetTopFoldersAsync(string hubId, string projectId, CancellationToken cancellationToken)
    {
        List<AccBrowseNode> folders = await CollectPagesAsync(
            $"{BaseUrl}/project/v1/hubs/{Uri.EscapeDataString(hubId)}/projects/{Uri.EscapeDataString(projectId)}/topFolders",
            root => ParseSimpleNodes(root, "folders", AccNodeKind.Folder),
            cancellationToken).ConfigureAwait(false);

        Log.Information("ACC Data Management: fetched {Count} top folder(s) for project", folders.Count);
        return folders;
    }

    public async Task<IReadOnlyList<AccBrowseNode>> GetFolderContentsAsync(string projectId, string folderId, CancellationToken cancellationToken)
    {
        List<AccBrowseNode> entries = await CollectPagesAsync(
            $"{BaseUrl}/data/v1/projects/{Uri.EscapeDataString(projectId)}/folders/{Uri.EscapeDataString(folderId)}/contents",
            ParseFolderContents,
            cancellationToken).ConfigureAwait(false);

        Log.Information("ACC Data Management: fetched {Count} folder/RVT entr(y/ies) for folder", entries.Count);
        return entries;
    }

    /// <summary>
    /// Recursively searches the given folder and its subfolders via the
    /// official <c>GET /data/v1/projects/{project_id}/folders/{folder_id}/search</c>
    /// endpoint, which inherently returns only tip (latest) versions — no
    /// extra filter parameter is needed for that. RVT-name filtering is
    /// applied client-side, identically to <see cref="GetFolderContentsAsync"/>.
    /// </summary>
    public async Task<AccSearchOutcome> SearchFolderRecursiveAsync(string projectId, string folderId, CancellationToken cancellationToken)
    {
        List<AccBrowseNode> results = await CollectPagesAsync(
            $"{BaseUrl}/data/v1/projects/{Uri.EscapeDataString(projectId)}/folders/{Uri.EscapeDataString(folderId)}/search",
            ParseSearchResults,
            cancellationToken).ConfigureAwait(false);

        Log.Information("ACC Data Management: recursive search returned {Count} RVT item(s)", results.Count);
        return new AccSearchOutcome { Results = results };
    }

    public async Task<AccSearchOutcome> SearchProjectAsync(string hubId, string projectId, CancellationToken cancellationToken)
    {
        IReadOnlyList<AccBrowseNode> topFolders = await GetTopFoldersAsync(hubId, projectId, cancellationToken).ConfigureAwait(false);

        using SemaphoreSlim gate = new SemaphoreSlim(MaxConcurrentTopFolderSearches);
        Task<(List<AccBrowseNode> Results, bool Failed)>[] tasks = topFolders
            .Select(folder => SearchOneTopFolderAsync(projectId, folder.Id, gate, cancellationToken))
            .ToArray();

        (List<AccBrowseNode> Results, bool Failed)[] outcomes = await Task.WhenAll(tasks).ConfigureAwait(false);

        Dictionary<string, AccBrowseNode> merged = new(StringComparer.Ordinal);
        int failedCount = 0;
        foreach ((List<AccBrowseNode> Results, bool Failed) outcome in outcomes)
        {
            if (outcome.Failed)
            {
                failedCount++;
                continue;
            }

            foreach (AccBrowseNode node in outcome.Results)
            {
                // Deduplicate by stable item identity, never by display name.
                string key = node.ItemId ?? node.Id;
                if (!merged.ContainsKey(key))
                {
                    merged.Add(key, node);
                }
            }
        }

        if (failedCount > 0)
        {
            Log.Warning(
                "ACC Data Management: entire-project search had partial results — {FailedCount} of {TotalCount} top folder(s) could not be searched",
                failedCount, topFolders.Count);
        }

        Log.Information("ACC Data Management: entire-project search returned {Count} RVT item(s)", merged.Count);

        return new AccSearchOutcome
        {
            Results = merged.Values.ToList(),
            HasPartialFailure = failedCount > 0,
            FailedFolderCount = failedCount,
        };
    }

    public async Task<string?> GetVersionStorageUrnAsync(string projectId, string versionId, CancellationToken cancellationToken)
    {
        // The version id contains a "?version=N" query segment, so it MUST be
        // escaped rather than concatenated raw.
        string url = $"{BaseUrl}/data/v1/projects/{Uri.EscapeDataString(projectId)}/versions/{Uri.EscapeDataString(versionId)}";

        using JsonDocument document = await GetWithUnauthorizedRetryAsync(url, cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
            !data.TryGetProperty("relationships", out JsonElement relationships) ||
            !relationships.TryGetProperty("storage", out JsonElement storage) ||
            !storage.TryGetProperty("data", out JsonElement storageData) ||
            !storageData.TryGetProperty("id", out JsonElement idElement) ||
            idElement.ValueKind != JsonValueKind.String)
        {
            Log.Information("ACC Data Management marker: VersionHasNoStorageRelationship");
            return null;
        }

        Log.Information("ACC Data Management marker: VersionStorageUrnResolved");
        return idElement.GetString();
    }

    public async Task<AccSignedDownload> GetSignedDownloadAsync(string bucketKey, string objectKey, CancellationToken cancellationToken)
    {
        string url =
            $"{BaseUrl}/oss/v2/buckets/{Uri.EscapeDataString(bucketKey)}/objects/{Uri.EscapeDataString(objectKey)}/signeds3download?minutesExpiration=60";

        using JsonDocument document = await GetWithUnauthorizedRetryAsync(url, cancellationToken).ConfigureAwait(false);
        JsonElement root = document.RootElement;

        AccSignedDownload result = new AccSignedDownload
        {
            Status = root.TryGetProperty("status", out JsonElement statusEl) && statusEl.ValueKind == JsonValueKind.String
                ? statusEl.GetString() ?? ""
                : "",
            Url = root.TryGetProperty("url", out JsonElement urlEl) && urlEl.ValueKind == JsonValueKind.String
                ? urlEl.GetString()
                : null,
            Size = root.TryGetProperty("size", out JsonElement sizeEl) && sizeEl.ValueKind == JsonValueKind.Number
                ? sizeEl.GetInt64()
                : null,
        };

        // Never log the signed URL itself — it is a time-limited credential.
        Log.Information(
            "ACC Data Management marker: SignedDownloadResolved Status={Status} HasUrl={HasUrl}",
            result.Status, result.Url != null);

        return result;
    }

    private async Task<(List<AccBrowseNode> Results, bool Failed)> SearchOneTopFolderAsync(
        string projectId, string folderId, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AccSearchOutcome outcome = await SearchFolderRecursiveAsync(projectId, folderId, cancellationToken).ConfigureAwait(false);
            return (new List<AccBrowseNode>(outcome.Results), false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation aborts the whole entire-project search — never treated as a per-folder failure to route around.
            throw;
        }
        catch (AccDataManagementException ex)
        {
            ApsApiException? apiEx = ex.InnerException as ApsApiException;
            Log.Warning(
                "ACC Data Management: top folder search failed, continuing with remaining folders. StatusCode={StatusCode} ErrorCode={ErrorCode}",
                apiEx?.StatusCode, apiEx?.ApsErrorCode ?? ex.InnerException?.GetType().Name);
            return (new List<AccBrowseNode>(), true);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The search endpoint's <c>data[]</c> are version resources; the items
    /// they belong to are in <c>included[]</c> (SDK-verified: <c>Search.Data</c>
    /// is <c>List&lt;VersionData&gt;</c>, <c>Search.Included</c> is
    /// <c>List&lt;ItemData&gt;</c>) — the reverse cross-reference direction
    /// from <see cref="ParseFolderContents"/>. A version's
    /// <c>relationships.item.data.id</c> resolves the owning item, whose
    /// <c>attributes.pathInProject</c> is the API-supplied relative folder
    /// path (never constructed/guessed locally) and whose
    /// <c>relationships.parent.data.id</c> is the containing folder id.
    /// </summary>
    private static IEnumerable<AccBrowseNode> ParseSearchResults(JsonElement root)
    {
        Dictionary<string, JsonElement> itemsById = ExtractIncludedByType(root, "items");

        if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement version in data.EnumerateArray())
        {
            if (GetString(version, "type") != "versions")
            {
                continue;
            }

            string versionId = GetString(version, "id") ?? "";
            string? itemId = GetRelationshipId(version, "item");
            if (itemId == null || !itemsById.TryGetValue(itemId, out JsonElement item))
            {
                // Cannot resolve the owning item from this response — skip rather than guess.
                continue;
            }

            string displayName = GetAttributeString(item, "displayName") ?? GetAttributeString(version, "displayName") ?? "";
            if (!displayName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new AccBrowseNode
            {
                Id = itemId,
                ItemId = itemId,
                VersionId = versionId,
                Name = displayName,
                Kind = AccNodeKind.RvtFile,
                LastModifiedUtc = GetAttributeDateTime(version, "lastModifiedTime") ?? GetAttributeDateTime(item, "lastModifiedTime"),
                VersionNumber = GetAttributeInt(version, "versionNumber"),
                FolderId = GetRelationshipId(item, "parent"),
                FolderPath = GetAttributeString(item, "pathInProject"),
                // The Revit-Cloud-Model marker and its projectGuid/modelGuid live on
                // the VERSION's extension, not the item's — verified against the
                // official APS "Accessing BIM 360 Design models on Revit" guidance
                // (attributes.extension.type == "versions:autodesk.bim360:C4RModel",
                // attributes.extension.data.{projectGuid,modelGuid}). The item's own
                // extension type is kept only as a display fallback when the version
                // doesn't carry one.
                ExtensionType = GetAttributeExtensionType(version) ?? GetAttributeExtensionType(item),
                CloudProjectGuid = GetAttributeExtensionDataString(version, "projectGuid"),
                CloudModelGuid = GetAttributeExtensionDataString(version, "modelGuid"),
            };
        }
    }

    private static Dictionary<string, JsonElement> ExtractIncludedByType(JsonElement root, string type)
    {
        Dictionary<string, JsonElement> map = new(StringComparer.Ordinal);

        if (!root.TryGetProperty("included", out JsonElement included) || included.ValueKind != JsonValueKind.Array)
        {
            return map;
        }

        foreach (JsonElement resource in included.EnumerateArray())
        {
            if (GetString(resource, "type") == type && GetString(resource, "id") is string id)
            {
                map[id] = resource;
            }
        }

        return map;
    }

    private static string? GetAttributeExtensionType(JsonElement entry)
    {
        if (entry.TryGetProperty("attributes", out JsonElement attrs) &&
            attrs.TryGetProperty("extension", out JsonElement ext) &&
            ext.TryGetProperty("type", out JsonElement typeEl) &&
            typeEl.ValueKind == JsonValueKind.String)
        {
            return typeEl.GetString();
        }

        return null;
    }

    /// <summary>
    /// Reads a string field from <c>attributes.extension.data</c> — the
    /// loosely-typed (per-item-type) extension payload the official SDK
    /// itself models as a generic dictionary, not a fixed schema. Never
    /// invents a value: returns null whenever the field is absent or not a
    /// string, exactly as returned.
    /// </summary>
    private static string? GetAttributeExtensionDataString(JsonElement entry, string fieldName)
    {
        if (entry.TryGetProperty("attributes", out JsonElement attrs) &&
            attrs.TryGetProperty("extension", out JsonElement ext) &&
            ext.TryGetProperty("data", out JsonElement data) &&
            data.TryGetProperty(fieldName, out JsonElement fieldEl) &&
            fieldEl.ValueKind == JsonValueKind.String)
        {
            return fieldEl.GetString();
        }

        return null;
    }

    private static int? GetAttributeInt(JsonElement entry, string propertyName)
    {
        if (entry.TryGetProperty("attributes", out JsonElement attrs) &&
            attrs.TryGetProperty(propertyName, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result))
        {
            return result;
        }

        return null;
    }

    private static string? GetRelationshipId(JsonElement entry, string relationshipName)
    {
        if (entry.TryGetProperty("relationships", out JsonElement relationships) &&
            relationships.TryGetProperty(relationshipName, out JsonElement relationship) &&
            relationship.TryGetProperty("data", out JsonElement relationshipData) &&
            relationshipData.TryGetProperty("id", out JsonElement idEl) &&
            idEl.ValueKind == JsonValueKind.String)
        {
            return idEl.GetString();
        }

        return null;
    }

    private async Task<List<AccBrowseNode>> CollectPagesAsync(
        string firstUrl,
        Func<JsonElement, IEnumerable<AccBrowseNode>> parsePage,
        CancellationToken cancellationToken)
    {
        List<AccBrowseNode> results = new();
        HashSet<string> visitedUrls = new(StringComparer.Ordinal);
        string? url = firstUrl;

        while (url != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visitedUrls.Add(url))
            {
                // A "next" link that repeats a URL we already fetched — stop
                // rather than loop forever; keep whatever was already collected.
                Log.Warning("ACC Data Management: stopped pagination — repeated next link detected");
                break;
            }

            using JsonDocument page = await GetWithUnauthorizedRetryAsync(url, cancellationToken).ConfigureAwait(false);
            results.AddRange(parsePage(page.RootElement));
            url = GetNextPageUrl(page.RootElement);
        }

        return results;
    }

    private static IEnumerable<AccBrowseNode> ParseSimpleNodes(JsonElement root, string expectedType, AccNodeKind kind)
    {
        if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement entry in data.EnumerateArray())
        {
            if (GetString(entry, "type") is string type && !string.Equals(type, expectedType, StringComparison.Ordinal))
            {
                continue;
            }

            string id = GetString(entry, "id") ?? "";
            string name = GetAttributeString(entry, "name") ?? "";

            yield return new AccBrowseNode
            {
                Id = id,
                Name = name,
                Kind = kind,
                LastModifiedUtc = GetAttributeDateTime(entry, "lastModifiedTime"),
                // Only hubs carry a region attribute; harmless no-op (null) for projects/folders.
                Region = GetAttributeString(entry, "region"),
            };
        }
    }

    /// <summary>
    /// Folder contents mix "folders" and "items". Only folders and RVT-named
    /// items are returned — everything else (non-RVT items) is omitted, per
    /// the read-only browser's display scope. An item's version number, if
    /// present, comes from the response's "included" array (version
    /// resources) cross-referenced by the item's relationships.tip.data.id —
    /// the SDK-verified shape for this endpoint.
    /// </summary>
    private static IEnumerable<AccBrowseNode> ParseFolderContents(JsonElement root)
    {
        Dictionary<string, AccVersionInfo> versionsById = ExtractIncludedVersions(root);

        if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement entry in data.EnumerateArray())
        {
            string type = GetString(entry, "type") ?? "";
            string id = GetString(entry, "id") ?? "";

            if (string.Equals(type, "folders", StringComparison.Ordinal))
            {
                yield return new AccBrowseNode
                {
                    Id = id,
                    Name = GetAttributeString(entry, "name") ?? "",
                    Kind = AccNodeKind.Folder,
                    LastModifiedUtc = GetAttributeDateTime(entry, "lastModifiedTime"),
                };
                continue;
            }

            if (!string.Equals(type, "items", StringComparison.Ordinal))
            {
                continue;
            }

            string displayName = GetAttributeString(entry, "displayName") ?? "";
            if (!displayName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DateTime? lastModified = GetAttributeDateTime(entry, "lastModifiedTime");
            AccVersionInfo? tip = null;

            string? tipId = GetRelationshipId(entry, "tip");
            if (tipId != null)
            {
                versionsById.TryGetValue(tipId, out tip);
            }

            yield return new AccBrowseNode
            {
                Id = id,
                ItemId = id,
                VersionId = tipId,
                Name = displayName,
                Kind = AccNodeKind.RvtFile,
                LastModifiedUtc = lastModified ?? tip?.LastModifiedUtc,
                VersionNumber = tip?.VersionNumber,
                ExtensionType = tip?.ExtensionType,
                CloudProjectGuid = tip?.ProjectGuid,
                CloudModelGuid = tip?.ModelGuid,
            };
        }
    }

    /// <summary>Tip-version metadata cross-referenced onto a folder-contents item — mirrors the same fields <see cref="ParseSearchResults"/> reads directly off a version.</summary>
    private sealed record AccVersionInfo(int? VersionNumber, DateTime? LastModifiedUtc, string? ExtensionType, string? ProjectGuid, string? ModelGuid);

    private static Dictionary<string, AccVersionInfo> ExtractIncludedVersions(JsonElement root)
    {
        Dictionary<string, AccVersionInfo> map = new(StringComparer.Ordinal);

        if (!root.TryGetProperty("included", out JsonElement included) || included.ValueKind != JsonValueKind.Array)
        {
            return map;
        }

        foreach (JsonElement resource in included.EnumerateArray())
        {
            if (GetString(resource, "type") != "versions")
            {
                continue;
            }

            string? id = GetString(resource, "id");
            if (id == null)
            {
                continue;
            }

            map[id] = new AccVersionInfo(
                GetAttributeInt(resource, "versionNumber"),
                GetAttributeDateTime(resource, "lastModifiedTime"),
                GetAttributeExtensionType(resource),
                GetAttributeExtensionDataString(resource, "projectGuid"),
                GetAttributeExtensionDataString(resource, "modelGuid"));
        }

        return map;
    }

    /// <summary>
    /// Returns the "next" pagination link only if it is an absolute
    /// https:// URL on the expected APS host — never follows a
    /// server-supplied link elsewhere, however that link ended up in the
    /// response.
    /// </summary>
    private static string? GetNextPageUrl(JsonElement root)
    {
        if (!root.TryGetProperty("links", out JsonElement links) ||
            !links.TryGetProperty("next", out JsonElement next) ||
            !next.TryGetProperty("href", out JsonElement href) ||
            href.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? url = href.GetString();
        if (url == null)
        {
            return null;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) &&
            parsed.Scheme == Uri.UriSchemeHttps &&
            string.Equals(parsed.Host, AllowedPaginationHost, StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        Log.Warning("ACC Data Management: rejected pagination link to an unexpected host");
        return null;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetAttributeString(JsonElement entry, string propertyName) =>
        entry.TryGetProperty("attributes", out JsonElement attrs) ? GetString(attrs, propertyName) : null;

    private static DateTime? GetAttributeDateTime(JsonElement entry, string propertyName)
    {
        string? raw = GetAttributeString(entry, propertyName);
        if (raw == null)
        {
            return null;
        }

        return DateTime.TryParse(
            raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)
            ? parsed
            : null;
    }

    private async Task<JsonDocument> GetWithUnauthorizedRetryAsync(string url, CancellationToken cancellationToken)
    {
        string accessToken = await authenticationService.GetValidAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await transport.GetJsonAsync(url, accessToken, cancellationToken).ConfigureAwait(false);
        }
        catch (ApsApiException ex) when (ex.StatusCode == 401)
        {
            return await RetryAfterUnauthorizedAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (ApsApiException ex)
        {
            throw new AccDataManagementException(AccDataManagementErrorMapper.ToFriendlyMessage(ex), ex);
        }
    }

    private async Task<JsonDocument> RetryAfterUnauthorizedAsync(string url, CancellationToken cancellationToken)
    {
        Log.Warning("ACC Data Management request returned 401; attempting one token refresh and retry");

        string refreshedToken;
        try
        {
            refreshedToken = await authenticationService.RefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception refreshEx)
        {
            Log.Warning(refreshEx, "ACC Data Management token refresh after 401 failed; returning to signed-out");
            await SafeSignOutAsync(cancellationToken).ConfigureAwait(false);
            throw new AccDataManagementException(AccDataManagementErrorMapper.SessionExpiredMessage, refreshEx);
        }

        try
        {
            return await transport.GetJsonAsync(url, refreshedToken, cancellationToken).ConfigureAwait(false);
        }
        catch (ApsApiException retryEx) when (retryEx.StatusCode == 401)
        {
            Log.Warning("ACC Data Management retry still returned 401 after refresh; returning to signed-out");
            await SafeSignOutAsync(cancellationToken).ConfigureAwait(false);
            throw new AccDataManagementException(AccDataManagementErrorMapper.SessionExpiredMessage, retryEx);
        }
        catch (ApsApiException retryEx)
        {
            throw new AccDataManagementException(AccDataManagementErrorMapper.ToFriendlyMessage(retryEx), retryEx);
        }
    }

    private async Task SafeSignOutAsync(CancellationToken cancellationToken)
    {
        try
        {
            await authenticationService.SignOutAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort only; the caller already has a session-expired error to surface.
        }
    }
}
