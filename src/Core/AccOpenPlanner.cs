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
    public static bool TryBuildPlan(Job job, out AccOpenPlan? plan, out string? blockReason)
    {
        plan = null;

        if (job.Kind != ModelKind.Acc)
        {
            blockReason = "Not an ACC job.";
            return false;
        }

        AccCloudSource? source = job.AccSource;
        if (source == null || source.ResolutionStatus != AccSourceResolutionStatus.CloudModelVerified)
        {
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
            blockReason = "The selected ACC model's region is not a supported Revit cloud region and cannot be opened.";
            return false;
        }

        blockReason = null;
        plan = new AccOpenPlan { Region = source.Region!, ProjectGuid = projectGuid, ModelGuid = modelGuid };
        return true;
    }
}
