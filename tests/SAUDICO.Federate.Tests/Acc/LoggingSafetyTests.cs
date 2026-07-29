using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using SAUDICO.Federate.ACC.Authentication;
using SAUDICO.Federate.ACC.Tokens;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

internal sealed class CapturingSink : ILogEventSink
{
    public List<string> Messages { get; } = new();

    public void Emit(Serilog.Events.LogEvent logEvent)
    {
        Messages.Add(logEvent.RenderMessage());
    }
}

public sealed class LoggingSafetyTests
{
    private const string SecretCode = "SECRET-AUTH-CODE-MARKER-9f8e7d";
    private const string SecretVerifier = "SECRET-VERIFIER-MARKER-1a2b3c";
    private const string SecretAccessToken = "SECRET-ACCESS-TOKEN-MARKER-4d5e6f";
    private const string SecretRefreshToken = "SECRET-REFRESH-TOKEN-MARKER-7g8h9i";

    [Fact]
    public async Task SignInFlow_NeverLogsSensitiveValues()
    {
        CapturingSink sink = new CapturingSink();
        ILogger previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).MinimumLevel.Verbose().CreateLogger();

        try
        {
            FakeConfigurationService config = new FakeConfigurationService();
            FakePkceService pkce = new FakePkceService();
            SAUDICO.Federate.ACC.OAuthState.InMemoryOAuthStateStore stateStore = new SAUDICO.Federate.ACC.OAuthState.InMemoryOAuthStateStore();
            FakeCallbackListener callback = new FakeCallbackListener
            {
                Result = () => SAUDICO.Federate.ACC.Callback.OAuthCallbackResult.Ok(SecretCode, "fixed-state")
            };
            FakeAuthorizationClient authClient = new FakeAuthorizationClient
            {
                ExchangeResult = () => new ApsToken
                {
                    AccessToken = SecretAccessToken,
                    RefreshToken = SecretRefreshToken,
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
                }
            };
            FakeUserProfileService profile = new FakeUserProfileService();
            FakeTokenStore tokenStore = new FakeTokenStore();
            FakeBrowserLauncher browser = new FakeBrowserLauncher();

            ApsAuthenticationService service = new ApsAuthenticationService(
                config, pkce, stateStore, callback, authClient, profile, tokenStore, browser);

            await service.SignInAsync(CancellationToken.None);
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = previous;
        }

        foreach (string message in sink.Messages)
        {
            Assert.DoesNotContain(SecretCode, message, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretVerifier, message, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretAccessToken, message, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretRefreshToken, message, StringComparison.Ordinal);
            Assert.DoesNotContain("fixed-verifier", message, StringComparison.Ordinal);
        }
    }
}
