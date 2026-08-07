namespace SAUDICO.Federate.Core;

/// <summary>Pure domain policy for opening a source document; contains no Revit API types.</summary>
public sealed class OpenPlan
{
    public SourceKind SourceKind { get; init; }
    public DetachStrategy DetachStrategy { get; init; }
    public WorksetStrategy WorksetStrategy { get; init; }
    public bool VerifyIdentityAfterOpen { get; init; }

    public static OpenPlan ForLocalFile() => new()
    {
        SourceKind = SourceKind.LocalFile,
        DetachStrategy = DetachStrategy.None,
        WorksetStrategy = WorksetStrategy.Default
    };

    public static OpenPlan ForFileCentral() => new()
    {
        SourceKind = SourceKind.FileCentral,
        DetachStrategy = DetachStrategy.DetachAndPreserveWorksets,
        WorksetStrategy = WorksetStrategy.OpenAll
    };

    public static OpenPlan ForAccCloud() => new()
    {
        SourceKind = SourceKind.AccCloudModel,
        DetachStrategy = DetachStrategy.DoNotDetach,
        WorksetStrategy = WorksetStrategy.OpenAll,
        VerifyIdentityAfterOpen = true
    };
}

public enum DetachStrategy { None, DetachAndPreserveWorksets, DoNotDetach }
public enum WorksetStrategy { Default, OpenAll, Custom }
