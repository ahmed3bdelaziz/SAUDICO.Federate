using System;

namespace SAUDICO.Federate.UI;

/// <summary>
/// Framework-independent routing decision for "Add ACC Models". Extracted
/// out of <see cref="Manager"/> so the guarantee that Levels 1 and 2 never
/// reach the real-authentication path is directly unit-testable without
/// constructing real WPF windows or a Revit host.
/// </summary>
public static class AccBrowserLevelRouter
{
    public static void Route(
        AccDiagnosticLevel level,
        Action openLevel1Shell,
        Action openLevel2NoAuthBrowser,
        Action openRealAuthenticatedBrowser)
    {
        switch (level)
        {
            case AccDiagnosticLevel.Level1Shell:
                openLevel1Shell();
                return;
            case AccDiagnosticLevel.Level2AccBrowserNoAuth:
                openLevel2NoAuthBrowser();
                return;
            case AccDiagnosticLevel.RealAuthentication:
                openRealAuthenticatedBrowser();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown ACC diagnostic level.");
        }
    }
}
