using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.DataManagement;
using SAUDICO.Federate.UI;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

internal sealed class FakeAccDataManagementClient : IAccDataManagementClient
{
    public int GetHubsCallCount;
    public IReadOnlyList<AccBrowseNode> HubsResult = new List<AccBrowseNode>();
    public System.Exception? ThrowOnGetHubs;

    public Task<IReadOnlyList<AccBrowseNode>> GetHubsAsync(CancellationToken cancellationToken)
    {
        GetHubsCallCount++;
        if (ThrowOnGetHubs != null)
        {
            throw ThrowOnGetHubs;
        }

        return Task.FromResult(HubsResult);
    }

    public Task<IReadOnlyList<AccBrowseNode>> GetProjectsAsync(string hubId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AccBrowseNode>>(new List<AccBrowseNode>());

    public Task<IReadOnlyList<AccBrowseNode>> GetTopFoldersAsync(string hubId, string projectId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AccBrowseNode>>(new List<AccBrowseNode>());

    public Task<IReadOnlyList<AccBrowseNode>> GetFolderContentsAsync(string projectId, string folderId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AccBrowseNode>>(new List<AccBrowseNode>());
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
}
