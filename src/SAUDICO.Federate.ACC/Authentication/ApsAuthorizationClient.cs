using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.Configuration;
using SAUDICO.Federate.ACC.Http;
using SAUDICO.Federate.ACC.Pkce;
using SAUDICO.Federate.ACC.Tokens;

namespace SAUDICO.Federate.ACC.Authentication;

public sealed class ApsAuthorizationClient : IApsAuthorizationClient
{
    private readonly IApsHttpTransport transport;

    public ApsAuthorizationClient(IApsHttpTransport transport)
    {
        this.transport = transport;
    }

    public Uri BuildAuthorizationUri(ApsConfiguration configuration, PkcePair pkce, string state)
    {
        string scope = string.Join(" ", configuration.Scopes);

        StringBuilder builder = new StringBuilder(configuration.AuthorizationEndpoint);
        builder.Append('?');
        AppendParam(builder, "response_type", "code", first: true);
        AppendParam(builder, "client_id", configuration.ClientId);
        AppendParam(builder, "redirect_uri", configuration.CallbackUri);
        AppendParam(builder, "scope", scope);
        AppendParam(builder, "state", state);
        AppendParam(builder, "code_challenge", pkce.CodeChallenge);
        AppendParam(builder, "code_challenge_method", pkce.CodeChallengeMethod);

        return new Uri(builder.ToString());
    }

    public async Task<ApsToken> ExchangeAuthorizationCodeAsync(
        ApsConfiguration configuration, string code, string codeVerifier, CancellationToken cancellationToken)
    {
        List<KeyValuePair<string, string>> form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"),
            new("client_id", configuration.ClientId),
            new("code", code),
            new("redirect_uri", configuration.CallbackUri),
            new("code_verifier", codeVerifier)
        };

        JsonDocument response = await transport
            .PostFormAsync(configuration.TokenEndpoint, form, cancellationToken)
            .ConfigureAwait(false);

        using (response)
        {
            return ParseToken(response);
        }
    }

    public async Task<ApsToken> RefreshAsync(
        ApsConfiguration configuration, string refreshToken, CancellationToken cancellationToken)
    {
        List<KeyValuePair<string, string>> form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "refresh_token"),
            new("client_id", configuration.ClientId),
            new("refresh_token", refreshToken)
        };

        JsonDocument response = await transport
            .PostFormAsync(configuration.TokenEndpoint, form, cancellationToken)
            .ConfigureAwait(false);

        using (response)
        {
            return ParseToken(response);
        }
    }

    private static ApsToken ParseToken(JsonDocument document)
    {
        JsonElement root = document.RootElement;

        string accessToken = GetString(root, "access_token") ?? "";
        string tokenType = GetString(root, "token_type") ?? "Bearer";
        string? refreshToken = GetString(root, "refresh_token");
        string? idToken = GetString(root, "id_token");
        int expiresIn = root.TryGetProperty("expires_in", out JsonElement expiresElement) && expiresElement.TryGetInt32(out int seconds)
            ? seconds
            : 0;

        return new ApsToken
        {
            AccessToken = accessToken,
            TokenType = tokenType,
            RefreshToken = refreshToken,
            IdToken = idToken,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn)
        };
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement element) ? element.GetString() : null;

    private static void AppendParam(StringBuilder builder, string name, string value, bool first = false)
    {
        if (!first)
        {
            builder.Append('&');
        }

        builder.Append(name).Append('=').Append(Uri.EscapeDataString(value ?? ""));
    }
}
