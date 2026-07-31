using System;

namespace SAUDICO.Federate.UI;

/// <summary>
/// Ensures the ACC browser starts loading hubs exactly once as soon as
/// sign-in is true — whether that is already the case at construction time
/// (the underlying <c>IApsAuthenticationService</c> was already SignedIn
/// from an earlier "Add ACC Models" session, so <see cref="AuthViewModel"/>
/// starts life already signed in and no PropertyChanged event for
/// IsSignedIn will ever fire) or via a later PropertyChanged transition.
/// Framework-independent so this exact lifecycle defect — and its fix — is
/// directly unit-testable without constructing a real WPF window.
/// </summary>
public static class AccBrowseAutoStart
{
    /// <summary>Call once, immediately after the browse panel is wired up. Starts the browser if already signed in.</summary>
    public static void EvaluateInitialState(bool isSignedInNow, Action start)
    {
        if (isSignedInNow)
        {
            start();
        }
    }

    /// <summary>
    /// Call from <see cref="AuthViewModel"/>'s PropertyChanged handler.
    /// Only reacts to the IsSignedIn property — every other property
    /// change (DisplayName, ErrorMessage, State, ...) is ignored, so it
    /// never calls Start or Reset for unrelated notifications.
    /// </summary>
    public static void HandlePropertyChanged(string? propertyName, bool isSignedInNow, Action start, Action reset)
    {
        if (propertyName != nameof(AuthViewModel.IsSignedIn))
        {
            return;
        }

        if (isSignedInNow)
        {
            start();
        }
        else
        {
            reset();
        }
    }
}
