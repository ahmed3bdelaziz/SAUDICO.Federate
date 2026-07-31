using System;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.Profile;

namespace SAUDICO.Federate.ACC.Authentication;

public interface IApsAuthenticationService
{
    bool IsAuthenticated { get; }

    ApsAuthenticationState State { get; }

    ApsUserProfile? CurrentUser { get; }

    event EventHandler<ApsAuthenticationStateChangedEventArgs>? AuthenticationStateChanged;

    Task<ApsUserProfile> SignInAsync(CancellationToken cancellationToken);

    Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Forces an actual refresh-token grant call regardless of the cached
    /// access token's apparent expiry. Used by callers (e.g. the ACC Data
    /// Management client) that received a 401 from an API despite
    /// <see cref="GetValidAccessTokenAsync"/> having returned what looked
    /// like a still-valid token, so a real refresh is attempted exactly
    /// once before giving up. Reuses the same refresh flow, gating, and
    /// state transitions as <see cref="GetValidAccessTokenAsync"/>.
    /// </summary>
    Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);
}
