using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.DataManagement;

namespace SAUDICO.Federate.UI;

/// <summary>
/// Read-only ACC browsing state machine: Hubs → Projects → Top Folders →
/// Folder Contents (with further subfolder drill-down), plus RVT search
/// (current folder / current folder+subfolders / entire project) and
/// multi-selection for adding models to the federation queue. Contains no
/// HTTP logic itself — all Data Management calls go through
/// <see cref="IAccDataManagementClient"/>. All state mutation happens on
/// the UI thread (RelayCommand.Execute is only ever invoked by WPF's
/// command binding on the UI thread, and async continuations resume on the
/// captured Dispatcher SynchronizationContext), so unlike
/// <see cref="AuthViewModel"/> no explicit Dispatcher marshaling is needed.
/// </summary>
public sealed class AccBrowseViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAccDataManagementClient client;
    private readonly Action<IReadOnlyList<AccBrowseNode>>? onAddToQueue;
    private readonly List<AccPathLevel> path = new();
    private List<AccBrowseNode> loadedUnfiltered = new();

    private CancellationTokenSource? loadCts;
    private bool started;
    private bool isLoading;
    private string? errorMessage;
    private string searchText = "";
    private AccBrowseNode? selectedItem;
    private AccSearchScope searchScope = AccSearchScope.CurrentFolder;
    private bool hasPartialSearchFailure;

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

    /// <summary>Which folders a Search() run covers. Changing this does not itself trigger a request.</summary>
    public AccSearchScope SearchScope
    {
        get => searchScope;
        set
        {
            if (searchScope == value)
            {
                return;
            }

            searchScope = value;
            On();
            On(nameof(IsRecursiveSearchScope));
        }
    }

    public bool IsRecursiveSearchScope => SearchScope != AccSearchScope.CurrentFolder;

    public Array SearchScopes => Enum.GetValues(typeof(AccSearchScope));

    /// <summary>True when the most recent recursive/entire-project search skipped at least one folder it could not access (e.g. 403) rather than failing outright.</summary>
    public bool HasPartialSearchFailure
    {
        get => hasPartialSearchFailure;
        private set { hasPartialSearchFailure = value; On(); }
    }

    public const string PartialSearchFailureMessage =
        "Some folders could not be searched (e.g. no access) — results shown are from the folders that could be searched.";

    public AccBrowseNode? SelectedItem
    {
        get => selectedItem;
        set { selectedItem = value; On(); }
    }

    public int SelectedCount => Items.Count(i => i.IsSelected);

    public RelayCommand OpenCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SearchCommand { get; }
    public RelayCommand SelectAllVisibleCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand AddSelectedModelsCommand { get; }

    public AccBrowseViewModel(IAccDataManagementClient client, Action<IReadOnlyList<AccBrowseNode>>? onAddToQueue = null)
    {
        this.client = client;
        this.onAddToQueue = onAddToQueue;

        Items.CollectionChanged += OnItemsCollectionChanged;

        OpenCommand = new RelayCommand(OpenSelectedAsync, () => !IsLoading && SelectedItem?.IsNavigable == true);
        BackCommand = new RelayCommand(GoBackAsync, () => !IsLoading && CanGoBack);
        RefreshCommand = new RelayCommand(LoadCurrentLevelAsync, () => !IsLoading);
        CancelCommand = new RelayCommand(Cancel, () => IsLoading);
        SearchCommand = new RelayCommand(RunScopedSearchAsync, () => !IsLoading && IsRecursiveSearchScope && CanRunScopedSearch());
        SelectAllVisibleCommand = new RelayCommand(SelectAllVisible, () => Items.Any(i => i.IsSelectable));
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => Items.Any(i => i.IsSelected));
        AddSelectedModelsCommand = new RelayCommand(AddSelectedModels, () => Items.Any(i => i.IsSelected));
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
        HasPartialSearchFailure = false;
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
            AccNodeKind.Hub => new AccPathLevel { Name = node.Name, Kind = AccNodeKind.Hub, HubId = node.Id, HubName = node.Name, Region = node.Region },
            AccNodeKind.Project => new AccPathLevel
            {
                Name = node.Name, Kind = AccNodeKind.Project,
                HubId = parent?.HubId, HubName = parent?.HubName, Region = parent?.Region,
                ProjectId = node.Id, ProjectName = node.Name,
            },
            AccNodeKind.Folder => new AccPathLevel
            {
                Name = node.Name, Kind = AccNodeKind.Folder,
                HubId = parent?.HubId, HubName = parent?.HubName, Region = parent?.Region,
                ProjectId = parent?.ProjectId, ProjectName = parent?.ProjectName,
                FolderId = node.Id,
            },
            _ => throw new InvalidOperationException("Unreachable: IsNavigable already excludes this kind."),
        };

        path.Add(next);
        SelectedItem = null;
        SearchScope = AccSearchScope.CurrentFolder;
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
        SearchScope = AccSearchScope.CurrentFolder;
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
        HasPartialSearchFailure = false;

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

    private bool CanRunScopedSearch()
    {
        if (path.Count == 0)
        {
            return false;
        }

        AccPathLevel current = path[^1];
        return SearchScope switch
        {
            AccSearchScope.CurrentFolderAndSubfolders => current.Kind == AccNodeKind.Folder && current.ProjectId != null && current.FolderId != null,
            AccSearchScope.EntireProject => current.ProjectId != null && current.HubId != null,
            _ => false,
        };
    }

    private Task RunScopedSearchAsync()
    {
        if (!CanRunScopedSearch())
        {
            return Task.CompletedTask;
        }

        AccPathLevel current = path[^1];

        return SearchScope switch
        {
            AccSearchScope.CurrentFolderAndSubfolders => LoadAsync(async ct =>
            {
                AccSearchOutcome outcome = await client.SearchFolderRecursiveAsync(current.ProjectId!, current.FolderId!, ct).ConfigureAwait(false);
                HasPartialSearchFailure = outcome.HasPartialFailure;
                return EnrichSearchResults(outcome.Results, current);
            }),
            AccSearchScope.EntireProject => LoadAsync(async ct =>
            {
                AccSearchOutcome outcome = await client.SearchProjectAsync(current.HubId!, current.ProjectId!, ct).ConfigureAwait(false);
                HasPartialSearchFailure = outcome.HasPartialFailure;
                return EnrichSearchResults(outcome.Results, current);
            }),
            _ => Task.CompletedTask,
        };
    }

    private static IReadOnlyList<AccBrowseNode> EnrichSearchResults(IReadOnlyList<AccBrowseNode> results, AccPathLevel context)
    {
        foreach (AccBrowseNode node in results)
        {
            node.HubId = context.HubId;
            node.HubName = context.HubName;
            node.Region = context.Region;
            node.ProjectId = context.ProjectId;
            node.ProjectName = context.ProjectName;
        }

        return results;
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

    private void SelectAllVisible()
    {
        foreach (AccBrowseNode node in Items)
        {
            if (node.IsSelectable)
            {
                node.IsSelected = true;
            }
        }

        On(nameof(SelectedCount));
    }

    private void ClearSelection()
    {
        foreach (AccBrowseNode node in Items)
        {
            node.IsSelected = false;
        }

        On(nameof(SelectedCount));
    }

    private void AddSelectedModels()
    {
        List<AccBrowseNode> selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        onAddToQueue?.Invoke(selected);
        ClearSelection();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (AccBrowseNode node in Items)
        {
            node.PropertyChanged -= OnNodePropertyChanged;
            node.PropertyChanged += OnNodePropertyChanged;
        }

        On(nameof(SelectedCount));
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccBrowseNode.IsSelected))
        {
            On(nameof(SelectedCount));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void On([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        loadCts?.Cancel();
        loadCts?.Dispose();
        Items.CollectionChanged -= OnItemsCollectionChanged;
    }

    private sealed class AccPathLevel
    {
        public string Name { get; init; } = "";
        public AccNodeKind Kind { get; init; }
        public string? HubId { get; init; }
        public string? HubName { get; init; }
        public string? Region { get; init; }
        public string? ProjectId { get; init; }
        public string? ProjectName { get; init; }
        public string? FolderId { get; init; }
    }
}
