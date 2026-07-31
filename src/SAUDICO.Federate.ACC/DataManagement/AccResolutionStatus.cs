namespace SAUDICO.Federate.ACC.DataManagement;

/// <summary>
/// How confidently an <see cref="AccBrowseNode"/>/search result has been
/// classified as an actually cloud-openable Revit model. This increment
/// never resolves <c>ProjectGuid</c>/<c>ModelGuid</c> (deferred to the next
/// task), so <see cref="CloudModelVerified"/> is never produced by
/// <see cref="AccCloudModelClassifier"/> today — it exists so the next
/// increment's identifier-resolution step has somewhere to report success
/// without needing another status enum.
/// </summary>
public enum AccResolutionStatus
{
    /// <summary>Revit Cloud Model — ProjectGuid/ModelGuid officially resolved and verified. Not produced by this increment.</summary>
    CloudModelVerified,

    /// <summary>A plain uploaded RVT file — confirmed (by its extension type) not to be a Revit Cloud Model, so it is not cloud-openable through ModelPath.</summary>
    UploadedFile,

    /// <summary>Metadata insufficient to prove either way. The safe default whenever classification cannot be made with confidence — never fabricated.</summary>
    Unresolved,
}
