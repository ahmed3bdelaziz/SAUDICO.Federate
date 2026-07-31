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

    [Fact]
    public async Task SelectAllVisible_SelectsOnlyRvtRows_NotFolders()
    {
        FakeAccDataManagementClient client = new FakeAccDataManagementClient();
        client.FolderContentsResult = new List<AccBrowseNode>
        {
            new AccBrowseNode { Id = "sub-1", Name = "Subfolder", Kind = AccNodeKind.Folder },
            new AccBrowseNode { Id = "item-1", ItemId = "item-1", Name = "A.rvt", Kind = AccNodeKind.RvtFile },
            new AccBrowseNode { Id = "item-2", ItemId = "item-2", Name = "B.rvt", Kind = AccNodeKind.RvtFile },
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
            new AccBrowseNode { Id = "item-1", ItemId = "item-1", Name = "A.rvt", Kind = AccNodeKind.RvtFile },
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
            new AccBrowseNode { Id = "item-1", ItemId = "item-1", Name = "A.rvt", Kind = AccNodeKind.RvtFile },
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
            new AccBrowseNode { Id = "item-1", ItemId = "item-1", Name = "A.rvt", Kind = AccNodeKind.RvtFile },
            new AccBrowseNode { Id = "item-2", ItemId = "item-2", Name = "B.rvt", Kind = AccNodeKind.RvtFile },
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
