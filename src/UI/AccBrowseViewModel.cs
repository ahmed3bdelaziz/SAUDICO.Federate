using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.DataManagement;

namespace SAUDICO.Federate.UI;

/// <summary>
/// Read-only ACC browsing state machine: Hubs → Projects → Top Folders →
/// Folder Contents (with further subfolder drill-down). Contains no HTTP
/// logic itself — all Data Management calls go through
/// <see cref="IAccDataManagementClient"/>. All state mutation happens on
/// the UI thread (RelayCommand.Execute is only ever invoked by WPF's
/// command binding on the UI thread, and async continuations resume on the
/// captured Dispatcher SynchronizationContext), so unlike
/// <see cref="AuthViewModel"/> no explicit Dispatcher marshaling is needed.
/// </summary>
public sealed class AccBrowseViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAccDataManagementClient client;
    private readonly List<AccPathLevel> path = new();
    private List<AccBrowseNode> loadedUnfiltered = new();

    private CancellationTokenSource? loadCts;
    private bool started;
    private bool isLoading;
    private string? errorMessage;
    private string searchText = "";
    private AccBrowseNode? selectedItem;

    public ObservableCollection<AccBrowseNode> Items { get; } = new();

    public bool IsLoading
    {
        get => isLoading;
        private set { isLoading = value; On(); On(nameof(ShowEmptyState)); }
    }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set { errorMessage = value; On(); On(nameof(HasError)); On(nameof(ShowEmptyState)); }
    }

    public bool HasError => ErrorMessage != null;

    public bool CanGoBack => path.Count > 0;

    public bool IsAtHubsLevel => path.Count == 0;

    /// <summary>Shown while a level is loading — the Hubs level gets the specific wording required by the ACC browser spec.</summary>
    public string LoadingText => IsAtHubsLevel ? "Loading Autodesk accounts..." : "Loading…";

    /// <summary>
    /// True once a load has actually been attempted (never true before the
    /// first Start()/Reset() cycle completes) and finished successfully
    /// with zero results and no error.
    /// </summary>
    public bool ShowEmptyState => started && !IsLoading && !HasError && Items.Count == 0;

    public string EmptyStateText => IsAtHubsLevel
        ? "No accessible Autodesk accounts were found."
        : "No items found in this folder.";

    public string BreadcrumbText => "Hubs" + (path.Count == 0 ? "" : " > " + string.Join(" > ", path.Select(p => p.Name)));

    public string SearchText
    {
        get => searchText;
        set { searchText = value; On(); ApplyFilter(); }
    }

    public AccBrowseNode? SelectedItem
    {
        get => selectedItem;
        set { selectedItem = value; On(); }
    }

    public RelayCommand OpenCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand CancelCommand { get; }

    public AccBrowseViewModel(IAccDataManagementClient client)
    {
        this.client = client;

        OpenCommand = new RelayCommand(OpenSelectedAsync, () => !IsLoading && SelectedItem?.IsNavigable == true);
        BackCommand = new RelayCommand(GoBackAsync, () => !IsLoading && CanGoBack);
        RefreshCommand = new RelayCommand(LoadCurrentLevelAsync, () => !IsLoading);
        CancelCommand = new RelayCommand(Cancel, () => IsLoading);
    }

    /// <summary>
    /// Called once, whether the authenticated user was already SignedIn at
    /// construction time or just transitioned to SignedIn — loads Hubs
    /// exactly once. Fire-and-forget for WPF call sites; use
    /// <see cref="StartAsync"/> to await completion (e.g. in tests).
    /// </summary>
    public void Start() => _ = StartAsync();

    /// <summary>Idempotent: a second call while already started (or mid-load) is a no-op and issues no additional request.</summary>
    public Task StartAsync()
    {
        if (started)
        {
            return Task.CompletedTask;
        }

        started = true;
        return LoadCurrentLevelAsync();
    }

    /// <summary>Called when the user signs out — resets to a clean, unloaded state.</summary>
    public void Reset()
    {
        started = false;
        loadCts?.Cancel();
        path.Clear();
        loadedUnfiltered = new List<AccBrowseNode>();
        Items.Clear();
        ErrorMessage = null;
        SelectedItem = null;
        RaiseLevelChanged();
    }

    private Task OpenSelectedAsync()
    {
        AccBrowseNode? node = SelectedItem;
        if (node == null || !node.IsNavigable)
        {
            return Task.CompletedTask;
        }

        AccPathLevel? parent = path.Count > 0 ? path[^1] : null;

        AccPathLevel next = node.Kind switch
        {
            AccNodeKind.Hub => new AccPathLevel { Name = node.Name, Kind = AccNodeKind.Hub, HubId = node.Id },
            AccNodeKind.Project => new AccPathLevel { Name = node.Name, Kind = AccNodeKind.Project, HubId = parent?.HubId, ProjectId = node.Id },
            AccNodeKind.Folder => new AccPathLevel { Name = node.Name, Kind = AccNodeKind.Folder, HubId = parent?.HubId, ProjectId = parent?.ProjectId, FolderId = node.Id },
            _ => throw new InvalidOperationException("Unreachable: IsNavigable already excludes this kind."),
        };

        path.Add(next);
        SelectedItem = null;
        RaiseLevelChanged();
        return LoadCurrentLevelAsync();
    }

    private Task GoBackAsync()
    {
        if (path.Count == 0)
        {
            return Task.CompletedTask;
        }

        path.RemoveAt(path.Count - 1);
        SelectedItem = null;
        RaiseLevelChanged();
        return LoadCurrentLevelAsync();
    }

    private void RaiseLevelChanged()
    {
        On(nameof(BreadcrumbText));
        On(nameof(CanGoBack));
        On(nameof(IsAtHubsLevel));
        On(nameof(LoadingText));
        On(nameof(EmptyStateText));
    }

    private Task LoadCurrentLevelAsync()
    {
        if (path.Count == 0)
        {
            return LoadAsync(client.GetHubsAsync);
        }

        AccPathLevel current = path[^1];
        return current.Kind switch
        {
            AccNodeKind.Hub => LoadAsync(ct => client.GetProjectsAsync(current.HubId!, ct)),
            AccNodeKind.Project => LoadAsync(ct => client.GetTopFoldersAsync(current.HubId!, current.ProjectId!, ct)),
            AccNodeKind.Folder => LoadAsync(ct => client.GetFolderContentsAsync(current.ProjectId!, current.FolderId!, ct)),
            _ => Task.CompletedTask,
        };
    }

    private void Cancel() => loadCts?.Cancel();

    private async Task LoadAsync(Func<CancellationToken, Task<IReadOnlyList<AccBrowseNode>>> fetch)
    {
        loadCts?.Cancel();
        loadCts?.Dispose();
        CancellationTokenSource cts = new CancellationTokenSource();
        loadCts = cts;

        ErrorMessage = null;
        IsLoading = true;
        loadedUnfiltered = new List<AccBrowseNode>();
        Items.Clear();

        try
        {
            IReadOnlyList<AccBrowseNode> result = await fetch(cts.Token).ConfigureAwait(true);
            loadedUnfiltered = new List<AccBrowseNode>(result);
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user — no error, list simply stays empty until Refresh/navigation.
        }
        catch (ACC.Errors.ApsAuthenticationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (ACC.DataManagement.AccDataManagementException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        Items.Clear();
        IEnumerable<AccBrowseNode> filtered = string.IsNullOrWhiteSpace(SearchText)
            ? loadedUnfiltered
            : loadedUnfiltered.Where(n => n.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (AccBrowseNode node in filtered)
        {
            Items.Add(node);
        }

        On(nameof(ShowEmptyState));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void On([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        loadCts?.Cancel();
        loadCts?.Dispose();
    }

    private sealed class AccPathLevel
    {
        public string Name { get; init; } = "";
        public AccNodeKind Kind { get; init; }
        public string? HubId { get; init; }
        public string? ProjectId { get; init; }
        public string? FolderId { get; init; }
    }
}
