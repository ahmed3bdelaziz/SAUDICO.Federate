using System;
using SAUDICO.Federate.ACC.Profile;

namespace SAUDICO.Federate.ACC.Authentication;

public sealed class ApsAuthenticationStateChangedEventArgs : EventArgs
{
    public ApsAuthenticationState OldState { get; set; }
    public ApsAuthenticationState NewState { get; set; }
    public ApsUserProfile? User { get; set; }

    /// <summary>Safe, user-facing message only — never a raw error/exception detail.</summary>
    public string? Message { get; set; }
}
