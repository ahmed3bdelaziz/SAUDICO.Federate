using System;
using SAUDICO.Federate.Core;
using SAUDICO.Federate.Shared;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

/// <summary>
/// AccOpenPlanner is the pure (no Autodesk.Revit.DB object construction)
/// gate Opener.OpenAcc uses before it ever touches the Revit API — every
/// case here is exactly what determines whether a queued ACC job actually
/// reaches the cloud-open call. Deliberately never constructs a Document/
/// Application/OpenOptions (there is no live Revit process in this test
/// host) — see AccRegionMapperTests/AccOpenerPolicyTests for the same
/// reasoning applied to the region mapping and open-option policy.
/// </summary>
public sealed class AccOpenPlannerTests
{
    private static Job Job(ModelKind kind, AccCloudSource? source) => new Job
    {
        Source = "Model.rvt",
        Kind = kind,
        AccSource = source,
    };

    private static AccCloudSource VerifiedSource(string projectGuid, string modelGuid, string region) => new AccCloudSource
    {
        ItemId = "item-1",
        ResolutionStatus = AccSourceResolutionStatus.CloudModelVerified,
        ProjectGuid = projectGuid,
        ModelGuid = modelGuid,
        Region = region,
    };

    [Fact]
    public void TryBuildPlan_VerifiedJobWithValidGuidsAndRegion_ReachesCloudOpener()
    {
        Guid projectGuid = Guid.NewGuid();
        Guid modelGuid = Guid.NewGuid();
        Job job = Job(ModelKind.Acc, VerifiedSource(projectGuid.ToString(), modelGuid.ToString(), "US"));

        bool ok = AccOpenPlanner.TryBuildPlan(job, out AccOpenPlan? plan, out string? blockReason);

        Assert.True(ok);
        Assert.Null(blockReason);
        Assert.NotNull(plan);
        Assert.Equal(projectGuid, plan!.ProjectGuid);
        Assert.Equal(modelGuid, plan.ModelGuid);
        Assert.Equal("US", plan.Region);
    }

    [Fact]
    public void TryBuildPlan_EmeaRegion_Verified()
    {
        Job job = Job(ModelKind.Acc, VerifiedSource(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "EMEA"));

        Assert.True(AccOpenPlanner.TryBuildPlan(job, out AccOpenPlan? plan, out _));
        Assert.Equal("EMEA", plan!.Region);
    }

    [Fact]
    public void TryBuildPlan_NotAnAccJob_IsBlocked()
    {
        Job job = new Job { Source = @"C:\models\a.rvt", Kind = ModelKind.Local };

        Assert.False(AccOpenPlanner.TryBuildPlan(job, out AccOpenPlan? plan, out string? blockReason));
        Assert.Null(plan);
        Assert.NotNull(blockReason);
    }

    [Theory]
    [InlineData(AccSourceResolutionStatus.Unresolved)]
    [InlineData(AccSourceResolutionStatus.UploadedFile)]
    public void TryBuildPlan_NotCloudModelVerified_IsBlockedWithOriginalMessage(AccSourceResolutionStatus status)
    {
        Job job = Job(ModelKind.Acc, new AccCloudSource { ItemId = "item-1", ResolutionStatus = status });

        Assert.False(AccOpenPlanner.TryBuildPlan(job, out AccOpenPlan? plan, out string? blockReason));
        Assert.Null(plan);
        Assert.Equal("The selected ACC model has not yet been resolved to a Revit cloud model path.", blockReason);
    }

    [Fact]
    public void TryBuildPlan_NoAccSourceAtAll_IsBlocked()
    {
        Job job = Job(ModelKind.Acc, null);

        Assert.False(AccOpenPlanner.TryBuildPlan(job, out AccOpenPlan? plan, out string? blockReason));
        Assert.Null(plan);
        Assert.NotNull(blockReason);
    }

    [Fact]
    public void TryBuildPlan_VerifiedButMissingProjectGuid_IsBlocked()
    {
        Job job = Job(ModelKind.Acc, VerifiedSource("", Guid.NewGuid().ToString(), "US"));

        Assert.False(AccOpenPlanner.TryBuildPlan(job, out AccOpenPlan? plan, out string? blockReason));
        Assert.Null(plan);
        Assert.Contains("ProjectGuid", blockReason);
    }

    [Fact]
    public void TryBuildPlan_VerifiedButMalformedModelGuid_IsBlocked()
    {
        Job job = Job(ModelKind.Acc, VerifiedSource(Guid.NewGuid().ToString(), "not-a-guid", "US"));

        Assert.False(AccOpenPlanner.TryBuildPlan(job, out AccOpenPlan? plan, out string? blockReason));
        Assert.Null(plan);
    }

    [Theory]
    [InlineData("AUS")]
    [InlineData("APAC")]
    [InlineData("")]
    [InlineData(null)]
    public void TryBuildPlan_UnsupportedOrUnknownRegion_IsBlocked(string? region)
    {
        Job job = Job(ModelKind.Acc, VerifiedSource(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), region!));

        Assert.False(AccOpenPlanner.TryBuildPlan(job, out AccOpenPlan? plan, out string? blockReason));
        Assert.Null(plan);
        Assert.Contains("region", blockReason);
    }
}

/// <summary>
/// AccRegionMapper.IsSupportedRegion is pure string comparison against the
/// two officially documented Revit API cloud-region values — never guesses
/// at newer APS platform region codes this session could not verify
/// against an equivalent Revit-API-accepted literal.
/// </summary>
public sealed class AccRegionMapperTests
{
    [Theory]
    [InlineData("US")]
    [InlineData("EMEA")]
    public void IsSupportedRegion_OfficialValues_ReturnsTrue(string region)
    {
        Assert.True(AccRegionMapper.IsSupportedRegion(region));
    }

    [Theory]
    [InlineData("AUS")]
    [InlineData("CAN")]
    [InlineData("DEU")]
    [InlineData("GBR")]
    [InlineData("IND")]
    [InlineData("JPN")]
    [InlineData("APAC")]
    [InlineData("us")]
    [InlineData("emea")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSupportedRegion_AnyOtherValue_ReturnsFalse(string? region)
    {
        Assert.False(AccRegionMapper.IsSupportedRegion(region));
    }
}

// Note: Opener.AccDetachOption/AccWorksetOption (DetachFromCentralOption.DoNotDetach
// and WorksetConfigurationOption.OpenAllWorksets) are not asserted by a unit test here.
// RevitAPI.dll is reference-only (Private=false, matching every other project in this
// solution) and is not physically present in the test output directory; even a bare
// enum-value comparison forces the CLR to load the assembly and fails with
// FileNotFoundException ("The specified module could not be found") outside a live
// Revit process, because RevitAPI.dll itself depends on other native Revit modules
// that are never copied alongside it. This is a pre-existing, solution-wide constraint
// (no test in this project has ever touched an Autodesk.Revit.DB type) — the two
// constants are instead verified by direct code reading and by the successful build.
