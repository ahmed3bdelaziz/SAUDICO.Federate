using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.Authentication;
using SAUDICO.Federate.ACC.Configuration;
using SAUDICO.Federate.ACC.Http;
using SAUDICO.Federate.ACC.Pkce;
using SAUDICO.Federate.ACC.Tokens;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

public sealed class ApsAuthorizationClientTests
{
    private static ApsConfiguration Config() => new ApsConfiguration
    {
        SchemaVersion = 1,
        Enabled = true,
        ClientId = "test-client",
        CallbackUri = "http://localhost:39999/callback/",
        AuthorizationEndpoint = "https://developer.api.autodesk.com/authentication/v2/authorize",
        TokenEndpoint = "https://developer.api.autodesk.com/authentication/v2/token",
        UserProfileEndpoint = "https://api.userprofile.autodesk.com/userinfo",
        Scopes = new System.Collections.Generic.List<string> { "data:read", "openid" }
    };

    [Fact]
    public async Task TokenExchange_ComputesCorrectUtcExpiry()
    {
        string? capturedBody = null;
        FakeHttpMessageHandler handler = new FakeHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"access_token\":\"tok\",\"token_type\":\"Bearer\",\"expires_in\":3600,\"refresh_token\":\"ref\"}",
                    Encoding.UTF8, "application/json")
            };
        });

        ApsAuthorizationClient client = new ApsAuthorizationClient(new ApsHttpTransport(new HttpClient(handler)));
        DateTime before = DateTime.UtcNow;

        ApsToken token = await client.ExchangeAuthorizationCodeAsync(Config(), "code", "verifier", CancellationToken.None);

        DateTime after = DateTime.UtcNow;

        Assert.InRange(token.ExpiresAtUtc, before.AddSeconds(3599), after.AddSeconds(3601));
        Assert.Equal("tok", token.AccessToken);
        Assert.Equal("ref", token.RefreshToken);
        Assert.DoesNotContain("client_secret", capturedBody);
        Assert.Contains("grant_type=authorization_code", capturedBody);
        Assert.Contains("code_verifier=verifier", capturedBody);
    }

    [Fact]
    public void AuthorizationUri_ContainsRequiredParameters()
    {
        ApsAuthorizationClient client = new ApsAuthorizationClient(new ApsHttpTransport());
        PkcePair pkce = new PkcePair { CodeVerifier = "v", CodeChallenge = "challenge-value", CodeChallengeMethod = "S256" };

        Uri uri = client.BuildAuthorizationUri(Config(), pkce, "state-value");
        string query = uri.Query;

        Assert.Contains("response_type=code", query);
        Assert.Contains("client_id=test-client", query);
        Assert.Contains("code_challenge=challenge-value", query);
        Assert.Contains("code_challenge_method=S256", query);
        Assert.Contains("state=state-value", query);
        Assert.DoesNotContain("client_secret", query);
    }
}
