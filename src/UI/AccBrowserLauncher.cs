using System;
using SAUDICO.Federate.ACC.Authentication;

namespace SAUDICO.Federate.UI;

/// <summary>
/// The single non-escaping boundary around the entire "Add ACC Models"
/// operation: lazy authentication-service composition, ViewModel/window
/// construction, and display. Any exception from any of those stages is
/// caught here, logged safely (never a Client ID, token, code, state, or
/// verifier), and reported back through a safe user-facing message — it
/// never propagates out and never terminates the host process.
///
/// Framework-independent by design (no WPF types) so the catch/log/report
/// behavior is directly unit-testable without constructing real windows.
/// </summary>
public static class AccBrowserLauncher
{
    public const string SafeFailureMessage =
        "SAUDICO Federate could not open the ACC Browser.\nReview the application log for details.";

    public static bool TryLaunch(
        Func<IApsAuthenticationService> composeAuthentication,
        Action<IApsAuthenticationService> createAndShowWindow,
        Action<string> showSafeMessage,
        Func<string>? describeSafeDiagnostics = null)
    {
        string stage = "ComposeAuthentication";

        try
        {
            IApsAuthenticationService authentication = composeAuthentication();

            stage = "ConstructWindow";
            createAndShowWindow(authentication);

            return true;
        }
        catch (Exception ex)
        {
            LogFailure(stage, ex, describeSafeDiagnostics);
            showSafeMessage(SafeFailureMessage);
            return false;
        }
    }

    private static void LogFailure(string stage, Exception ex, Func<string>? describeSafeDiagnostics)
    {
        string diagnostics = "unavailable";

        if (describeSafeDiagnostics != null)
        {
            try
            {
                diagnostics = describeSafeDiagnostics();
            }
            catch
            {
                diagnostics = "unavailable";
            }
        }

        try
        {
            Serilog.Log.Error(
                ex,
                "Add ACC Models failed at stage {Stage}. {ExceptionType}: {ExceptionMessage}. Diagnostics: {Diagnostics}",
                stage, ex.GetType().FullName, ex.Message, diagnostics);

            Exception? inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth < 5)
            {
                Serilog.Log.Error(
                    "Add ACC Models inner exception (depth {Depth}): {ExceptionType}: {ExceptionMessage}",
                    depth, inner.GetType().FullName, inner.Message);
                inner = inner.InnerException;
                depth++;
            }
        }
        catch
        {
            // Logging must never itself become the reason this boundary fails.
        }
    }
}
