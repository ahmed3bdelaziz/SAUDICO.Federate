using System;

namespace SAUDICO.Federate.ACC.OAuthState;

/// <summary>
/// A single in-flight login attempt's server-side-equivalent binding.
/// Memory-only: never persisted, never logged.
/// </summary>
public sealed class OAuthAuthorizationState
{
    public string State { get; set; } = "";
    public string CodeVerifier { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
