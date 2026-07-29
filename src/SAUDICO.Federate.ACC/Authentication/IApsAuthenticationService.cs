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

    Task SignOutAsync(CancellationToken cancellationToken);
}
