using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.Callback;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

public sealed class LocalOAuthCallbackListenerTests
{
    private static Uri CallbackUri(int port) => new Uri($"http://localhost:{port}/saudico-test-callback/");

    [Fact]
    public async Task ValidCodeAndState_ReturnsSuccess()
    {
        Uri uri = CallbackUri(38761);
        LocalOAuthCallbackListener listener = new LocalOAuthCallbackListener();
        Task<OAuthCallbackResult> listenTask = listener.ListenAsync(uri, TimeSpan.FromSeconds(5), CancellationToken.None);

        using HttpClient client = new HttpClient();
        await client.GetAsync(new Uri(uri, "?code=abc123&state=xyz789"));

        OAuthCallbackResult result = await listenTask;

        Assert.True(result.Success);
        Assert.Equal("abc123", result.Code);
        Assert.Equal("xyz789", result.State);
    }

    [Fact]
    public async Task AuthorizationDenied_ReturnsDenied()
    {
        Uri uri = CallbackUri(38762);
        LocalOAuthCallbackListener listener = new LocalOAuthCallbackListener();
        Task<OAuthCallbackResult> listenTask = listener.ListenAsync(uri, TimeSpan.FromSeconds(5), CancellationToken.None);

        using HttpClient client = new HttpClient();
        await client.GetAsync(new Uri(uri, "?error=access_denied&error_description=user+cancelled"));

        OAuthCallbackResult result = await listenTask;

        Assert.False(result.Success);
        Assert.Equal("access_denied", result.Error);
    }

    [Fact]
    public async Task MissingCode_ReturnsDenied()
    {
        Uri uri = CallbackUri(38763);
        LocalOAuthCallbackListener listener = new LocalOAuthCallbackListener();
        Task<OAuthCallbackResult> listenTask = listener.ListenAsync(uri, TimeSpan.FromSeconds(5), CancellationToken.None);

        using HttpClient client = new HttpClient();
        await client.GetAsync(new Uri(uri, "?state=xyz789"));

        OAuthCallbackResult result = await listenTask;

        Assert.False(result.Success);
        Assert.Equal("missing_parameters", result.Error);
    }

    [Fact]
    public async Task Cancellation_ReturnsCancelled()
    {
        Uri uri = CallbackUri(38764);
        LocalOAuthCallbackListener listener = new LocalOAuthCallbackListener();
        using CancellationTokenSource cts = new CancellationTokenSource();

        Task<OAuthCallbackResult> listenTask = listener.ListenAsync(uri, TimeSpan.FromSeconds(30), cts.Token);
        cts.Cancel();

        OAuthCallbackResult result = await listenTask;

        Assert.True(result.IsCancelled);
    }

    [Fact]
    public async Task Timeout_ReturnsTimedOut()
    {
        Uri uri = CallbackUri(38765);
        LocalOAuthCallbackListener listener = new LocalOAuthCallbackListener();

        OAuthCallbackResult result = await listener.ListenAsync(uri, TimeSpan.FromMilliseconds(200), CancellationToken.None);

        Assert.True(result.IsTimedOut);
    }

    [Fact]
    public async Task ListenerDisposesAndReleasesThePort()
    {
        Uri uri = CallbackUri(38766);
        LocalOAuthCallbackListener first = new LocalOAuthCallbackListener();
        await first.ListenAsync(uri, TimeSpan.FromMilliseconds(200), CancellationToken.None);

        // If the first listener failed to stop/dispose, binding again would throw.
        LocalOAuthCallbackListener second = new LocalOAuthCallbackListener();
        OAuthCallbackResult result = await second.ListenAsync(uri, TimeSpan.FromMilliseconds(200), CancellationToken.None);

        Assert.True(result.IsTimedOut);
    }
}
