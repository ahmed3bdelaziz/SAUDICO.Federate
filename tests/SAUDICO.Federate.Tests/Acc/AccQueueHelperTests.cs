using System.Collections.Generic;
using SAUDICO.Federate.Core;
using SAUDICO.Federate.Shared;
using SAUDICO.Federate.UI;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

/// <summary>
/// The federation-queue rules for ACC jobs — extracted into
/// <see cref="AccQueueHelper"/> specifically so they're testable without a
/// real WPF Manager window (same pattern as AccBrowserLevelRouter etc.).
/// Manager.AddAccModelsToQueue/Run call this exact helper, not a
/// reimplementation, so these tests cover the real production logic.
/// </summary>
public sealed class AccQueueHelperTests
{
    private static Job AccJob(string itemId, AccSourceResolutionStatus status = AccSourceResolutionStatus.Unresolved) => new Job
    {
        Source = "Model.rvt",
        Kind = ModelKind.Acc,
        AccSource = new AccCloudSource { ItemId = itemId, ResolutionStatus = status },
    };

    [Fact]
    public void IsDuplicate_SameItemIdAlreadyQueued_ReturnsTrue()
    {
        List<Job> jobs = new() { AccJob("item-1") };

        Assert.True(AccQueueHelper.IsDuplicate(jobs, "item-1"));
    }

    [Fact]
    public void IsDuplicate_DifferentItemId_ReturnsFalse()
    {
        List<Job> jobs = new() { AccJob("item-1") };

        Assert.False(AccQueueHelper.IsDuplicate(jobs, "item-2"));
    }

    [Fact]
    public void IsDuplicate_MatchesByItemId_NotDisplayName()
    {
        // Two jobs could share a display name (Model.rvt) but must only be
        // treated as duplicates when their stable ItemId matches.
        List<Job> jobs = new() { AccJob("item-1") };

        Assert.False(AccQueueHelper.IsDuplicate(jobs, "item-2"));
        Assert.True(AccQueueHelper.IsDuplicate(jobs, "item-1"));
    }

    [Fact]
    public void HasUnresolvedAccJob_UnresolvedAccJobPresent_ReturnsTrue()
    {
        List<Job> jobs = new() { AccJob("item-1", AccSourceResolutionStatus.Unresolved) };

        Assert.True(AccQueueHelper.HasUnresolvedAccJob(jobs));
    }

    [Fact]
    public void HasUnresolvedAccJob_UploadedFileStatus_StillCountsAsUnresolvedForFederation()
    {
        List<Job> jobs = new() { AccJob("item-1", AccSourceResolutionStatus.UploadedFile) };

        Assert.True(AccQueueHelper.HasUnresolvedAccJob(jobs));
    }

    [Fact]
    public void HasUnresolvedAccJob_CloudModelVerified_ReturnsFalse()
    {
        List<Job> jobs = new() { AccJob("item-1", AccSourceResolutionStatus.CloudModelVerified) };

        Assert.False(AccQueueHelper.HasUnresolvedAccJob(jobs));
    }

    [Fact]
    public void HasUnresolvedAccJob_OnlyLocalAndCentralJobs_ReturnsFalse()
    {
        List<Job> jobs = new()
        {
            new Job { Source = @"C:\models\a.rvt", Kind = ModelKind.Local },
            new Job { Source = @"C:\models\b.rvt", Kind = ModelKind.Central },
        };

        Assert.False(AccQueueHelper.HasUnresolvedAccJob(jobs));
        Assert.False(AccQueueHelper.IsDuplicate(jobs, "anything"));
    }

    [Fact]
    public void HasAnyAccJob_UnresolvedAccJob_ReturnsTrue()
    {
        List<Job> jobs = new() { AccJob("item-1", AccSourceResolutionStatus.Unresolved) };

        Assert.True(AccQueueHelper.HasAnyAccJob(jobs));
    }

    [Fact]
    public void HasAnyAccJob_CloudModelVerifiedJob_StillReturnsTrue()
    {
        // HasAnyAccJob just means "any ACC job at all" — it no longer gates
        // Start Federation by itself now that a CloudModelVerified job with
        // valid GUIDs/region is openable. See HasBlockedAccJob below for
        // the actual gate.
        List<Job> jobs = new() { AccJob("item-1", AccSourceResolutionStatus.CloudModelVerified) };

        Assert.True(AccQueueHelper.HasAnyAccJob(jobs));
    }

    private static Job VerifiedAccJob(string projectGuid, string modelGuid, string region) => new Job
    {
        Source = "Model.rvt",
        Kind = ModelKind.Acc,
        AccSource = new AccCloudSource
        {
            ItemId = "item-1",
            ResolutionStatus = AccSourceResolutionStatus.CloudModelVerified,
            ProjectGuid = projectGuid,
            ModelGuid = modelGuid,
            Region = region,
        },
    };

    private static Job DownloadableAccJob(AccSourceResolutionStatus status) => new Job
    {
        Source = "Model.rvt",
        Kind = ModelKind.Acc,
        AccSource = new AccCloudSource
        {
            ItemId = "item-1",
            ResolutionStatus = status,
            ProjectId = "b.proj-1",
            VersionId = "urn:adsk.wipprod:fs.file:vf.abc?version=1",
        },
    };

    [Fact]
    public void HasBlockedAccJob_UnresolvedJobWithoutDownloadIds_ReturnsTrue()
    {
        // No ProjectId/VersionId — it can neither be opened by GUID nor downloaded.
        List<Job> jobs = new() { AccJob("item-1", AccSourceResolutionStatus.Unresolved) };

        Assert.True(AccQueueHelper.HasBlockedAccJob(jobs));
    }

    [Theory]
    [InlineData(AccSourceResolutionStatus.UploadedFile)]
    [InlineData(AccSourceResolutionStatus.Unresolved)]
    public void HasBlockedAccJob_NonCloudWorksharedJob_IsBlocked_AndPointsAtAddRvt(AccSourceResolutionStatus status)
    {
        // The ACC browser only queues cloud-workshared models; ordinary
        // uploaded RVTs go through "Add RVT" instead, so even a job carrying
        // full download identifiers must be refused here.
        List<Job> jobs = new() { DownloadableAccJob(status) };

        Assert.True(AccQueueHelper.HasBlockedAccJob(jobs));
        Assert.Empty(AccQueueHelper.JobsRequiringDownload(jobs));
        Assert.Contains("Add RVT", AccQueueHelper.FirstBlockReason(jobs));
    }

    [Fact]
    public void RequiresDownload_IsDisabled_SoNoAccJobIsEverDownloaded()
    {
        Assert.False(AccOpenPlanner.UploadedFileDownloadEnabled);
        Assert.False(AccOpenPlanner.RequiresDownload(DownloadableAccJob(AccSourceResolutionStatus.UploadedFile)));
    }

    [Fact]
    public void JobsRequiringDownload_CloudWorksharedJob_IsNotDownloaded()
    {
        // A genuine cloud-worksharing model is opened by GUID, never downloaded.
        List<Job> jobs = new() { VerifiedAccJob(System.Guid.NewGuid().ToString(), System.Guid.NewGuid().ToString(), "US") };

        Assert.Empty(AccQueueHelper.JobsRequiringDownload(jobs));
    }

    [Fact]
    public void JobsRequiringDownload_LocalAndCentralJobs_AreNeverDownloaded()
    {
        List<Job> jobs = new()
        {
            new Job { Source = @"C:\models\a.rvt", Kind = ModelKind.Local },
            new Job { Source = @"C:\models\b.rvt", Kind = ModelKind.Central },
        };

        Assert.Empty(AccQueueHelper.JobsRequiringDownload(jobs));
    }

    [Fact]
    public void HasBlockedAccJob_VerifiedJobWithValidGuidsAndRegion_ReturnsFalse()
    {
        List<Job> jobs = new() { VerifiedAccJob(System.Guid.NewGuid().ToString(), System.Guid.NewGuid().ToString(), "US") };

        Assert.False(AccQueueHelper.HasBlockedAccJob(jobs));
    }

    [Fact]
    public void HasBlockedAccJob_VerifiedJobWithMissingGuidAndNoDownloadIds_ReturnsTrue()
    {
        List<Job> jobs = new() { VerifiedAccJob("", System.Guid.NewGuid().ToString(), "US") };

        Assert.True(AccQueueHelper.HasBlockedAccJob(jobs));
        Assert.True(AccQueueHelper.HasAccJobWithMissingGuid(jobs));
        Assert.False(AccQueueHelper.HasAccJobWithUnsupportedRegion(jobs));
        Assert.Contains("ProjectGuid", AccQueueHelper.FirstBlockReason(jobs));
    }

    [Fact]
    public void HasBlockedAccJob_VerifiedJobWithMalformedGuid_ReturnsTrue()
    {
        List<Job> jobs = new() { VerifiedAccJob("not-a-guid", System.Guid.NewGuid().ToString(), "US") };

        Assert.True(AccQueueHelper.HasBlockedAccJob(jobs));
        Assert.True(AccQueueHelper.HasAccJobWithMissingGuid(jobs));
    }

    [Fact]
    public void HasBlockedAccJob_VerifiedJobWithUnsupportedRegion_ReturnsTrue()
    {
        List<Job> jobs = new() { VerifiedAccJob(System.Guid.NewGuid().ToString(), System.Guid.NewGuid().ToString(), "AUS") };

        Assert.True(AccQueueHelper.HasBlockedAccJob(jobs));
        Assert.False(AccQueueHelper.HasAccJobWithMissingGuid(jobs));
        Assert.True(AccQueueHelper.HasAccJobWithUnsupportedRegion(jobs));
        Assert.Contains("region", AccQueueHelper.FirstBlockReason(jobs));
    }

    [Fact]
    public void HasBlockedAccJob_OnlyLocalAndCentralJobs_ReturnsFalse()
    {
        List<Job> jobs = new()
        {
            new Job { Source = @"C:\models\a.rvt", Kind = ModelKind.Local },
            new Job { Source = @"C:\models\b.rvt", Kind = ModelKind.Central },
        };

        Assert.False(AccQueueHelper.HasBlockedAccJob(jobs));
    }

    [Fact]
    public void HasAnyAccJob_OnlyLocalAndCentralJobs_ReturnsFalse()
    {
        List<Job> jobs = new()
        {
            new Job { Source = @"C:\models\a.rvt", Kind = ModelKind.Local },
            new Job { Source = @"C:\models\b.rvt", Kind = ModelKind.Central },
        };

        Assert.False(AccQueueHelper.HasAnyAccJob(jobs));
    }

    [Fact]
    public void HasAnyAccJob_EmptyQueue_ReturnsFalse()
    {
        Assert.False(AccQueueHelper.HasAnyAccJob(new List<Job>()));
    }

    [Fact]
    public void LocalAndCentralJobs_AccSourceStaysNull_UnaffectedByAccFieldsAddedToJob()
    {
        Job local = new Job { Source = @"C:\models\a.rvt", Kind = ModelKind.Local };
        Job central = new Job { Source = @"C:\models\b.rvt", Kind = ModelKind.Central };

        Assert.Null(local.AccSource);
        Assert.Null(central.AccSource);
        Assert.Equal("a.rvt", local.Name);
        Assert.Equal("b.rvt", central.Name);
    }

    [Fact]
    public void AccJob_NameDerivesFromDisplayNameSourceLikeLocalJobsDeriveFromPath()
    {
        Job job = AccJob("item-1");
        job.Source = "Architectural Model.rvt";

        Assert.Equal("Architectural Model.rvt", job.Name);
    }

    [Fact]
    public void AccCloudSource_RetainsRegionAndAllRequiredQueueFields()
    {
        AccCloudSource source = new AccCloudSource
        {
            Region = "EMEA",
            HubId = "hub-1",
            HubName = "Acme Hub",
            ProjectId = "proj-1",
            ProjectName = "Project X",
            FolderId = "folder-1",
            FolderPath = "/Project Files/Sub",
            ItemId = "item-1",
            VersionId = "ver-1",
            VersionNumber = 3,
            ResolutionStatus = AccSourceResolutionStatus.Unresolved,
            ResolutionMessage = "The selected ACC model has not yet been resolved to a Revit cloud model path.",
        };

        Assert.Equal("EMEA", source.Region);
        Assert.Equal("Acme Hub", source.HubName);
        Assert.Equal("Project X", source.ProjectName);
        Assert.Equal("/Project Files/Sub", source.FolderPath);
        Assert.Null(source.ProjectGuid);
        Assert.Null(source.ModelGuid);
    }
}
