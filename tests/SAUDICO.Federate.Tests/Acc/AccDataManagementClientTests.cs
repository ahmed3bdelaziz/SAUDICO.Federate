using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.Authentication;
using SAUDICO.Federate.ACC.DataManagement;
using SAUDICO.Federate.ACC.Errors;
using SAUDICO.Federate.ACC.Http;
using SAUDICO.Federate.ACC.Profile;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

internal sealed class FakeDataManagementTransport : IApsHttpTransport
{
    public List<string> RequestedUrls { get; } = new();
    public List<string> RequestedTokens { get; } = new();
    public Queue<Func<JsonDocument>> Responses { get; } = new();

    public Task<JsonDocument> GetJsonAsync(string url, string bearerToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestedUrls.Add(url);
        RequestedTokens.Add(bearerToken);

        if (Responses.Count == 0)
        {
            throw new InvalidOperationException("Test bug: no queued response for GetJsonAsync call " + RequestedUrls.Count);
        }

        return Task.FromResult(Responses.Dequeue()());
    }

    public Task<JsonDocument> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Data Management only ever performs GET requests.");
}

internal sealed class FakeAuthenticationServiceForDataManagement : IApsAuthenticationService
{
    public string AccessToken = "fake-access-token-1";
    public string RefreshedAccessToken = "fake-access-token-2";
    public int RefreshCallCount;
    public Exception? ThrowOnRefresh;
    public bool SignedOutCalled;

    public bool IsAuthenticated => true;
    public ApsAuthenticationState State => ApsAuthenticationState.SignedIn;
    public ApsUserProfile? CurrentUser => null;
    public event EventHandler<ApsAuthenticationStateChangedEventArgs>? AuthenticationStateChanged { add { } remove { } }

    public Task<ApsUserProfile> SignInAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(AccessToken);

    public Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        RefreshCallCount++;
        if (ThrowOnRefresh != null)
        {
            throw ThrowOnRefresh;
        }

        return Task.FromResult(RefreshedAccessToken);
    }

    public Task SignOutAsync(CancellationToken cancellationToken)
    {
        SignedOutCalled = true;
        return Task.CompletedTask;
    }
}

public sealed class AccDataManagementClientTests
{
    private static JsonDocument Doc(string json) => JsonDocument.Parse(json);

    private static (AccDataManagementClient Client, FakeDataManagementTransport Transport, FakeAuthenticationServiceForDataManagement Auth) CreateClient()
    {
        FakeDataManagementTransport transport = new FakeDataManagementTransport();
        FakeAuthenticationServiceForDataManagement auth = new FakeAuthenticationServiceForDataManagement();
        return (new AccDataManagementClient(transport, auth), transport, auth);
    }

    [Fact]
    public async Task GetHubsAsync_ReturnsHubs()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc(
            """{"links":{"self":{"href":"x"}},"data":[{"type":"hubs","id":"hub-1","attributes":{"name":"Acme Hub"}}]}"""));

        IReadOnlyList<AccBrowseNode> hubs = await client.GetHubsAsync(CancellationToken.None);

        AccBrowseNode hub = Assert.Single(hubs);
        Assert.Equal("hub-1", hub.Id);
        Assert.Equal("Acme Hub", hub.Name);
        Assert.Equal(AccNodeKind.Hub, hub.Kind);
        Assert.Contains("/project/v1/hubs", transport.RequestedUrls[0]);
    }

    [Fact]
    public async Task GetProjectsAsync_ReturnsProjectsForHub()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc(
            """{"links":{"self":{"href":"x"}},"data":[{"type":"projects","id":"proj-1","attributes":{"name":"Project One"}}]}"""));

        IReadOnlyList<AccBrowseNode> projects = await client.GetProjectsAsync("hub-1", CancellationToken.None);

        AccBrowseNode project = Assert.Single(projects);
        Assert.Equal("proj-1", project.Id);
        Assert.Equal("Project One", project.Name);
        Assert.Equal(AccNodeKind.Project, project.Kind);
        Assert.Contains("/project/v1/hubs/hub-1/projects", transport.RequestedUrls[0]);
    }

    [Fact]
    public async Task GetTopFoldersAsync_ReturnsTopFolders()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc(
            """{"links":{"self":{"href":"x"}},"data":[{"type":"folders","id":"folder-1","attributes":{"name":"Project Files","lastModifiedTime":"2026-01-01T00:00:00Z"}}]}"""));

        IReadOnlyList<AccBrowseNode> folders = await client.GetTopFoldersAsync("hub-1", "proj-1", CancellationToken.None);

        AccBrowseNode folder = Assert.Single(folders);
        Assert.Equal("folder-1", folder.Id);
        Assert.Equal("Project Files", folder.Name);
        Assert.Equal(AccNodeKind.Folder, folder.Kind);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), folder.LastModifiedUtc);
        Assert.Contains("/project/v1/hubs/hub-1/projects/proj-1/topFolders", transport.RequestedUrls[0]);
    }

    [Fact]
    public async Task GetFolderContentsAsync_ReturnsFoldersAndRvtItems_WithVersionFromIncluded()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc("""
        {
          "links": { "self": { "href": "x" } },
          "data": [
            { "type": "folders", "id": "sub-1", "attributes": { "name": "Subfolder", "lastModifiedTime": "2026-01-02T00:00:00Z" } },
            { "type": "items", "id": "item-1", "attributes": { "displayName": "Model.rvt", "lastModifiedTime": "2026-01-03T00:00:00Z" },
              "relationships": { "tip": { "data": { "type": "versions", "id": "ver-1" } } } }
          ],
          "included": [
            { "type": "versions", "id": "ver-1", "attributes": { "versionNumber": 3, "lastModifiedTime": "2026-01-03T00:00:00Z" } }
          ]
        }
        """));

        IReadOnlyList<AccBrowseNode> entries = await client.GetFolderContentsAsync("proj-1", "folder-1", CancellationToken.None);

        Assert.Equal(2, entries.Count);
        AccBrowseNode folder = Assert.Single(entries, e => e.Kind == AccNodeKind.Folder);
        Assert.Equal("Subfolder", folder.Name);
        AccBrowseNode rvt = Assert.Single(entries, e => e.Kind == AccNodeKind.RvtFile);
        Assert.Equal("Model.rvt", rvt.Name);
        Assert.Equal(3, rvt.VersionNumber);
        Assert.Equal(new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), rvt.LastModifiedUtc);
    }

    [Fact]
    public async Task GetFolderContentsAsync_ExcludesNonRvtItems()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc("""
        {
          "links": { "self": { "href": "x" } },
          "data": [
            { "type": "items", "id": "item-1", "attributes": { "displayName": "Notes.docx", "lastModifiedTime": "2026-01-04T00:00:00Z" } },
            { "type": "items", "id": "item-2", "attributes": { "displayName": "Model.RVT", "lastModifiedTime": "2026-01-04T00:00:00Z" } }
          ]
        }
        """));

        IReadOnlyList<AccBrowseNode> entries = await client.GetFolderContentsAsync("proj-1", "folder-1", CancellationToken.None);

        AccBrowseNode only = Assert.Single(entries);
        Assert.Equal("Model.RVT", only.Name);
        Assert.Equal(AccNodeKind.RvtFile, only.Kind);
    }

    [Fact]
    public async Task GetProjectsAsync_FollowsPaginationUntilNoNextLink()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc("""
        {"links":{"self":{"href":"x"},"next":{"href":"https://developer.api.autodesk.com/project/v1/hubs/hub-1/projects?page=2"}},
         "data":[{"type":"projects","id":"proj-1","attributes":{"name":"Project One"}}]}
        """));
        transport.Responses.Enqueue(() => Doc(
            """{"links":{"self":{"href":"x"}},"data":[{"type":"projects","id":"proj-2","attributes":{"name":"Project Two"}}]}"""));

        IReadOnlyList<AccBrowseNode> projects = await client.GetProjectsAsync("hub-1", CancellationToken.None);

        Assert.Equal(2, projects.Count);
        Assert.Equal(new[] { "proj-1", "proj-2" }, new[] { projects[0].Id, projects[1].Id });
        Assert.Equal(2, transport.RequestedUrls.Count);
        Assert.Equal("https://developer.api.autodesk.com/project/v1/hubs/hub-1/projects?page=2", transport.RequestedUrls[1]);
    }

    [Fact]
    public async Task GetHubsAsync_EmptyDataArray_ReturnsEmptyList()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        transport.Responses.Enqueue(() => Doc("""{"links":{"self":{"href":"x"}},"data":[]}"""));

        IReadOnlyList<AccBrowseNode> hubs = await client.GetHubsAsync(CancellationToken.None);

        Assert.Empty(hubs);
    }

    [Fact]
    public async Task GetHubsAsync_AlreadyCancelledToken_ThrowsWithoutCallingTransport()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, _) = CreateClient();
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetHubsAsync(cts.Token));

        Assert.Empty(transport.RequestedUrls);
    }

    [Fact]
    public async Task GetHubsAsync_401ThenRefreshSucceeds_RetriesOnceAndReturnsData()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, FakeAuthenticationServiceForDataManagement auth) = CreateClient();
        transport.Responses.Enqueue(() => throw new ApsApiException("unauthorized", 401, "unauthorized"));
        transport.Responses.Enqueue(() => Doc(
            """{"links":{"self":{"href":"x"}},"data":[{"type":"hubs","id":"hub-1","attributes":{"name":"Acme Hub"}}]}"""));

        IReadOnlyList<AccBrowseNode> hubs = await client.GetHubsAsync(CancellationToken.None);

        Assert.Single(hubs);
        Assert.Equal(1, auth.RefreshCallCount);
        Assert.Equal(2, transport.RequestedTokens.Count);
        Assert.Equal("fake-access-token-1", transport.RequestedTokens[0]);
        Assert.Equal("fake-access-token-2", transport.RequestedTokens[1]);
        Assert.False(auth.SignedOutCalled);
    }

    [Fact]
    public async Task GetHubsAsync_401ThenRefreshFails_SignsOutAndThrowsSessionExpired()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, FakeAuthenticationServiceForDataManagement auth) = CreateClient();
        transport.Responses.Enqueue(() => throw new ApsApiException("unauthorized", 401, "unauthorized"));
        auth.ThrowOnRefresh = new ApsSessionExpiredException("expired");

        AccDataManagementException ex = await Assert.ThrowsAsync<AccDataManagementException>(() => client.GetHubsAsync(CancellationToken.None));

        Assert.Equal(AccDataManagementErrorMapper.SessionExpiredMessage, ex.Message);
        Assert.True(auth.SignedOutCalled);
        Assert.Equal(1, auth.RefreshCallCount);
    }

    [Fact]
    public async Task GetHubsAsync_401ThenRetryStill401_SignsOutAndThrowsSessionExpired()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, FakeAuthenticationServiceForDataManagement auth) = CreateClient();
        transport.Responses.Enqueue(() => throw new ApsApiException("unauthorized", 401, "unauthorized"));
        transport.Responses.Enqueue(() => throw new ApsApiException("unauthorized", 401, "unauthorized"));

        AccDataManagementException ex = await Assert.ThrowsAsync<AccDataManagementException>(() => client.GetHubsAsync(CancellationToken.None));

        Assert.Equal(AccDataManagementErrorMapper.SessionExpiredMessage, ex.Message);
        Assert.True(auth.SignedOutCalled);
        Assert.Equal(1, auth.RefreshCallCount);
        Assert.Equal(2, transport.RequestedUrls.Count);
    }

    [Theory]
    [InlineData(403, "You do not have sufficient access to this Autodesk project or account.")]
    [InlineData(404, "This item is unavailable.")]
    [InlineData(429, "Autodesk Construction Cloud is rate-limiting requests right now. Please try again shortly.")]
    [InlineData(500, "Autodesk Construction Cloud service is unavailable right now.")]
    [InlineData(503, "Autodesk Construction Cloud service is unavailable right now.")]
    public async Task GetHubsAsync_MapsHttpStatusToFriendlyMessage(int statusCode, string expectedMessage)
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, FakeAuthenticationServiceForDataManagement auth) = CreateClient();
        transport.Responses.Enqueue(() => throw new ApsApiException("error", statusCode, "error"));

        AccDataManagementException ex = await Assert.ThrowsAsync<AccDataManagementException>(() => client.GetHubsAsync(CancellationToken.None));

        Assert.Equal(expectedMessage, ex.Message);
        Assert.False(auth.SignedOutCalled);
    }

    [Fact]
    public async Task GetHubsAsync_NetworkFailure_MapsToConnectionUnavailableMessage()
    {
        (AccDataManagementClient client, FakeDataManagementTransport transport, FakeAuthenticationServiceForDataManagement auth) = CreateClient();
        transport.Responses.Enqueue(() => throw new ApsApiException("network failure", null, "network_error"));

        AccDataManagementException ex = await Assert.ThrowsAsync<AccDataManagementException>(() => client.GetHubsAsync(CancellationToken.None));

        Assert.Equal("A network connection to Autodesk Construction Cloud is unavailable.", ex.Message);
        Assert.False(auth.SignedOutCalled);
    }
}
