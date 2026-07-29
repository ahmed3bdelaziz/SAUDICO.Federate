using System;

namespace SAUDICO.Federate.ACC.Tokens;

/// <summary>
/// Full in-memory token set. Only <see cref="RefreshToken"/> is ever
/// persisted (via <see cref="IApsTokenStore"/>); <see cref="AccessToken"/>
/// stays memory-only for the lifetime of the process.
/// </summary>
public sealed class ApsToken
{
    public string AccessToken { get; set; } = "";
    public string TokenType { get; set; } = "Bearer";
    public string? RefreshToken { get; set; }
    public string? IdToken { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    public bool IsExpired(TimeSpan safetyMargin) =>
        DateTime.UtcNow + safetyMargin >= ExpiresAtUtc;
}
