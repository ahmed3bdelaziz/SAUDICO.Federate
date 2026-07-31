using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SAUDICO.Federate.ACC.Callback;
using SAUDICO.Federate.ACC.Configuration;
using SAUDICO.Federate.ACC.Errors;
using SAUDICO.Federate.ACC.OAuthState;
using SAUDICO.Federate.ACC.Pkce;
using SAUDICO.Federate.ACC.Profile;
using SAUDICO.Federate.ACC.Tokens;

namespace SAUDICO.Federate.ACC.Authentication;

/// <summary>
/// Orchestrates the APS Authorization Code + PKCE sign-in flow. Delegates all
/// HTTP, crypto, callback, and storage work to focused collaborators — this
/// class only sequences them and owns the authentication state machine.
/// </summary>
public sealed class ApsAuthenticationService : IApsAuthenticationService, IDisposable
{
    private readonly IApsConfigurationService configurationService;
    private readonly IPkceService pkceService;
    private readonly IOAuthStateStore stateStore;
    private readonly ILocalOAuthCallbackListener callbackListener;
    private readonly IApsAuthorizationClient authorizationClient;
    private readonly IApsUserProfileService profileService;
    private readonly IApsTokenStore tokenStore;
    private readonly ISystemBrowserLauncher browserLauncher;
    private readonly SemaphoreSlim refreshGate = new SemaphoreSlim(1, 1);
    private readonly SemaphoreSlim signInGate = new SemaphoreSlim(1, 1);
    private readonly object gate = new object();
    private readonly TimeSpan callbackTimeout;
    private readonly TimeSpan expirySafetyMargin;

    private ApsConfiguration? configuration;
    private ApsToken? currentToken;

    public ApsAuthenticationState State { get; private set; }
    public ApsUserProfile? CurrentUser { get; private set; }
    public bool IsAuthenticated => State == ApsAuthenticationState.SignedIn;

    public event EventHandler<ApsAuthenticationStateChangedEventArgs>? AuthenticationStateChanged;

    public ApsAuthenticationService(
        IApsConfigurationService configurationService,
        IPkceService pkceService,
        IOAuthStateStore stateStore,
        ILocalOAuthCallbackListener callbackListener,
        IApsAuthorizationClient authorizationClient,
        IApsUserProfileService profileService,
        IApsTokenStore tokenStore,
        ISystemBrowserLauncher browserLauncher,
        TimeSpan? callbackTimeout = null,
        TimeSpan? expirySafetyMargin = null)
    {
        this.configurationService = configurationService;
        this.pkceService = pkceService;
        this.stateStore = stateStore;
        this.callbackListener = callbackListener;
        this.authorizationClient = authorizationClient;
        this.profileService = profileService;
        this.tokenStore = tokenStore;
        this.browserLauncher = browserLauncher;
        this.callbackTimeout = callbackTimeout ?? TimeSpan.FromMinutes(3);
        this.expirySafetyMargin = expirySafetyMargin ?? TimeSpan.FromMinutes(2);

        InitializeConfiguration();
    }

    private void InitializeConfiguration()
    {
        try
        {
            configuration = configurationService.Load();
        }
        catch (Exception ex)
        {
            // Deliberately broad: ANY configuration-load failure (malformed JSON, missing
            // file, deserialization failure, or anything unforeseen) must leave this service
            // in a safe, non-throwing ConfigurationInvalid state rather than escape the
            // constructor. Never logs the raw JSON or Client ID — only the exception type,
            // safe message, and (if available) the distinguishing load-failure reason.
            string reason = (ex as ApsConfigurationException)?.Reason?.ToString() ?? ex.GetType().Name;
            Log.Warning(ex, "APS configuration could not be loaded: {Reason} ({ExceptionType})", reason, ex.GetType().FullName);
            SetState(ApsAuthenticationState.ConfigurationInvalid, ConfigMessage);
            return;
        }

        LogConfigurationDiagnostics(configuration);

        if (!configuration.Enabled)
        {
            SetState(ApsAuthenticationState.Disabled, null);
            return;
        }

        ApsConfigurationValidationResult validation = configurationService.Validate(configuration);
        if (!validation.IsValid)
        {
            Log.Warning("APS configuration invalid with {ErrorCount} error(s)", validation.Errors.Count);
            SetState(ApsAuthenticationState.ConfigurationInvalid, ConfigMessage);
            return;
        }

        SetState(ApsAuthenticationState.SignedOut, null);
    }

    /// <summary>
    /// Logs only the five safe fields required by policy — never the Client ID,
    /// never the full authorization/token URL, never a query string.
    /// </summary>
    private void LogConfigurationDiagnostics(ApsConfiguration config)
    {
        ApsConfigurationDiagnostics diagnostics = configurationService.Diagnose(config);

        string host = "unknown";
        int port = 0;
        string path = "unknown";

        if (Uri.TryCreate(config.CallbackUri, UriKind.Absolute, out Uri? callbackUri))
        {
            host = callbackUri.Host;
            port = callbackUri.Port;
            path = callbackUri.AbsolutePath;
        }

        Log.Information(
            "APS configuration enabled: {Enabled}; Client ID configured: {ClientIdConfigured}; Callback host: {CallbackHost}; Callback port: {CallbackPort}; Callback path: {CallbackPath}",
            diagnostics.IsEnabled, diagnostics.IsClientIdConfigured, host, port, path);
    }

    private const string ConfigMessage = "APS is not configured. Add the SAUDICO Federate APS Client ID.";

    public async Task<ApsUserProfile> SignInAsync(CancellationToken cancellationToken)
    {
        if (configuration == null || State == ApsAuthenticationState.Disabled)
        {
            throw new ApsAuthenticationException(ConfigMessage, ApsAuthenticationFailureReason.ConfigurationInvalid);
        }

        ApsConfigurationValidationResult validation = configurationService.Validate(configuration);
        if (!validation.IsValid)
        {
            SetState(ApsAuthenticationState.ConfigurationInvalid, ConfigMessage);
            throw new ApsAuthenticationException(ConfigMessage, ApsAuthenticationFailureReason.ConfigurationInvalid);
        }

        await signInGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetState(ApsAuthenticationState.SigningIn, "Waiting for Autodesk sign-in...");
            Log.Information("APS authentication initiated");

            PkcePair pkce = pkceService.Create();
            string state = pkceService.GenerateState();
            stateStore.Begin(new OAuthAuthorizationState
            {
                State = state,
                CodeVerifier = pkce.CodeVerifier,
                CreatedAtUtc = DateTime.UtcNow
            });

            Uri callbackUri = new Uri(configuration.CallbackUri);
            Task<OAuthCallbackResult> callbackTask = callbackListener.ListenAsync(callbackUri, callbackTimeout, cancellationToken);

            Uri authorizeUri = authorizationClient.BuildAuthorizationUri(configuration, pkce, state);

            OAuthCallbackResult callback;
            try
            {
                browserLauncher.Launch(authorizeUri);
                Log.Information("System browser launched for APS sign-in");

                callback = await callbackTask.ConfigureAwait(false);
            }
            catch (ApsAuthenticationException ex) when (ex.Reason == ApsAuthenticationFailureReason.CallbackBindFailed)
            {
                Log.Error("APS callback listener failed to bind");
                stateStore.Clear();
                SetState(ApsAuthenticationState.Failed, "SAUDICO Federate could not start the local Autodesk sign-in callback.");
                throw;
            }

            Log.Information("APS callback received");

            if (callback.IsCancelled)
            {
                stateStore.Clear();
                SetState(ApsAuthenticationState.SignedOut, null);
                throw new ApsAuthenticationException(
                    "Autodesk sign-in was cancelled or access was not granted.", ApsAuthenticationFailureReason.Cancelled);
            }

            if (callback.IsTimedOut)
            {
                stateStore.Clear();
                Log.Warning("APS callback timed out");
                SetState(ApsAuthenticationState.Failed, "Autodesk sign-in did not complete before the request expired.");
                throw new ApsAuthenticationException(
                    "Autodesk sign-in did not complete before the request expired.", ApsAuthenticationFailureReason.Timeout);
            }

            if (!callback.Success)
            {
                stateStore.Clear();
                Log.Warning("APS authorization denied");
                SetState(ApsAuthenticationState.SignedOut, "Autodesk sign-in was cancelled or access was not granted.");
                throw new ApsAuthorizationDeniedException(
                    "Autodesk sign-in was cancelled or access was not granted.", callback.Error);
            }

            if (!stateStore.TryConsume(callback.State!, out OAuthAuthorizationState? matched) || matched == null)
            {
                Log.Warning("APS callback state mismatch");
                SetState(ApsAuthenticationState.Failed, "Autodesk sign-in validation failed. Please try again.");
                throw new ApsAuthenticationException(
                    "Autodesk sign-in validation failed. Please try again.", ApsAuthenticationFailureReason.StateMismatch);
            }

            ApsToken token;
            try
            {
                token = await authorizationClient
                    .ExchangeAuthorizationCodeAsync(configuration, callback.Code!, matched.CodeVerifier, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ApsApiException ex)
            {
                Log.Warning("APS token exchange failed: HTTP {StatusCode} {ErrorCode}", ex.StatusCode, ex.ApsErrorCode);
                string message = "SAUDICO Federate could not reach Autodesk Platform Services.";
                SetState(ApsAuthenticationState.Failed, message);
                throw new ApsAuthenticationException(message, ApsAuthenticationFailureReason.Network, ex);
            }

            if (string.IsNullOrEmpty(token.AccessToken))
            {
                SetState(ApsAuthenticationState.Failed, "Autodesk sign-in returned an unexpected response.");
                throw new ApsAuthenticationException(
                    "Autodesk sign-in returned an unexpected response.", ApsAuthenticationFailureReason.UnexpectedResponse);
            }

            lock (gate)
            {
                currentToken = token;
            }

            await tokenStore.SaveAsync(token, cancellationToken).ConfigureAwait(false);

            ApsUserProfile profile = await profileService
                .GetCurrentUserAsync(configuration.UserProfileEndpoint, token.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            CurrentUser = profile;
            Log.Information("APS authentication succeeded");
            SetState(ApsAuthenticationState.SignedIn, null, profile);
            return profile;
        }
        finally
        {
            signInGate.Release();
        }
    }

    public async Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken)
    {
        EnsureUsable();

        ApsToken? token;
        lock (gate)
        {
            token = currentToken;
        }

        if (token != null && !token.IsExpired(expirySafetyMargin))
        {
            return token.AccessToken;
        }

        return await RefreshAccessTokenCoreAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
    }

    public Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        EnsureUsable();
        return RefreshAccessTokenCoreAsync(forceRefresh: true, cancellationToken);
    }

    private void EnsureUsable()
    {
        if (configuration == null || State == ApsAuthenticationState.Disabled ||
            State == ApsAuthenticationState.ConfigurationInvalid)
        {
            throw new ApsAuthenticationException(ConfigMessage, ApsAuthenticationFailureReason.ConfigurationInvalid);
        }
    }

    /// <summary>
    /// Shared refresh implementation. When <paramref name="forceRefresh"/> is
    /// false (the <see cref="GetValidAccessTokenAsync"/> path), the
    /// double-checked-locking re-validation inside the gate is preserved
    /// exactly as before, so concurrent callers still produce exactly one
    /// HTTP refresh call. When true (the forced-refresh path), that
    /// re-validation is skipped and a real refresh-token grant call is
    /// always attempted.
    /// </summary>
    private async Task<string> RefreshAccessTokenCoreAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ApsToken? token;
            lock (gate)
            {
                token = currentToken;
            }

            if (!forceRefresh && token != null && !token.IsExpired(expirySafetyMargin))
            {
                return token.AccessToken;
            }

            string? refreshToken = token?.RefreshToken;
            if (refreshToken == null)
            {
                ApsToken? stored = await tokenStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                refreshToken = stored?.RefreshToken;
            }

            if (refreshToken == null)
            {
                SetState(ApsAuthenticationState.SignedOut, null);
                throw new ApsSessionExpiredException("Your Autodesk session has expired. Sign in again.");
            }

            ApsAuthenticationState previousState = State;
            SetState(ApsAuthenticationState.Refreshing, null);
            Log.Information("APS token refresh started");

            ApsToken refreshed;
            try
            {
                refreshed = await authorizationClient
                    .RefreshAsync(configuration!, refreshToken, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ApsApiException ex) when (string.Equals(ex.ApsErrorCode, "invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("APS token refresh failed: invalid_grant");
                await tokenStore.DeleteAsync(cancellationToken).ConfigureAwait(false);

                lock (gate)
                {
                    currentToken = null;
                }

                CurrentUser = null;
                SetState(ApsAuthenticationState.SignedOut, "Your Autodesk session has expired. Sign in again.");
                throw new ApsSessionExpiredException("Your Autodesk session has expired. Sign in again.", ex);
            }
            catch (ApsApiException ex)
            {
                Log.Warning("APS token refresh failed: HTTP {StatusCode}", ex.StatusCode);
                SetState(previousState, null);
                throw new ApsAuthenticationException(
                    "SAUDICO Federate could not reach Autodesk Platform Services.",
                    ApsAuthenticationFailureReason.Network, ex);
            }

            refreshed.RefreshToken ??= refreshToken;

            lock (gate)
            {
                currentToken = refreshed;
            }

            await tokenStore.SaveAsync(refreshed, cancellationToken).ConfigureAwait(false);
            Log.Information("APS token refresh succeeded");
            SetState(ApsAuthenticationState.SignedIn, null);

            return refreshed.AccessToken;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        Log.Information("APS sign-out requested");

        lock (gate)
        {
            currentToken = null;
        }

        CurrentUser = null;
        await tokenStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
        stateStore.Clear();

        if (configuration != null && configuration.Enabled)
        {
            ApsConfigurationValidationResult validation = configurationService.Validate(configuration);
            SetState(validation.IsValid ? ApsAuthenticationState.SignedOut : ApsAuthenticationState.ConfigurationInvalid, null);
        }
        else
        {
            SetState(ApsAuthenticationState.Disabled, null);
        }
    }

    private void SetState(ApsAuthenticationState newState, string? message, ApsUserProfile? user = null)
    {
        ApsAuthenticationState old;
        lock (gate)
        {
            old = State;
            State = newState;
            if (user != null)
            {
                CurrentUser = user;
            }
        }

        AuthenticationStateChanged?.Invoke(this, new ApsAuthenticationStateChangedEventArgs
        {
            OldState = old,
            NewState = newState,
            User = CurrentUser,
            Message = message
        });
    }

    public void Dispose()
    {
        refreshGate.Dispose();
        signInGate.Dispose();
    }
}
