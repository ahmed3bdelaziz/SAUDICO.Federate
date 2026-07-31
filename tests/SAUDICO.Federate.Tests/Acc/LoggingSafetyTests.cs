using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using SAUDICO.Federate.ACC.Authentication;
using SAUDICO.Federate.ACC.DataManagement;
using SAUDICO.Federate.ACC.Errors;
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

    [Fact]
    public void ConfigurationDiagnosticsLogging_NeverLogsClientIdValue()
    {
        const string SecretClientId = "SECRET-CLIENT-ID-MARKER-abc123xyz";

        CapturingSink sink = new CapturingSink();
        ILogger previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).MinimumLevel.Verbose().CreateLogger();

        try
        {
            FakeConfigurationService config = new FakeConfigurationService();
            config.Configuration.ClientId = SecretClientId;

            ApsAuthenticationService service = new ApsAuthenticationService(
                config,
                new FakePkceService(),
                new SAUDICO.Federate.ACC.OAuthState.InMemoryOAuthStateStore(),
                new FakeCallbackListener(),
                new FakeAuthorizationClient(),
                new FakeUserProfileService(),
                new FakeTokenStore(),
                new FakeBrowserLauncher());

            Assert.NotNull(service);
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = previous;
        }

        foreach (string message in sink.Messages)
        {
            Assert.DoesNotContain(SecretClientId, message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AccBrowserLauncherFailure_NeverLogsSensitiveMarkerBeyondExceptionText()
    {
        CapturingSink sink = new CapturingSink();
        ILogger previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).MinimumLevel.Verbose().CreateLogger();

        try
        {
            SAUDICO.Federate.UI.AccBrowserLauncher.TryLaunch(
                composeAuthentication: () => throw new InvalidOperationException("simulated composition failure"),
                createAndShowWindow: _ => { },
                showSafeMessage: _ => { });
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = previous;
        }

        foreach (string message in sink.Messages)
        {
            Assert.DoesNotContain("clientId", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("access_token", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("refresh_token", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("code_verifier", message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AccDataManagementClient_UnauthorizedRefreshRetryFlow_NeverLogsAccessTokenValues()
    {
        const string SecretAccessToken1 = "SECRET-DM-ACCESS-TOKEN-MARKER-aa11bb22";
        const string SecretAccessToken2 = "SECRET-DM-REFRESHED-TOKEN-MARKER-cc33dd44";

        CapturingSink sink = new CapturingSink();
        ILogger previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).MinimumLevel.Verbose().CreateLogger();

        try
        {
            FakeDataManagementTransport transport = new FakeDataManagementTransport();
            transport.Responses.Enqueue(() => throw new ApsApiException("unauthorized", 401, "unauthorized"));
            transport.Responses.Enqueue(() => JsonDocument.Parse(
                """{"links":{"self":{"href":"x"}},"data":[{"type":"hubs","id":"hub-1","attributes":{"name":"Acme Hub"}}]}"""));

            FakeAuthenticationServiceForDataManagement auth = new FakeAuthenticationServiceForDataManagement
            {
                AccessToken = SecretAccessToken1,
                RefreshedAccessToken = SecretAccessToken2,
            };

            AccDataManagementClient client = new AccDataManagementClient(transport, auth);
            await client.GetHubsAsync(CancellationToken.None);
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = previous;
        }

        foreach (string message in sink.Messages)
        {
            Assert.DoesNotContain(SecretAccessToken1, message, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretAccessToken2, message, StringComparison.Ordinal);
            Assert.DoesNotContain("Authorization", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer", message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AccDataManagementClient_EntireProjectSearchWithPartial403_NeverLogsAccessTokenValue()
    {
        const string SecretAccessToken = "SECRET-DM-SEARCH-TOKEN-MARKER-ee55ff66";

        CapturingSink sink = new CapturingSink();
        ILogger previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).MinimumLevel.Verbose().CreateLogger();

        try
        {
            FakeDataManagementTransport transport = new FakeDataManagementTransport();
            transport.Responses.Enqueue(() => JsonDocument.Parse(
                """{"links":{"self":{"href":"x"}},"data":[{"type":"folders","id":"top-1","attributes":{"name":"Folder One"}},{"type":"folders","id":"top-2","attributes":{"name":"Folder Two"}}]}"""));
            transport.Responses.Enqueue(() => throw new ApsApiException("forbidden", 403, "forbidden"));
            transport.Responses.Enqueue(() => JsonDocument.Parse(
                """{"links":{"self":{"href":"x"}},"data":[{"type":"versions","id":"ver-1","attributes":{"versionNumber":1},"relationships":{"item":{"data":{"type":"items","id":"item-1"}}}}],"included":[{"type":"items","id":"item-1","attributes":{"displayName":"Model.rvt","pathInProject":"/x"}}]}"""));

            FakeAuthenticationServiceForDataManagement auth = new FakeAuthenticationServiceForDataManagement { AccessToken = SecretAccessToken };
            AccDataManagementClient client = new AccDataManagementClient(transport, auth);

            await client.SearchProjectAsync("hub-1", "proj-1", CancellationToken.None);
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = previous;
        }

        foreach (string message in sink.Messages)
        {
            Assert.DoesNotContain(SecretAccessToken, message, StringComparison.Ordinal);
            Assert.DoesNotContain("Authorization", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer", message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
