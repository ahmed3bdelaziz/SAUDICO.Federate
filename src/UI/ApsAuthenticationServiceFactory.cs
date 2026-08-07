using SAUDICO.Federate.ACC.Authentication;
using SAUDICO.Federate.ACC.Callback;
using SAUDICO.Federate.ACC.Configuration;
using SAUDICO.Federate.ACC.Http;
using SAUDICO.Federate.ACC.OAuthState;
using SAUDICO.Federate.ACC.Pkce;
using SAUDICO.Federate.ACC.Profile;
using SAUDICO.Federate.ACC.Tokens;

namespace SAUDICO.Federate.UI;

internal static class ApsAuthenticationServiceFactory
{
    public static ApsAuthenticationService Create()
    {
        ApsHttpTransport transport = new ApsHttpTransport();

        return new ApsAuthenticationService(
            new ApsConfigurationService(),
            new PkceService(),
            new InMemoryOAuthStateStore(),
            new LocalOAuthCallbackListener(),
            new ApsAuthorizationClient(transport),
            new ApsUserProfileService(transport),
            new DpapiApsTokenStore(),
            new SystemBrowserLauncher());
    }
}
