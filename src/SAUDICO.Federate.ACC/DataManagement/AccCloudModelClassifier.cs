using System;

namespace SAUDICO.Federate.ACC.DataManagement;

/// <summary>
/// Classifies an RVT search result's <see cref="AccResolutionStatus"/> —
/// never from its file name, and never by assuming every ".rvt" item is a
/// Revit Cloud Model.
///
/// <see cref="AccResolutionStatus.CloudModelVerified"/> requires ALL of:
/// the tip version's <c>attributes.extension.type</c> equals the official
/// Revit Cloud Model marker <c>versions:autodesk.bim360:C4RModel</c>
/// (verified against the official Autodesk Platform Services blog post
/// "Accessing BIM 360 Design models on Revit"), AND both
/// <c>attributes.extension.data.projectGuid</c> and <c>.modelGuid</c> are
/// present and parse as valid GUIDs (<see cref="Guid.TryParse(string, out Guid)"/>).
/// Any one of those being missing or malformed — including a matching
/// extension type with an absent/garbled GUID — falls back to
/// <see cref="AccResolutionStatus.Unresolved"/> rather than guess. That is
/// always the safe direction: both <see cref="AccResolutionStatus.UploadedFile"/>
/// and <see cref="AccResolutionStatus.Unresolved"/> equally block the item
/// from federation; only fabricating <see cref="AccResolutionStatus.CloudModelVerified"/>
/// without real proof would be unsafe, and this classifier structurally
/// cannot do that.
/// </summary>
public static class AccCloudModelClassifier
{
    public const string RevitCloudModelExtensionType = "versions:autodesk.bim360:C4RModel";

    private static readonly string[] KnownPlainFileExtensionTypes =
    {
        "items:autodesk.bim360:File",
    };

    public static AccResolutionStatus Classify(string? extensionType, string? rawProjectGuid, string? rawModelGuid)
    {
        if (string.Equals(extensionType, RevitCloudModelExtensionType, StringComparison.Ordinal) &&
            Guid.TryParse(rawProjectGuid, out _) &&
            Guid.TryParse(rawModelGuid, out _))
        {
            return AccResolutionStatus.CloudModelVerified;
        }

        if (extensionType != null && Array.IndexOf(KnownPlainFileExtensionTypes, extensionType) >= 0)
        {
            return AccResolutionStatus.UploadedFile;
        }

        return AccResolutionStatus.Unresolved;
    }
}
