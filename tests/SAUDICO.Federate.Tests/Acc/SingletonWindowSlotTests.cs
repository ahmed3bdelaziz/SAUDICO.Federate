using SAUDICO.Federate.UI;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

/// <summary>
/// Proves the reuse guarantee behind "Add ACC Models": a second click while
/// the window is open activates the existing instance, and a click after
/// the window closed creates (and does not try to reuse/activate) a fresh
/// instance instead of the closed one.
/// </summary>
public sealed class SingletonWindowSlotTests
{
    private sealed class FakeWindow
    {
        public bool Activated { get; private set; }
        public void Activate() => Activated = true;
    }

    [Fact]
    public void TryActivateExisting_ReturnsFalse_WhenNothingTracked()
    {
        SingletonWindowSlot<FakeWindow> slot = new();

        bool activated = slot.TryActivateExisting(w => w.Activate());

        Assert.False(activated);
    }

    [Fact]
    public void TryActivateExisting_ActivatesTrackedInstance_AfterSet()
    {
        SingletonWindowSlot<FakeWindow> slot = new();
        FakeWindow window = new();
        slot.Set(window);

        bool activated = slot.TryActivateExisting(w => w.Activate());

        Assert.True(activated);
        Assert.True(window.Activated);
    }

    [Fact]
    public void TryActivateExisting_ReturnsFalse_AfterClear_DoesNotReuseClosedInstance()
    {
        SingletonWindowSlot<FakeWindow> slot = new();
        FakeWindow window = new();
        slot.Set(window);

        slot.Clear(); // simulates the tracked window's Closed event firing

        bool activated = slot.TryActivateExisting(w => w.Activate());

        Assert.False(activated);
        Assert.False(window.Activated);
    }

    [Fact]
    public void Set_AfterClear_TracksAFreshInstance_NotThePreviousOne()
    {
        SingletonWindowSlot<FakeWindow> slot = new();
        FakeWindow closedWindow = new();
        slot.Set(closedWindow);
        slot.Clear();

        FakeWindow freshWindow = new();
        slot.Set(freshWindow);

        bool activated = slot.TryActivateExisting(w => w.Activate());

        Assert.True(activated);
        Assert.True(freshWindow.Activated);
        Assert.False(closedWindow.Activated);
    }
}
