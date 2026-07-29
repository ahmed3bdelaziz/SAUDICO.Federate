namespace SAUDICO.Federate.ACC.Authentication;

public enum ApsAuthenticationState
{
    Disabled,
    ConfigurationInvalid,
    SignedOut,
    SigningIn,
    SignedIn,
    Refreshing,
    Expired,
    Failed
}
