using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.Authentication;
using SAUDICO.Federate.ACC.Callback;
using SAUDICO.Federate.ACC.Errors;
using SAUDICO.Federate.ACC.Tokens;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

public sealed class ApsAuthenticationServiceTests
{
    private static (ApsAuthenticationService service, FakeAuthorizationClient authClient, FakeTokenStore tokenStore, FakeCallbackListener callback)
        Build(TimeSpan? expirySafetyMargin = null)
    {
        FakeConfigurationService config = new FakeConfigurationService();
        FakePkceService pkce = new FakePkceService();
        SAUDICO.Federate.ACC.OAuthState.InMemoryOAuthStateStore stateStore = new SAUDICO.Federate.ACC.OAuthState.InMemoryOAuthStateStore();
        FakeCallbackListener callback = new FakeCallbackListener();
        FakeAuthorizationClient authClient = new FakeAuthorizationClient();
        FakeUserProfileService profile = new FakeUserProfileService();
        FakeTokenStore tokenStore = new FakeTokenStore();
        FakeBrowserLauncher browser = new FakeBrowserLauncher();

        ApsAuthenticationService service = new ApsAuthenticationService(
            config, pkce, stateStore, callback, authClient, profile, tokenStore, browser,
            callbackTimeout: TimeSpan.FromSeconds(5),
            expirySafetyMargin: expirySafetyMargin ?? TimeSpan.FromMinutes(2));

        return (service, authClient, tokenStore, callback);
    }

    [Fact]
    public async Task SignIn_HappyPath_SignsInAndReturnsToken()
    {
        (ApsAuthenticationService service, FakeAuthorizationClient authClient, _, _) = Build();

        await service.SignInAsync(CancellationToken.None);

        Assert.Equal(ApsAuthenticationState.SignedIn, service.State);
        Assert.True(service.IsAuthenticated);
        Assert.Equal("user-1", service.CurrentUser!.UserId);

        string token = await service.GetValidAccessTokenAsync(CancellationToken.None);
        Assert.Equal("access-1", token);
        Assert.Equal(0, authClient.RefreshCallCount);
    }

    [Fact]
    public async Task ValidToken_IsReusedWithoutRefresh()
    {
        (ApsAuthenticationService service, FakeAuthorizationClient authClient, _, _) = Build();
        await service.SignInAsync(CancellationToken.None);

        await service.GetValidAccessTokenAsync(CancellationToken.None);
        await service.GetValidAccessTokenAsync(CancellationToken.None);

        Assert.Equal(0, authClient.RefreshCallCount);
    }

    [Fact]
    public async Task NearExpiryToken_TriggersRefresh()
    {
        (ApsAuthenticationService service, FakeAuthorizationClient authClient, _, _) = Build();
        authClient.ExchangeResult = () => new ApsToken
        {
            AccessToken = "access-1",
            RefreshToken = "refresh-1",
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30)
        };

        await service.SignInAsync(CancellationToken.None);
        string token = await service.GetValidAccessTokenAsync(CancellationToken.None);

        Assert.Equal("access-2", token);
        Assert.Equal(1, authClient.RefreshCallCount);
    }

    [Fact]
    public async Task ConcurrentCallers_ProduceOnlyOneRefresh()
    {
        (ApsAuthenticationService service, FakeAuthorizationClient authClient, _, _) = Build();
        authClient.ExchangeResult = () => new ApsToken
        {
            AccessToken = "access-1",
            RefreshToken = "refresh-1",
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30)
        };

        await service.SignInAsync(CancellationToken.None);

        Task<string>[] tasks = Enumerable.Range(0, 5)
            .Select(_ => service.GetValidAccessTokenAsync(CancellationToken.None))
            .ToArray();
        string[] results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("access-2", r));
        Assert.Equal(1, authClient.RefreshCallCount);
    }

    [Fact]
    public async Task RotatedRefreshToken_IsPersisted()
    {
        (ApsAuthenticationService service, FakeAuthorizationClient authClient, FakeTokenStore tokenStore, _) = Build();
        authClient.ExchangeResult = () => new ApsToken
        {
            AccessToken = "access-1",
            RefreshToken = "refresh-1",
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30)
        };

        await service.SignInAsync(CancellationToken.None);
        await service.GetValidAccessTokenAsync(CancellationToken.None);

        Assert.Equal("refresh-2", tokenStore.Saved!.RefreshToken);
    }

    [Fact]
    public async Task InvalidGrant_ClearsAuthentication()
    {
        (ApsAuthenticationService service, FakeAuthorizationClient authClient, FakeTokenStore tokenStore, _) = Build();
        authClient.ExchangeResult = () => new ApsToken
        {
            AccessToken = "access-1",
            RefreshToken = "refresh-1",
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30)
        };

        await service.SignInAsync(CancellationToken.None);
        authClient.ThrowOnRefresh = new ApsApiException("invalid_grant", 400, "invalid_grant");

        await Assert.ThrowsAsync<ApsSessionExpiredException>(() => service.GetValidAccessTokenAsync(CancellationToken.None));

        Assert.Equal(ApsAuthenticationState.SignedOut, service.State);
        Assert.True(tokenStore.Deleted);
    }

    [Fact]
    public async Task AuthorizationDenied_ThrowsAndStaysSignedOut()
    {
        (ApsAuthenticationService service, _, _, FakeCallbackListener callback) = Build();
        callback.Result = () => OAuthCallbackResult.Denied("access_denied", "user declined");

        await Assert.ThrowsAsync<ApsAuthorizationDeniedException>(() => service.SignInAsync(CancellationToken.None));

        Assert.Equal(ApsAuthenticationState.SignedOut, service.State);
    }

    [Fact]
    public async Task StateMismatch_ThrowsAndFails()
    {
        (ApsAuthenticationService service, _, _, FakeCallbackListener callback) = Build();
        callback.Result = () => OAuthCallbackResult.Ok("some-code", "an-unexpected-state");

        ApsAuthenticationException ex = await Assert.ThrowsAsync<ApsAuthenticationException>(
            () => service.SignInAsync(CancellationToken.None));

        Assert.Equal(ApsAuthenticationFailureReason.StateMismatch, ex.Reason);
        Assert.Equal(ApsAuthenticationState.Failed, service.State);
    }

    [Fact]
    public async Task Timeout_ThrowsTimeoutReason()
    {
        (ApsAuthenticationService service, _, _, FakeCallbackListener callback) = Build();
        callback.Result = () => OAuthCallbackResult.TimedOut();

        ApsAuthenticationException ex = await Assert.ThrowsAsync<ApsAuthenticationException>(
            () => service.SignInAsync(CancellationToken.None));

        Assert.Equal(ApsAuthenticationFailureReason.Timeout, ex.Reason);
    }

    [Fact]
    public async Task SignOut_ClearsStoredTokenAndCurrentUser()
    {
        (ApsAuthenticationService service, _, FakeTokenStore tokenStore, _) = Build();
        await service.SignInAsync(CancellationToken.None);

        await service.SignOutAsync(CancellationToken.None);

        Assert.Equal(ApsAuthenticationState.SignedOut, service.State);
        Assert.Null(service.CurrentUser);
        Assert.True(tokenStore.Deleted);
    }
}
