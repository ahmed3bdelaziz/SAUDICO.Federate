using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SAUDICO.Federate.ACC.Authentication;
using SAUDICO.Federate.ACC.DataManagement;
using SAUDICO.Federate.ACC.Http;

namespace SAUDICO.Federate.UI;

/// <summary>
/// ACC browser shell. Contains no authentication or Data Management
/// business logic itself — only composes the existing isolated
/// <see cref="AuthPanel"/>/<see cref="AuthViewModel"/> and the read-only
/// <see cref="AccBrowsePanel"/>/<see cref="AccBrowseViewModel"/>. Opened
/// without ExternalEvent: no Revit API operation is required to show this
/// window, to sign in, or to browse ACC. Constructed and shown on the same
/// WPF Dispatcher as the calling Federation Manager window.
/// </summary>
public partial class AccBrowserWindow : Window
{
    private readonly AuthViewModel? viewModel;
    private readonly AccBrowseViewModel? browseViewModel;

    public AccBrowserWindow(IApsAuthenticationService authenticationService, Action<IReadOnlyList<AccBrowseNode>>? onAddToQueue = null)
    {
        LogLifecycleMarker("AccBrowserWindowConstructorEntered");

        LogLifecycleMarker("BeforeInitializeComponent");
        InitializeComponent();
        LogLifecycleMarker("AfterInitializeComponent");

        // AuthViewModel/AuthPanel construction log their own lifecycle
        // markers internally (AuthViewModelConstructorEntered/Completed,
        // AuthenticationStateEventSubscribed, AuthPanelConstructorEntered/
        // Completed, DataContextAssigned). Constructing them here does not
        // start HttpListener, open the system browser, call any APS
        // endpoint, or read the user profile — that only happens once the
        // user clicks "Sign in with Autodesk" inside the panel.
        viewModel = new AuthViewModel(authenticationService);
        Host.Content = new AuthPanel(viewModel);

        // AccDataManagementClient construction makes no HTTP call itself —
        // it only wraps the existing HTTP transport and authentication
        // abstraction. Hubs are not fetched until AuthViewModel reaches
        // SignedIn (see OnAuthStateChanged below), i.e. only after the user
        // has actually signed in. "Add Selected Models" hands the selected
        // rows to the caller (the Federation Manager queue) — this window
        // and its ViewModel never open, download, or write anything.
        IAccDataManagementClient dataClient = new AccDataManagementClient(new ApsHttpTransport(), authenticationService);
        browseViewModel = new AccBrowseViewModel(dataClient, onAddToQueue);
        BrowseHost.Content = new AccBrowsePanel(browseViewModel);
        viewModel.PropertyChanged += OnAuthViewModelPropertyChanged;

        // AuthViewModel may already be SignedIn at construction time — the
        // underlying IApsAuthenticationService is a singleton composed once
        // per Revit session, so if the user already signed in during an
        // earlier "Add ACC Models" open, this new AuthViewModel starts life
        // already SignedIn and no PropertyChanged event for IsSignedIn will
        // ever fire. Without this explicit check, Hubs would never load and
        // the browser would show a silently-empty list. See AccBrowseAutoStart.
        AccBrowseAutoStart.EvaluateInitialState(viewModel.IsSignedIn, browseViewModel.Start);

        Closing += OnClosing;
        Loaded += (_, _) => LogLifecycleMarker("AccBrowserWindowLoaded");
        ContentRendered += (_, _) => LogLifecycleMarker("AccBrowserWindowContentRendered");
        Closed += (_, _) => LogLifecycleMarker("AccBrowserWindowClosed");

        // Scoped to this window's lifetime only (subscribed here, removed in
        // OnClosing) — not a global/app-wide handler. Uses this window's own
        // Dispatcher rather than System.Windows.Application.Current, which
        // Revit does not guarantee is non-null.
        Dispatcher.UnhandledException += OnDispatcherUnhandledException;
    }

    /// <summary>
    /// Starts loading Hubs the moment sign-in succeeds; resets the browser
    /// back to empty/unloaded if the user signs out (or a Data Management
    /// 401-retry exhausts and signs the user out) so stale data from a
    /// previous session is never shown against a new one. Only the
    /// IsSignedIn property is acted on — every other AuthViewModel
    /// notification (DisplayName, ErrorMessage, State, ...) is ignored; see
    /// AccBrowseAutoStart.
    /// </summary>
    private void OnAuthViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (viewModel == null || browseViewModel == null)
        {
            return;
        }

        AccBrowseAutoStart.HandlePropertyChanged(e.PropertyName, viewModel.IsSignedIn, browseViewModel.Start, browseViewModel.Reset);
    }

    /// <summary>
    /// Diagnostic Level 2 only: the real ACC browser shell (title,
    /// dimensions, header, styling, resources) with static replacement
    /// content instead of <see cref="AuthPanel"/>/<see cref="AuthViewModel"/>.
    /// Constructs no authentication, configuration, DPAPI, HTTP, or
    /// callback-listener type. Remove this constructor once Level 2 is
    /// superseded by the real authenticated path.
    /// </summary>
    public AccBrowserWindow()
    {
        LogLifecycleMarker("AccBrowserWindowConstructorEntered");

        LogLifecycleMarker("BeforeInitializeComponent");
        InitializeComponent();
        LogLifecycleMarker("AfterInitializeComponent");

        StackPanel panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "ACC Browser UI shell",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Authentication components are disabled for diagnostic testing.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });
        Button closeButton = new Button
        {
            Content = "Close",
            Width = 80,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        closeButton.Click += (_, _) => Close();
        panel.Children.Add(closeButton);
        Host.Content = panel;
        LogLifecycleMarker("StaticContentAssigned");

        Loaded += (_, _) => LogLifecycleMarker("AccBrowserWindowLoaded");
        ContentRendered += (_, _) => LogLifecycleMarker("AccBrowserWindowContentRendered");
        Closed += (_, _) => LogLifecycleMarker("AccBrowserWindowClosed");
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        Dispatcher.UnhandledException -= OnDispatcherUnhandledException;
        if (viewModel != null)
        {
            viewModel.PropertyChanged -= OnAuthViewModelPropertyChanged;
        }

        browseViewModel?.Dispose();
        viewModel?.Dispose();
    }

    /// <summary>
    /// Scoped diagnostic tap only — logs the full exception (type, message,
    /// stack trace via Serilog's exception-aware overload, and the inner
    /// exception chain) so a deferred WPF binding/layout exception is
    /// captured. Deliberately never sets <see cref="DispatcherUnhandledExceptionEventArgs.Handled"/>:
    /// this must not suppress unknown, native, AccessViolation, or
    /// corrupted-state exceptions, nor mask ordinary managed ones — it only
    /// records what was about to crash before it does.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogUnhandledException(e.Exception);
    }

    private static void LogUnhandledException(Exception ex)
    {
        try
        {
            Serilog.Log.Error(
                ex,
                "ACC browser unhandled dispatcher exception. {ExceptionType}: {ExceptionMessage}",
                ex.GetType().FullName, ex.Message);

            Exception? inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth < 5)
            {
                Serilog.Log.Error(
                    inner,
                    "ACC browser unhandled dispatcher exception inner (depth {Depth}): {ExceptionType}: {ExceptionMessage}",
                    depth, inner.GetType().FullName, inner.Message);
                inner = inner.InnerException;
                depth++;
            }
        }
        catch
        {
            // Logging must never be the reason this handler fails.
        }
    }

    private static void LogLifecycleMarker(string marker)
    {
        try
        {
            Serilog.Log.Information("ACC browser lifecycle marker: {Marker}", marker);
        }
        catch
        {
            // Logging must never be the reason this diagnostic path fails.
        }
    }
}
