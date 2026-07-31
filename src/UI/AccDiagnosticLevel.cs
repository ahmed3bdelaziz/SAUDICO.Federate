namespace SAUDICO.Federate.UI;

/// <summary>
/// Selects which "Add ACC Models" isolation level is active. Temporary —
/// exists only while diagnosing the Revit 2025 crash. Flip
/// <see cref="AccDiagnosticLevelSelection.Current"/> back to
/// <see cref="RealAuthentication"/> once the crash cause is found and fixed.
/// </summary>
public enum AccDiagnosticLevel
{
    /// <summary>Minimal WPF shell only — no ACC styling, no authentication. Known-good fallback.</summary>
    Level1Shell,

    /// <summary>Real AccBrowserWindow shell (title/dimensions/header/styling) with static content, no authentication.</summary>
    Level2AccBrowserNoAuth,

    /// <summary>The real, fully authenticated ACC browser.</summary>
    RealAuthentication,
}

/// <summary>
/// Single place that selects the active diagnostic level. Kept separate
/// from <see cref="Manager"/> so it can be changed without touching the
/// window code-behind. Level 1 and Level 2 both passed their manual Revit
/// 2025 runtime retests; this now selects the real, fully authenticated
/// path. Level 1 and Level 2 remain fully intact in the codebase as
/// fallback options — flip this one constant to switch back.
/// </summary>
public static class AccDiagnosticLevelSelection
{
    public const AccDiagnosticLevel Current = AccDiagnosticLevel.RealAuthentication;
}
