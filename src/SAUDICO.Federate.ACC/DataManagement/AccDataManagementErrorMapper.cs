using SAUDICO.Federate.ACC.Errors;

namespace SAUDICO.Federate.ACC.DataManagement;

/// <summary>
/// Maps an <see cref="ApsApiException"/> from a Data Management GET call to
/// a fixed, safe, user-facing message — never the raw response body or
/// status text. Matches the required mapping table exactly.
/// </summary>
public static class AccDataManagementErrorMapper
{
    public const string SessionExpiredMessage = "Your Autodesk session has expired. Sign in again.";

    public static string ToFriendlyMessage(ApsApiException ex)
    {
        int? status = ex.StatusCode;

        if (status == 401)
        {
            return SessionExpiredMessage;
        }

        if (status == 403)
        {
            return "You do not have sufficient access to this Autodesk project or account.";
        }

        if (status == 404)
        {
            return "This item is unavailable.";
        }

        if (status == 429)
        {
            return "Autodesk Construction Cloud is rate-limiting requests right now. Please try again shortly.";
        }

        if (status >= 500 && status <= 599)
        {
            return "Autodesk Construction Cloud service is unavailable right now.";
        }

        if (status == null)
        {
            return "A network connection to Autodesk Construction Cloud is unavailable.";
        }

        return "Autodesk Construction Cloud returned an unexpected error.";
    }
}
