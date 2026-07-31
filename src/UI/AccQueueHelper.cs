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
    /// True if the queue contains any ACC job at all, resolved or not.
    /// Start Federation must refuse to run while this is true: identifier
    /// resolution (<see cref="AccSourceResolutionStatus.CloudModelVerified"/>)
    /// is implemented, but actually opening/exporting an ACC-sourced model
    /// is a separate, not-yet-implemented step — <c>Job.Source</c> for an
    /// ACC job is a display name, not a real file path, so letting one
    /// reach <c>Engine.Run</c>/<c>Opener.Open</c> today would fail unsafely
    /// rather than export anything.
    /// </summary>
    public static bool HasAnyAccJob(IEnumerable<Job> jobs) =>
        jobs.Any(j => j.Kind == ModelKind.Acc);
}
