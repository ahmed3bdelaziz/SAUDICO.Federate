using System;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.Configuration;
using SAUDICO.Federate.ACC.Pkce;
using SAUDICO.Federate.ACC.Tokens;

namespace SAUDICO.Federate.ACC.Authentication;

/// <summary>Focused typed client for the two APS Authentication v2 HTTP operations this app needs.</summary>
public interface IApsAuthorizationClient
{
    Uri BuildAuthorizationUri(ApsConfiguration configuration, PkcePair pkce, string state);

    Task<ApsToken> ExchangeAuthorizationCodeAsync(
        ApsConfiguration configuration, string code, string codeVerifier, CancellationToken cancellationToken);

    Task<ApsToken> RefreshAsync(
        ApsConfiguration configuration, string refreshToken, CancellationToken cancellationToken);
}
