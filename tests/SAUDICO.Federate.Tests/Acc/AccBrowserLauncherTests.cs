using System;
using SAUDICO.Federate.ACC.Authentication;
using SAUDICO.Federate.UI;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

public sealed class AccBrowserLauncherTests
{
    [Fact]
    public void HappyPath_ComposesAndShowsWindow_ReturnsTrue()
    {
        bool windowCreated = false;

        bool result = AccBrowserLauncher.TryLaunch(
            composeAuthentication: () => new FakeAuthenticationServiceForLauncher(),
            createAndShowWindow: _ => { windowCreated = true; },
            showSafeMessage: _ => Assert.Fail("Safe message should not be shown on the happy path."));

        Assert.True(result);
        Assert.True(windowCreated);
    }

    [Fact]
    public void ComposeAuthenticationThrows_IsCaught_SafeMessageShown_NoExceptionEscapes()
    {
        string? shownMessage = null;

        bool result = AccBrowserLauncher.TryLaunch(
            composeAuthentication: () => throw new InvalidOperationException("simulated malformed configuration"),
            createAndShowWindow: _ => Assert.Fail("Window construction should not be reached."),
            showSafeMessage: message => shownMessage = message);

        Assert.False(result);
        Assert.Equal(AccBrowserLauncher.SafeFailureMessage, shownMessage);
    }

    [Fact]
    public void WindowConstructionThrows_IsCaught_SafeMessageShown_NoExceptionEscapes()
    {
        string? shownMessage = null;

        bool result = AccBrowserLauncher.TryLaunch(
            composeAuthentication: () => new FakeAuthenticationServiceForLauncher(),
            createAndShowWindow: _ => throw new Exception("simulated XAML/window construction failure"),
            showSafeMessage: message => shownMessage = message);

        Assert.False(result);
        Assert.Equal(AccBrowserLauncher.SafeFailureMessage, shownMessage);
    }

    [Fact]
    public void SafeMessage_NeverContainsExceptionDetailOrSensitiveValues()
    {
        string? shownMessage = null;

        AccBrowserLauncher.TryLaunch(
            composeAuthentication: () => throw new InvalidOperationException("clientId=FAKE-SECRET-CLIENT-ID-MARKER"),
            createAndShowWindow: _ => { },
            showSafeMessage: message => shownMessage = message);

        Assert.DoesNotContain("FAKE-SECRET-CLIENT-ID-MARKER", shownMessage);
        Assert.DoesNotContain("clientId", shownMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnosticsDescriptorThatItselfThrows_DoesNotEscapeOrBreakLogging()
    {
        bool result = AccBrowserLauncher.TryLaunch(
            composeAuthentication: () => throw new InvalidOperationException("simulated failure"),
            createAndShowWindow: _ => { },
            showSafeMessage: _ => { },
            describeSafeDiagnostics: () => throw new Exception("diagnostics gathering itself failed"));

        Assert.False(result);
    }

    private sealed class FakeAuthenticationServiceForLauncher : IApsAuthenticationService
    {
        public bool IsAuthenticated => false;
        public ApsAuthenticationState State => ApsAuthenticationState.SignedOut;
        public SAUDICO.Federate.ACC.Profile.ApsUserProfile? CurrentUser => null;
        public event EventHandler<ApsAuthenticationStateChangedEventArgs>? AuthenticationStateChanged { add { } remove { } }

        public System.Threading.Tasks.Task<SAUDICO.Federate.ACC.Profile.ApsUserProfile> SignInAsync(System.Threading.CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public System.Threading.Tasks.Task<string> GetValidAccessTokenAsync(System.Threading.CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public System.Threading.Tasks.Task<string> RefreshAccessTokenAsync(System.Threading.CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public System.Threading.Tasks.Task SignOutAsync(System.Threading.CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
