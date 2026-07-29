namespace SAUDICO.Federate.ACC.Errors;

/// <summary>
/// Raised when the user cancels consent or Autodesk returns an OAuth
/// error/access_denied on the callback. Never carries raw query values.
/// </summary>
public sealed class ApsAuthorizationDeniedException : ApsAuthenticationException
{
    public string? OAuthError { get; }

    public ApsAuthorizationDeniedException(string message, string? oauthError)
        : base(message, ApsAuthenticationFailureReason.Cancelled)
    {
        OAuthError = oauthError;
    }
}
