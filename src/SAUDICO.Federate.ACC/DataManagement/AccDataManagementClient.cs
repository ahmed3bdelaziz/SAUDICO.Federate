using System;
using System.Collections.Generic;
using System.Globalization;
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

    private async Task<List<AccBrowseNode>> CollectPagesAsync(
        string firstUrl,
        Func<JsonElement, IEnumerable<AccBrowseNode>> parsePage,
        CancellationToken cancellationToken)
    {
        List<AccBrowseNode> results = new();
        string? url = firstUrl;

        while (url != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
        Dictionary<string, (int? VersionNumber, DateTime? LastModifiedUtc)> versionsById = ExtractIncludedVersions(root);

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
            int? versionNumber = null;

            string? tipId = GetTipVersionId(entry);
            if (tipId != null && versionsById.TryGetValue(tipId, out (int? VersionNumber, DateTime? LastModifiedUtc) tip))
            {
                versionNumber = tip.VersionNumber;
                lastModified ??= tip.LastModifiedUtc;
            }

            yield return new AccBrowseNode
            {
                Id = id,
                Name = displayName,
                Kind = AccNodeKind.RvtFile,
                LastModifiedUtc = lastModified,
                VersionNumber = versionNumber,
            };
        }
    }

    private static Dictionary<string, (int? VersionNumber, DateTime? LastModifiedUtc)> ExtractIncludedVersions(JsonElement root)
    {
        Dictionary<string, (int?, DateTime?)> map = new();

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

            int? versionNumber = null;
            if (resource.TryGetProperty("attributes", out JsonElement attrs) &&
                attrs.TryGetProperty("versionNumber", out JsonElement versionEl) &&
                versionEl.ValueKind == JsonValueKind.Number && versionEl.TryGetInt32(out int v))
            {
                versionNumber = v;
            }

            DateTime? lastModified = GetAttributeDateTime(resource, "lastModifiedTime");
            map[id] = (versionNumber, lastModified);
        }

        return map;
    }

    private static string? GetTipVersionId(JsonElement entry)
    {
        if (entry.TryGetProperty("relationships", out JsonElement relationships) &&
            relationships.TryGetProperty("tip", out JsonElement tip) &&
            tip.TryGetProperty("data", out JsonElement tipData) &&
            tipData.TryGetProperty("id", out JsonElement idEl) &&
            idEl.ValueKind == JsonValueKind.String)
        {
            return idEl.GetString();
        }

        return null;
    }

    private static string? GetNextPageUrl(JsonElement root)
    {
        if (root.TryGetProperty("links", out JsonElement links) &&
            links.TryGetProperty("next", out JsonElement next) &&
            next.TryGetProperty("href", out JsonElement href) &&
            href.ValueKind == JsonValueKind.String)
        {
            return href.GetString();
        }

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
