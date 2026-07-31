using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Threading;
using SAUDICO.Federate.ACC.Authentication;
using SAUDICO.Federate.ACC.Errors;

namespace SAUDICO.Federate.UI;

/// <summary>
/// All authentication business logic lives here and in SAUDICO.Federate.ACC —
/// AuthPanel.xaml.cs contains none of it, only composition-root wiring.
/// </summary>
public sealed class AuthViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IApsAuthenticationService authenticationService;
    private readonly Dispatcher dispatcher;
    private CancellationTokenSource? signInCts;

    private string displayName = "";
    private string? errorMessage;
    private ApsAuthenticationState state;

    public AuthViewModel(IApsAuthenticationService authenticationService)
    {
        LogMarker("AuthViewModelConstructorEntered");

        this.authenticationService = authenticationService;
        dispatcher = Dispatcher.CurrentDispatcher;
        authenticationService.AuthenticationStateChanged += OnAuthenticationStateChanged;
        LogMarker("AuthenticationStateEventSubscribed");

        state = authenticationService.State;
        displayName = authenticationService.CurrentUser?.DisplayName ?? "";

        SignInCommand = new RelayCommand(SignInAsync, () => CanSignIn);
        SignOutCommand = new RelayCommand(SignOutAsync, () => State == ApsAuthenticationState.SignedIn);
        CancelCommand = new RelayCommand(Cancel, () => State == ApsAuthenticationState.SigningIn);
        RetryCommand = new RelayCommand(SignInAsync, () => State == ApsAuthenticationState.Failed);

        LogMarker("AuthViewModelConstructorCompleted");
    }

    private static void LogMarker(string marker)
    {
        try
        {
            Serilog.Log.Information("ACC browser lifecycle marker: {Marker}", marker);
        }
        catch
        {
            // Logging must never be the reason authentication setup fails.
        }
    }

    public ApsAuthenticationState State
    {
        get => state;
        private set
        {
            state = value;
            On();
            On(nameof(IsSignedOut));
            On(nameof(IsSigningIn));
            On(nameof(IsSignedIn));
            On(nameof(IsFailed));
            On(nameof(IsDisabledOrConfigInvalid));
        }
    }

    public string DisplayName
    {
        get => displayName;
        private set { displayName = value; On(); }
    }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set { errorMessage = value; On(); }
    }

    public bool IsSignedOut => State == ApsAuthenticationState.SignedOut;
    public bool IsSigningIn => State == ApsAuthenticationState.SigningIn;
    public bool IsSignedIn => State == ApsAuthenticationState.SignedIn || State == ApsAuthenticationState.Refreshing;
    public bool IsFailed => State == ApsAuthenticationState.Failed;
    public bool IsDisabledOrConfigInvalid =>
        State == ApsAuthenticationState.Disabled || State == ApsAuthenticationState.ConfigurationInvalid;

    private bool CanSignIn => State == ApsAuthenticationState.SignedOut || State == ApsAuthenticationState.Expired;

    public RelayCommand SignInCommand { get; }
    public RelayCommand SignOutCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand RetryCommand { get; }

    private async System.Threading.Tasks.Task SignInAsync()
    {
        ErrorMessage = null;
        signInCts = new CancellationTokenSource();

        try
        {
            await authenticationService.SignInAsync(signInCts.Token);
        }
        catch (ApsAuthenticationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (OperationCanceledException)
        {
            // Cancel() already updated the UI.
        }
        finally
        {
            signInCts.Dispose();
            signInCts = null;
        }
    }

    private async System.Threading.Tasks.Task SignOutAsync()
    {
        ErrorMessage = null;
        await authenticationService.SignOutAsync(CancellationToken.None);
    }

    private void Cancel()
    {
        signInCts?.Cancel();
    }

    private void OnAuthenticationStateChanged(object? sender, ApsAuthenticationStateChangedEventArgs e)
    {
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => OnAuthenticationStateChanged(sender, e)));
            return;
        }

        State = e.NewState;
        DisplayName = e.User?.DisplayName ?? DisplayName;

        if (e.NewState == ApsAuthenticationState.SignedOut || e.NewState == ApsAuthenticationState.Disabled)
        {
            DisplayName = "";
        }

        if (e.NewState == ApsAuthenticationState.Failed || e.NewState == ApsAuthenticationState.ConfigurationInvalid)
        {
            ErrorMessage = e.Message;
        }
        else if (e.NewState == ApsAuthenticationState.SignedIn)
        {
            ErrorMessage = null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void On([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        authenticationService.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        signInCts?.Cancel();
        signInCts?.Dispose();
    }
}
