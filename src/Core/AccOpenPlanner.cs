using System;
using SAUDICO.Federate.Shared;

namespace SAUDICO.Federate.Core;

/// <summary>
/// Everything needed to attempt ModelPathUtils.ConvertCloudGUIDsToCloudPath
/// for a queued ACC job. Deliberately touches no Autodesk.Revit.DB type so
/// this planning step stays unit-testable without a live Revit process.
/// </summary>
public sealed class AccOpenPlan
{
    public string Region { get; init; } = "";
    public Guid ProjectGuid { get; init; }
    public Guid ModelGuid { get; init; }
}

/// <summary>
/// Decides whether a queued ACC job is actually safe to attempt opening —
/// pure validation, no Revit API calls. Only a job whose AccSource is
/// CloudModelVerified AND has parseable ProjectGuid/ModelGuid AND an
/// officially-supported region produces a plan; every other case is
/// refused with a specific reason, never guessed. Used both to gate Start
/// Federation (<see cref="UI.AccQueueHelper"/>) and inside
/// <see cref="Opener.OpenAcc"/> itself as a second, defense-in-depth check.
/// </summary>
public static class AccOpenPlanner
{
    /// <summary>
    /// The regions this Revit version can address, for user-facing messages.
    /// Revit 2025's ModelPathUtils exposes exactly two cloud-region constants
    /// (CloudRegionUS / CloudRegionEMEA) — verified by reflecting over the
    /// installed RevitAPI.dll, not assumed.
    /// </summary>
    internal const string RevitSupportedRegionsText = "supports (US or EMEA)";

    public static bool TryBuildPlan(Job job, out AccOpenPlan? plan, out string? blockReason)
    {
        plan = null;

        if (job.Kind != ModelKind.Acc)
        {
            blockReason = "Not an ACC job.";
            return false;
        }

        AccCloudSource? source = job.AccSource;
        if (source == null)
        {
            blockReason = "The selected ACC model is missing its cloud metadata and cannot be opened.";
            return false;
        }

        if (source.ResolutionStatus != AccSourceResolutionStatus.CloudModelVerified)
        {
            // Not a Revit Cloud Worksharing model. It may still be a plain
            // uploaded RVT, which is exported by downloading a local copy
            // instead — see AccOpenPlanner.RequiresDownload.
            blockReason = "The selected ACC model has not yet been resolved to a Revit cloud model path.";
            return false;
        }

        if (!Guid.TryParse(source.ProjectGuid, out Guid projectGuid) ||
            !Guid.TryParse(source.ModelGuid, out Guid modelGuid))
        {
            blockReason = "The selected ACC model is missing a verified ProjectGuid/ModelGuid and cannot be opened.";
            return false;
        }

        if (!AccRegionMapper.IsSupportedRegion(source.Region))
        {
            // Name the actual value: a blank region means the account context
            // never reached the job (a defect), whereas a real-but-unlisted
            // code means Revit genuinely cannot address that region.
            blockReason = string.IsNullOrWhiteSpace(source.Region)
                ? "The selected ACC model has no Autodesk region recorded, so its cloud path cannot be built. Re-open the ACC browser and re-add the model."
                : $"The selected ACC model's region \"{source.Region}\" is not one Revit {RevitSupportedRegionsText} can open.";
            return false;
        }

        blockReason = null;
        plan = new AccOpenPlan { Region = source.Region!, ProjectGuid = projectGuid, ModelGuid = modelGuid };
        return true;
    }

    /// <summary>
    /// True when this ACC job is not a directly-openable cloud-worksharing
    /// model but does carry the identifiers needed to download a local copy
    /// (a plain uploaded RVT). Downloading is read-only against ACC — if the
    /// version turns out to have no storage object, the download step itself
    /// fails that one job with a clear message rather than guessing here.
    /// </summary>
    /// <summary>
    /// Set to false: the ACC browser only queues Revit Cloud Worksharing
    /// models, so no queued ACC job is ever downloaded. Plain uploaded RVTs
    /// are added through the main window's "Add RVT" file picker instead.
    /// The download implementation (<c>AccUploadedFileDownloader</c>) is kept
    /// intact and unit-tested behind this switch so ACC uploaded-file export
    /// can be re-enabled without rebuilding it from scratch.
    /// </summary>
    public const bool UploadedFileDownloadEnabled = false;

    public static bool RequiresDownload(Job job)
    {
#pragma warning disable CS0162 // Unreachable while UploadedFileDownloadEnabled is false — intentional.
        if (!UploadedFileDownloadEnabled)
        {
            return false;
        }

        if (job.Kind != ModelKind.Acc || job.AccSource == null)
        {
            return false;
        }

        if (TryBuildPlan(job, out _, out _))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(job.AccSource.ProjectId) &&
               !string.IsNullOrWhiteSpace(job.AccSource.VersionId);
#pragma warning restore CS0162
    }

    /// <summary>
    /// True when an ACC job can neither be opened directly as a cloud model
    /// nor downloaded — the only case Start Federation must refuse outright.
    /// </summary>
    public static bool IsBlocked(Job job, out string? blockReason)
    {
        if (job.Kind != ModelKind.Acc)
        {
            blockReason = null;
            return false;
        }

        if (TryBuildPlan(job, out _, out string? planBlockReason))
        {
            blockReason = null;
            return false;
        }

        if (RequiresDownload(job))
        {
            blockReason = null;
            return false;
        }

        // Prefer the specific GUID/region reason when the item genuinely
        // claimed to be a cloud model; otherwise say plainly that this is not
        // a cloud model and point at the supported route for ordinary files.
        blockReason = job.AccSource?.ResolutionStatus == AccSourceResolutionStatus.CloudModelVerified
            ? planBlockReason
            : "This ACC item is not a Revit cloud workshared model. Add ordinary uploaded RVT files with \"Add RVT\" instead.";
        return true;
    }
}
