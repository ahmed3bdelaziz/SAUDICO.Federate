using System;
using System.Collections.Generic;
using System.Linq;
using SAUDICO.Federate.Core;
using SAUDICO.Federate.Shared;

namespace SAUDICO.Federate.UI;

/// <summary>
/// The federation-queue rules specific to ACC-sourced jobs, extracted out
/// of <see cref="Manager"/>'s code-behind so they are directly
/// unit-testable without constructing a real WPF window — the same
/// pattern already used for <see cref="AccBrowserLevelRouter"/>,
/// <see cref="SingletonWindowSlot{T}"/>, and <see cref="AccBrowseAutoStart"/>.
/// </summary>
public static class AccQueueHelper
{
    /// <summary>True if an ACC job with the same stable ItemId (never display name) is already queued.</summary>
    public static bool IsDuplicate(IEnumerable<Job> jobs, string itemId) =>
        jobs.Any(j => j.Kind == ModelKind.Acc && j.AccSource?.ItemId == itemId);

    /// <summary>
    /// True if any queued ACC job has not been verified as an actually
    /// cloud-openable Revit model. Used only to pick which of the two
    /// required Start-Federation messages to show — see
    /// <see cref="HasAnyAccJob"/> for what actually gates Start Federation
    /// today.
    /// </summary>
    public static bool HasUnresolvedAccJob(IEnumerable<Job> jobs) =>
        jobs.Any(j => j.Kind == ModelKind.Acc && j.AccSource?.ResolutionStatus != AccSourceResolutionStatus.CloudModelVerified);

    /// <summary>
    /// True if the queue contains any ACC job at all, resolved or not. Kept
    /// for compatibility/diagnostics — no longer what gates Start
    /// Federation on its own (see <see cref="HasBlockedAccJob"/>): a
    /// verified, openable ACC job is now allowed to run.
    /// </summary>
    public static bool HasAnyAccJob(IEnumerable<Job> jobs) =>
        jobs.Any(j => j.Kind == ModelKind.Acc);

    /// <summary>
    /// True if the queue contains an ACC job that can neither be opened as a
    /// Revit Cloud Worksharing model nor downloaded as a plain uploaded file.
    /// Start Federation must refuse to run while this is true.
    /// </summary>
    public static bool HasBlockedAccJob(IEnumerable<Job> jobs) =>
        jobs.Any(j => AccOpenPlanner.IsBlocked(j, out _));

    /// <summary>The first blocked ACC job's specific reason, or null when nothing is blocked.</summary>
    public static string? FirstBlockReason(IEnumerable<Job> jobs)
    {
        foreach (Job job in jobs)
        {
            if (AccOpenPlanner.IsBlocked(job, out string? reason))
            {
                return reason;
            }
        }

        return null;
    }

    /// <summary>Queued ACC jobs that must be downloaded to a local copy before they can be exported.</summary>
    public static IReadOnlyList<Job> JobsRequiringDownload(IEnumerable<Job> jobs) =>
        jobs.Where(AccOpenPlanner.RequiresDownload).ToList();

    /// <summary>True if a queued, CloudModelVerified ACC job is missing a parseable ProjectGuid/ModelGuid.</summary>
    public static bool HasAccJobWithMissingGuid(IEnumerable<Job> jobs) =>
        jobs.Any(j => j.Kind == ModelKind.Acc &&
            j.AccSource?.ResolutionStatus == AccSourceResolutionStatus.CloudModelVerified &&
            (!Guid.TryParse(j.AccSource.ProjectGuid, out _) || !Guid.TryParse(j.AccSource.ModelGuid, out _)));

    /// <summary>True if a queued, CloudModelVerified ACC job (with valid GUIDs) has a region Revit's cloud-open API does not support.</summary>
    public static bool HasAccJobWithUnsupportedRegion(IEnumerable<Job> jobs) =>
        jobs.Any(j => j.Kind == ModelKind.Acc &&
            j.AccSource?.ResolutionStatus == AccSourceResolutionStatus.CloudModelVerified &&
            !AccRegionMapper.IsSupportedRegion(j.AccSource.Region));
}
