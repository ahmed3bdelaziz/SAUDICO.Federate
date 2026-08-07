namespace SAUDICO.Federate.Shared;public static class C{public const string Tab="SAUDICO BIM",Panel="Federation",Title="SAUDICO Federate",Temp="SAUDICO-TEMP-NAVIS";}public enum ModelKind{Local,WorksharedLocal,Central,Acc}public enum State{Queued,Opening,Preparing,Exporting,Closing,Succeeded,Failed,Skipped,Cancelled}public enum ParamMode{None,Elements,All}public enum CoordMode{Shared,Internal}public enum DetailMode{Coarse,Medium,Fine}
/// <summary>Core/Shared-local mirror of SAUDICO.Federate.ACC.DataManagement.AccResolutionStatus — kept separate (not referenced directly) so Core/Export stay independent of the ACC project, per the NWC exporter's required independence from APS.</summary>
public enum AccSourceResolutionStatus{CloudModelVerified,UploadedFile,Unresolved}

/// <summary>
/// Single-threaded ambient handoff between a host's UIApplication.DialogBoxShowing
/// subscription (Revit-UI-API-dependent, wired per host) and Export.Nwc, which has
/// no UI-API dependency. Reset before each export, populated only when the verified
/// "no suitable geometry" Navisworks dialog is observed — never for any other dialog.
/// </summary>
public static class DialogWatch
{
    public static string? DialogId { get; private set; }
    public static string? Message { get; private set; }
    public static bool Matched => DialogId != null;

    public static void Reset()
    {
        DialogId = null;
        Message = null;
    }

    public static void Capture(string? dialogId, string? message)
    {
        DialogId = dialogId;
        Message = message;
    }
}

/// <summary>Which Navisworks ExportScope a diagnostic build uses. Compile-time only —
/// flip and rebuild between test A and test B; deliberately not exposed in the UI.</summary>
public enum DiagnosticScopeMode { View, Model }

/// <summary>
/// Temporary A/B diagnostic switch for the "no suitable geometry found" investigation.
/// View (default) reproduces production behavior: ExportScope=View, ViewId=the temporary
/// federation view. Model is a diagnostic-only probe: ExportScope=Model, ViewId=Invalid.
/// Never run both scopes in the same export — one controlled build per test.
/// </summary>
public static class DiagnosticSettings
{
    public const DiagnosticScopeMode ExportScopeMode = DiagnosticScopeMode.View;
}