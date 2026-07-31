using System;

namespace SAUDICO.Federate.UI;

/// <summary>
/// Tracks a single lazily-created window instance so a second "Add ACC
/// Models" click activates the still-open window instead of creating a
/// duplicate, and a click after the window closed creates a fresh instance
/// instead of reusing the closed one. Framework-independent (no WPF types)
/// so this reuse guarantee is directly unit-testable.
/// </summary>
public sealed class SingletonWindowSlot<TWindow> where TWindow : class
{
    private TWindow? current;

    public bool TryActivateExisting(Action<TWindow> activate)
    {
        if (current == null)
        {
            return false;
        }

        activate(current);
        return true;
    }

    public void Set(TWindow window) => current = window;

    public void Clear() => current = null;
}
