using System;
using SAUDICO.Federate.UI;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

/// <summary>
/// Proves the "Add ACC Models" routing decision: diagnostic Levels 1 and 2
/// never reach the real-authentication branch, so no authentication,
/// configuration, or token service is ever composed at those levels — the
/// only place <c>ComposeAuthenticationService</c>/<c>AccBrowserLauncher</c>
/// is invoked in production code is inside the real-authentication delegate.
/// </summary>
public sealed class AccBrowserLevelRouterTests
{
    [Fact]
    public void Level1_OnlyOpensLevel1Shell_NeverTouchesLevel2OrAuthentication()
    {
        bool level1Called = false;

        AccBrowserLevelRouter.Route(
            AccDiagnosticLevel.Level1Shell,
            openLevel1Shell: () => level1Called = true,
            openLevel2NoAuthBrowser: () => Assert.Fail("Level 2 must not be reached from Level 1."),
            openRealAuthenticatedBrowser: () => Assert.Fail("Authentication must not be composed from Level 1."));

        Assert.True(level1Called);
    }

    [Fact]
    public void Level2_OnlyOpensLevel2NoAuthBrowser_NeverComposesAuthentication()
    {
        bool level2Called = false;

        AccBrowserLevelRouter.Route(
            AccDiagnosticLevel.Level2AccBrowserNoAuth,
            openLevel1Shell: () => Assert.Fail("Level 1 must not be reached from Level 2."),
            openLevel2NoAuthBrowser: () => level2Called = true,
            openRealAuthenticatedBrowser: () => Assert.Fail("Authentication must not be composed from Level 2."));

        Assert.True(level2Called);
    }

    [Fact]
    public void RealAuthentication_OnlyOpensRealAuthenticatedBrowser()
    {
        bool realCalled = false;

        AccBrowserLevelRouter.Route(
            AccDiagnosticLevel.RealAuthentication,
            openLevel1Shell: () => Assert.Fail("Level 1 must not be reached from RealAuthentication."),
            openLevel2NoAuthBrowser: () => Assert.Fail("Level 2 must not be reached from RealAuthentication."),
            openRealAuthenticatedBrowser: () => realCalled = true);

        Assert.True(realCalled);
    }

    [Fact]
    public void UnknownLevel_ThrowsRatherThanSilentlyFallingBackToRealAuthentication()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AccBrowserLevelRouter.Route(
                (AccDiagnosticLevel)999,
                openLevel1Shell: () => { },
                openLevel2NoAuthBrowser: () => { },
                openRealAuthenticatedBrowser: () => Assert.Fail("An unrecognized level must not silently compose authentication.")));
    }

    [Fact]
    public void CurrentSelection_IsRealAuthentication_NowThatLevels1And2Passed()
    {
        // Documents the active selection so any future flip back to a
        // diagnostic level is a deliberate, visible test change.
        Assert.Equal(AccDiagnosticLevel.RealAuthentication, AccDiagnosticLevelSelection.Current);
    }
}
