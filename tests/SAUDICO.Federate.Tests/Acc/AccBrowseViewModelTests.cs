using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.DataManagement;
using SAUDICO.Federate.UI;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

internal sealed class FakeAccDataManagementClient : IAccDataManagementClient
{
    public int GetHubsCallCount;
    public int GetProjectsCallCount;
    public int GetTopFoldersCallCount;
    public int GetFolderContentsCallCount;
    public int SearchFolderRecursiveCallCount;
    public int SearchProjectCallCount;

    public IReadOnlyList<AccBrowseNode> HubsResult = new List<AccBrowseNode>();
    public IReadOnlyList<AccBrowseNode> ProjectsResult = new List<AccBrowseNode>();
    public IReadOnlyList<AccBrowseNode> TopFoldersResult = new List<AccBrowseNode>();
    public IReadOnlyList<AccBrowseNode> FolderContentsResult = new List<AccBrowseNode>();
    public AccSearchOutcome SearchFolderRecursiveResult = new AccSearchOutcome();
    public AccSearchOutcome SearchProjectResult = new AccSearchOutcome();
    public Exception? ThrowOnGetHubs;

    // Browsing never downloads — the browse ViewModel must never call these.
    public Task<string?> GetVersionStorageUrnAsync(string projectId, string versionId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Browsing must never resolve a storage URN.");

    public Task<AccSignedDownload> GetSignedDownloadAsync(string bucketKey, string objectKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Browsing must never request a signed download URL.");

    public Task<IReadOnlyList<AccBrowseNode>> GetHubsAsync(CancellationToken cancellationToken)
    {
        GetHubsCallCount++;
        if (ThrowOnGetHubs != null)
        {
            throw ThrowOnGetHubs;
        }

        return Task.FromResult(HubsResult);
    }

    public Task<IReadOnlyList<AccBrowseNode>> GetProjectsAsync(string hubId, CancellationToken cancellationToken)
    {
        GetProjectsCallCount++;
        return Task.FromResult(ProjectsResult);
    }

    public Task<IReadOnlyList<AccBrowseNode>> GetTopFoldersAsync(string hubId, string projectId, CancellationToken cancellationToken)
    {
        GetTopFoldersCallCount++;
        return Task.FromResult(TopFoldersResult);
    }

    public Task<IReadOnlyList<AccBrowseNode>> GetFolderContentsAsync(string projectId, string folderId, CancellationToken cancellationToken)
    {
        GetFolderContentsCallCount++;
        return Task.FromResult(FolderContentsResult);
    }

    public Task<AccSearchOutcome> SearchFolderRecursiveAsync(string projectId, string folderId, CancellationToken cancellationToken)
    {
        SearchFolderRecursiveCallCount++;
        return Task.FromResult(SearchFolderRecursiveResult);
    }

    public Task<AccSearchOutcome> SearchProjectAsync(string hubId, string projectId, CancellationToken cancellationToken)
    {
        SearchProjectCallCount++;
        return Task.FromResult(SearchProjectResult);
    }
}

public sealed class AccBrowseViewModelTests
{
    [Fact]
    public async Task StartAsync_CalledTwice_DoesNotDuplicateGetHubsCall()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient
        {
            HubsResult = new List<AccBrowseNode> { new AccBrowseNode { Id = "hub-1", Name = "Acme Hub", Kind = AccNodeKind.Hub } },
        };
        AccBrowseViewModel viewModel = new AccBrowseViewModel(client);

        await viewModel.StartAsync();
        await viewModel.StartAsync();
        viewModel.Start(); // fire-and-forget entry point WPF actually calls

        Assert.Equal(1, client.GetHubsCallCount);
    }

    [Fact]
    public async Task StartAsync_EmptyHubResponse_ShowsEmptyStateWithRequiredMessage()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient
        {
            HubsResult = new List<AccBrowseNode>(),
        };
        AccBrowseViewModel viewModel = new AccBrowseViewModel(client);

        await viewModel.StartAsync();

        Assert.Empty(viewModel.Items);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.HasError);
        Assert.True(viewModel.ShowEmptyState);
        Assert.Equal("No accessible Autodesk accounts were found.", viewModel.EmptyStateText);
    }

    [Fact]
    public async Task StartAsync_FailedHubRequest_ShowsSafeErrorState()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient
        {
            ThrowOnGetHubs = new AccDataManagementException(AccDataManagementErrorMapper.SessionExpiredMessage),
        };
        AccBrowseViewModel viewModel = new AccBrowseViewModel(client);

        await viewModel.StartAsync();

        Assert.True(viewModel.HasError);
        Assert.Equal(AccDataManagementErrorMapper.SessionExpiredMessage, viewModel.ErrorMessage);
        Assert.False(viewModel.ShowEmptyState);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public void Reset_BeforeAnyLoad_DoesNotShowEmptyState()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient();
        AccBrowseViewModel viewModel = new AccBrowseViewModel(client);

        viewModel.Reset();

        Assert.False(viewModel.ShowEmptyState);
        Assert.Equal(0, client.GetHubsCallCount);
    }

    [Fact]
    public async Task StartAsync_LoadingTextAndEmptyStateText_UseHubsWording()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient();
        AccBrowseViewModel viewModel = new AccBrowseViewModel(client);

        Assert.Equal("Loading Autodesk accounts...", viewModel.LoadingText);

        await viewModel.StartAsync();

        Assert.Equal("No accessible Autodesk accounts were found.", viewModel.EmptyStateText);
    }

    private static async Task<AccBrowseViewModel> NavigateToFolderAsync(FakeAccDataManagementClient client)
    {
        client.HubsResult = new List<AccBrowseNode> { new AccBrowseNode { Id = "hub-1", Name = "Acme Hub", Kind = AccNodeKind.Hub, Region = "US" } };
        client.ProjectsResult = new List<AccBrowseNode> { new AccBrowseNode { Id = "proj-1", Name = "Project X", Kind = AccNodeKind.Project } };
        client.TopFoldersResult = new List<AccBrowseNode> { new AccBrowseNode { Id = "folder-1", Name = "Project Files", Kind = AccNodeKind.Folder } };

        AccBrowseViewModel viewModel = new AccBrowseViewModel(client);
        await viewModel.StartAsync();
        viewModel.SelectedItem = viewModel.Items.Single(i => i.Kind == AccNodeKind.Hub);
        await ExecuteAsync(viewModel.OpenCommand);
        viewModel.SelectedItem = viewModel.Items.Single(i => i.Kind == AccNodeKind.Project);
        await ExecuteAsync(viewModel.OpenCommand);
        viewModel.SelectedItem = viewModel.Items.Single(i => i.Kind == AccNodeKind.Folder);
        await ExecuteAsync(viewModel.OpenCommand);
        return viewModel;
    }

    private static async Task ExecuteAsync(RelayCommand command)
    {
        command.Execute(null);
        // RelayCommand.Execute is async void; give its awaited work a chance to complete
        // (the fakes complete synchronously, so one yield is sufficient and deterministic here).
        await Task.Yield();
    }

    [Fact]
    public async Task CurrentFolderScope_UsesLocalFilterOnly_NeverCallsSearchEndpoints()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient();
        client.FolderContentsResult = new List<AccBrowseNode>
        {
            new AccBrowseNode { Id = "item-1", ItemId = "item-1", Name = "Model.rvt", Kind = AccNodeKind.RvtFile },
            new AccBrowseNode { Id = "item-2", ItemId = "item-2", Name = "Other.rvt", Kind = AccNodeKind.RvtFile },
        };
        AccBrowseViewModel viewModel = await NavigateToFolderAsync(client);

        Assert.Equal(AccSearchScope.CurrentFolder, viewModel.SearchScope);
        viewModel.SearchText = "Model";

        Assert.Single(viewModel.Items);
        Assert.Equal("Model.rvt", viewModel.Items[0].Name);
        Assert.Equal(0, client.SearchFolderRecursiveCallCount);
        Assert.Equal(0, client.SearchProjectCallCount);
    }

    [Fact]
    public async Task RecursiveSearchScope_EnrichesResultsWithHubProjectRegionContext()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient();
        client.SearchFolderRecursiveResult = new AccSearchOutcome
        {
            Results = new List<AccBrowseNode> { new AccBrowseNode { Id = "item-1", ItemId = "item-1", Name = "Model.rvt", Kind = AccNodeKind.RvtFile } },
        };
        AccBrowseViewModel viewModel = await NavigateToFolderAsync(client);

        viewModel.SearchScope = AccSearchScope.CurrentFolderAndSubfolders;
        await ExecuteAsync(viewModel.SearchCommand);

        AccBrowseNode result = Assert.Single(viewModel.Items);
        Assert.Equal("hub-1", result.HubId);
        Assert.Equal("Acme Hub", result.HubName);
        Assert.Equal("US", result.Region);
        Assert.Equal("proj-1", result.ProjectId);
        Assert.Equal("Project X", result.ProjectName);
        Assert.Equal(1, client.SearchFolderRecursiveCallCount);
    }

    [Fact]
    public async Task EntireProjectSearch_PartialFailure_SetsHasPartialSearchFailure()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient();
        client.SearchProjectResult = new AccSearchOutcome
        {
            Results = new List<AccBrowseNode> { new AccBrowseNode { Id = "item-1", ItemId = "item-1", Name = "Model.rvt", Kind = AccNodeKind.RvtFile } },
            HasPartialFailure = true,
            FailedFolderCount = 1,
        };
        AccBrowseViewModel viewModel = await NavigateToFolderAsync(client);

        viewModel.SearchScope = AccSearchScope.EntireProject;
        await ExecuteAsync(viewModel.SearchCommand);

        Assert.True(viewModel.HasPartialSearchFailure);
        Assert.Single(viewModel.Items);
    }

    /// <summary>
    /// A genuine Revit Cloud Worksharing row — the only kind the ACC browser
    /// allows to be queued. A plain uploaded RVT is deliberately not
    /// selectable (see <see cref="OnlyCloudWorksharedRowsAreSelectable"/>).
    /// </summary>
    private static AccBrowseNode CloudRvt(string id, string name) => new AccBrowseNode
    {
        Id = id,
        ItemId = id,
        Name = name,
        Kind = AccNodeKind.RvtFile,
        ExtensionType = AccCloudModelClassifier.RevitCloudModelExtensionType,
        CloudProjectGuid = Guid.NewGuid().ToString(),
        CloudModelGuid = Guid.NewGuid().ToString(),
    };

    [Fact]
    public async Task BrowsedFolderContents_CarryHubProjectAndRegionContext()
    {
        // Regression: folder contents were previously enriched only for the
        // two SEARCH scopes, so a model queued by BROWSING reached the
        // federation queue with a null Region and was wrongly refused as an
        // "unsupported region".
        FakeAccDataManagementClient client = new FakeAccDataManagementClient();
        client.FolderContentsResult = new List<AccBrowseNode> { CloudRvt("item-1", "Cloud.rvt") };
        AccBrowseViewModel viewModel = await NavigateToFolderAsync(client);

        AccBrowseNode row = viewModel.Items.Single(i => i.Kind == AccNodeKind.RvtFile);
        Assert.Equal("US", row.Region);
        Assert.Equal("hub-1", row.HubId);
        Assert.Equal("proj-1", row.ProjectId);
        Assert.Equal("folder-1", row.FolderId);
    }

    [Fact]
    public async Task BrowsedFolderRows_AreNotGivenAFabricatedFolderPath()
    {
        // The API supplies pathInProject only for search results; browsing
        // must leave it null rather than reconstruct it from breadcrumb text.
        FakeAccDataManagementClient client = new FakeAccDataManagementClient();
        client.FolderContentsResult = new List<AccBrowseNode> { CloudRvt("item-1", "Cloud.rvt") };
        AccBrowseViewModel viewModel = await NavigateToFolderAsync(client);

        Assert.Null(viewModel.Items.Single(i => i.Kind == AccNodeKind.RvtFile).FolderPath);
    }

    [Fact]
    public async Task OnlyCloudWorksharedRowsAreSelectable()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient();
        client.FolderContentsResult = new List<AccBrowseNode>
        {
            CloudRvt("item-1", "Cloud.rvt"),
            // A plain uploaded RVT: right extension type for a file, no cloud GUIDs.
            new AccBrowseNode { Id = "item-2", ItemId = "item-2", Name = "Uploaded.rvt", Kind = AccNodeKind.RvtFile, ExtensionType = "items:autodesk.bim360:File" },
            // An RVT whose tip version carried no usable metadata at all.
            new AccBrowseNode { Id = "item-3", ItemId = "item-3", Name = "Unknown.rvt", Kind = AccNodeKind.RvtFile },
        };
        AccBrowseViewModel viewModel = await NavigateToFolderAsync(client);

        Assert.True(viewModel.Items.Single(i => i.Name == "Cloud.rvt").IsSelectable);
        Assert.False(viewModel.Items.Single(i => i.Name == "Uploaded.rvt").IsSelectable);
        Assert.False(viewModel.Items.Single(i => i.Name == "Unknown.rvt").IsSelectable);

        // Select All must never pick up a non-cloud row.
        viewModel.SelectAllVisibleCommand.Execute(null);
        Assert.Equal(1, viewModel.SelectedCount);
        Assert.Equal("Cloud.rvt", viewModel.Items.Single(i => i.IsSelected).Name);
    }

    [Fact]
    public async Task NonCloudRows_ExplainWhyTheyCannotBeQueued()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient();
        client.FolderContentsResult = new List<AccBrowseNode>
        {
            CloudRvt("item-1", "Cloud.rvt"),
            new AccBrowseNode { Id = "item-2", ItemId = "item-2", Name = "Uploaded.rvt", Kind = AccNodeKind.RvtFile, ExtensionType = "items:autodesk.bim360:File" },
            new AccBrowseNode { Id = "sub-1", Name = "Subfolder", Kind = AccNodeKind.Folder },
        };
        AccBrowseViewModel viewModel = await NavigateToFolderAsync(client);

        Assert.Equal("Cloud workshared", viewModel.Items.Single(i => i.Name == "Cloud.rvt").SourceLabel);
        Assert.Contains("Add RVT", viewModel.Items.Single(i => i.Name == "Uploaded.rvt").SourceLabel);
        Assert.Equal("", viewModel.Items.Single(i => i.Kind == AccNodeKind.Folder).SourceLabel);
    }

    [Fact]
    public async Task SelectAllVisible_SelectsOnlyRvtRows_NotFolders()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient();
        client.FolderContentsResult = new List<AccBrowseNode>
        {
            new AccBrowseNode { Id = "sub-1", Name = "Subfolder", Kind = AccNodeKind.Folder },
            CloudRvt("item-1", "A.rvt"),
            CloudRvt("item-2", "B.rvt"),
        };
        AccBrowseViewModel viewModel = await NavigateToFolderAsync(client);

        viewModel.SelectAllVisibleCommand.Execute(null);

        Assert.Equal(2, viewModel.SelectedCount);
        Assert.False(viewModel.Items.Single(i => i.Kind == AccNodeKind.Folder).IsSelected);
        Assert.True(viewModel.Items.Where(i => i.Kind == AccNodeKind.RvtFile).All(i => i.IsSelected));
    }

    [Fact]
    public async Task ClearSelection_DeselectsEverything()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient();
        client.FolderContentsResult = new List<AccBrowseNode>
        {
            CloudRvt("item-1", "A.rvt"),
        };
        AccBrowseViewModel viewModel = await NavigateToFolderAsync(client);
        viewModel.SelectAllVisibleCommand.Execute(null);
        Assert.Equal(1, viewModel.SelectedCount);

        viewModel.ClearSelectionCommand.Execute(null);

        Assert.Equal(0, viewModel.SelectedCount);
    }

    [Fact]
    public async Task SelectedCount_UpdatesLive_WhenIndividualNodeToggled()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient();
        client.FolderContentsResult = new List<AccBrowseNode>
        {
            CloudRvt("item-1", "A.rvt"),
        };
        AccBrowseViewModel viewModel = await NavigateToFolderAsync(client);

        viewModel.Items[0].IsSelected = true;

        Assert.Equal(1, viewModel.SelectedCount);
    }

    [Fact]
    public async Task AddSelectedModels_InvokesCallbackWithSelectedOnly_ThenClearsSelection()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient();
        client.FolderContentsResult = new List<AccBrowseNode>
        {
            CloudRvt("item-1", "A.rvt"),
            CloudRvt("item-2", "B.rvt"),
        };

        IReadOnlyList<AccBrowseNode>? handedOff = null;
        AccBrowseViewModel viewModel = null!;
        client.HubsResult = new List<AccBrowseNode> { new AccBrowseNode { Id = "hub-1", Name = "Acme Hub", Kind = AccNodeKind.Hub } };
        client.ProjectsResult = new List<AccBrowseNode> { new AccBrowseNode { Id = "proj-1", Name = "Project X", Kind = AccNodeKind.Project } };
        client.TopFoldersResult = new List<AccBrowseNode> { new AccBrowseNode { Id = "folder-1", Name = "Project Files", Kind = AccNodeKind.Folder } };
        viewModel = new AccBrowseViewModel(client, selected => handedOff = selected);
        await viewModel.StartAsync();
        viewModel.SelectedItem = viewModel.Items.Single(i => i.Kind == AccNodeKind.Hub);
        await ExecuteAsync(viewModel.OpenCommand);
        viewModel.SelectedItem = viewModel.Items.Single(i => i.Kind == AccNodeKind.Project);
        await ExecuteAsync(viewModel.OpenCommand);
        viewModel.SelectedItem = viewModel.Items.Single(i => i.Kind == AccNodeKind.Folder);
        await ExecuteAsync(viewModel.OpenCommand);

        viewModel.Items.Single(i => i.Name == "A.rvt").IsSelected = true;
        viewModel.AddSelectedModelsCommand.Execute(null);

        Assert.NotNull(handedOff);
        AccBrowseNode onlySelected = Assert.Single(handedOff!);
        Assert.Equal("A.rvt", onlySelected.Name);
        Assert.Equal(0, viewModel.SelectedCount);
    }
}
