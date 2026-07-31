using SAUDICO.Federate.UI;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

/// <summary>
/// Proves the fix for the "Hubs list stays empty" lifecycle defect: the
/// browser must start whether sign-in was already true at construction
/// time (no PropertyChanged event ever fires in that case) or arrives via
/// a later PropertyChanged("IsSignedIn") transition, and must ignore every
/// unrelated notification.
/// </summary>
public sealed class AccBrowseAutoStartTests
{
    [Fact]
    public void EvaluateInitialState_AlreadySignedIn_CallsStart()
    {
        bool startCalled = false;

        AccBrowseAutoStart.EvaluateInitialState(isSignedInNow: true, start: () => startCalled = true);

        Assert.True(startCalled);
    }

    [Fact]
    public void EvaluateInitialState_NotSignedIn_DoesNotCallStart()
    {
        bool startCalled = false;

        AccBrowseAutoStart.EvaluateInitialState(isSignedInNow: false, start: () => startCalled = true);

        Assert.False(startCalled);
    }

    [Fact]
    public void HandlePropertyChanged_IsSignedInTrue_CallsStartOnly()
    {
        bool startCalled = false;
        bool resetCalled = false;

        AccBrowseAutoStart.HandlePropertyChanged(
            propertyName: nameof(AuthViewModel.IsSignedIn),
            isSignedInNow: true,
            start: () => startCalled = true,
            reset: () => resetCalled = true);

        Assert.True(startCalled);
        Assert.False(resetCalled);
    }

    [Fact]
    public void HandlePropertyChanged_IsSignedInFalse_CallsResetOnly()
    {
        bool startCalled = false;
        bool resetCalled = false;

        AccBrowseAutoStart.HandlePropertyChanged(
            propertyName: nameof(AuthViewModel.IsSignedIn),
            isSignedInNow: false,
            start: () => startCalled = true,
            reset: () => resetCalled = true);

        Assert.False(startCalled);
        Assert.True(resetCalled);
    }

    [Theory]
    [InlineData("DisplayName")]
    [InlineData("ErrorMessage")]
    [InlineData("State")]
    [InlineData(null)]
    public void HandlePropertyChanged_UnrelatedProperty_CallsNeitherStartNorReset(string? propertyName)
    {
        bool startCalled = false;
        bool resetCalled = false;

        AccBrowseAutoStart.HandlePropertyChanged(
            propertyName: propertyName,
            isSignedInNow: true,
            start: () => startCalled = true,
            reset: () => resetCalled = true);

        Assert.False(startCalled);
        Assert.False(resetCalled);
    }
}
