using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.Authentication;
using SAUDICO.Federate.ACC.Callback;
using SAUDICO.Federate.ACC.Configuration;
using SAUDICO.Federate.ACC.Pkce;
using SAUDICO.Federate.ACC.Profile;
using SAUDICO.Federate.ACC.Tokens;

namespace SAUDICO.Federate.Tests.Acc;

internal sealed class FakeConfigurationService : IApsConfigurationService
{
    public ApsConfiguration Configuration { get; set; } = new ApsConfiguration
    {
        SchemaVersion = 1,
        Enabled = true,
        ClientId = "test-client",
        CallbackUri = "http://localhost:39999/callback/",
        AuthorizationEndpoint = "https://developer.api.autodesk.com/authentication/v2/authorize",
        TokenEndpoint = "https://developer.api.autodesk.com/authentication/v2/token",
        UserProfileEndpoint = "https://api.userprofile.autodesk.com/userinfo",
        Scopes = new List<string> { "data:read", "user-profile:read", "openid" }
    };

    public bool ForceInvalid { get; set; }

    public ApsConfiguration Load() => Configuration;

    public ApsConfigurationValidationResult Validate(ApsConfiguration configuration) =>
        ForceInvalid
            ? ApsConfigurationValidationResult.Failure(new[] { "forced invalid" })
            : ApsConfigurationValidationResult.Success();
}

internal sealed class FakePkceService : IPkceService
{
    public PkcePair Create() => new PkcePair { CodeVerifier = "fixed-verifier", CodeChallenge = "fixed-challenge", CodeChallengeMethod = "S256" };
    public string GenerateState() => "fixed-state";
}

internal sealed class FakeCallbackListener : ILocalOAuthCallbackListener
{
    public Func<OAuthCallbackResult> Result { get; set; } = () => OAuthCallbackResult.Ok("fixed-code", "fixed-state");

    public Task<OAuthCallbackResult> ListenAsync(Uri callbackUri, TimeSpan timeout, CancellationToken cancellationToken) =>
        Task.FromResult(Result());
}

internal sealed class FakeAuthorizationClient : IApsAuthorizationClient
{
    public int ExchangeCallCount;
    public int RefreshCallCount;
    public Func<ApsToken>? ExchangeResult;
    public Func<ApsToken>? RefreshResult;
    public Exception? ThrowOnRefresh;

    public Uri BuildAuthorizationUri(ApsConfiguration configuration, PkcePair pkce, string state) =>
        new Uri(configuration.AuthorizationEndpoint + "?state=" + state);

    public Task<ApsToken> ExchangeAuthorizationCodeAsync(ApsConfiguration configuration, string code, string codeVerifier, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref ExchangeCallCount);
        return Task.FromResult((ExchangeResult ?? (() => new ApsToken
        {
            AccessToken = "access-1",
            RefreshToken = "refresh-1",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        }))());
    }

    public async Task<ApsToken> RefreshAsync(ApsConfiguration configuration, string refreshToken, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref RefreshCallCount);
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);

        if (ThrowOnRefresh != null)
        {
            throw ThrowOnRefresh;
        }

        return (RefreshResult ?? (() => new ApsToken
        {
            AccessToken = "access-2",
            RefreshToken = "refresh-2",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        }))();
    }
}

internal sealed class FakeUserProfileService : IApsUserProfileService
{
    public Task<ApsUserProfile> GetCurrentUserAsync(string userProfileEndpoint, string accessToken, CancellationToken cancellationToken) =>
        Task.FromResult(new ApsUserProfile { UserId = "user-1", DisplayName = "Test User" });
}

internal sealed class FakeTokenStore : IApsTokenStore
{
    public ApsToken? Saved;
    public bool Deleted;

    public Task SaveAsync(ApsToken token, CancellationToken cancellationToken)
    {
        Saved = token;
        Deleted = false;
        return Task.CompletedTask;
    }

    public Task<ApsToken?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Saved);

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        Saved = null;
        Deleted = true;
        return Task.CompletedTask;
    }
}

internal sealed class FakeBrowserLauncher : ISystemBrowserLauncher
{
    public Uri? LastLaunched;
    public void Launch(Uri uri) => LastLaunched = uri;
}
