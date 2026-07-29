using System.Threading;
using System.Threading.Tasks;

namespace SAUDICO.Federate.ACC.Tokens;

public interface IApsTokenStore
{
    /// <summary>
    /// Persists only the refresh token (DPAPI-protected, CurrentUser scope).
    /// The access token is never written to disk.
    /// </summary>
    Task SaveAsync(ApsToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a partial <see cref="ApsToken"/> with only <see cref="ApsToken.RefreshToken"/>
    /// populated (AccessToken empty, ExpiresAtUtc in the past so a refresh is forced),
    /// or null if nothing is stored or the stored data is corrupt/undecryptable.
    /// </summary>
    Task<ApsToken?> LoadAsync(CancellationToken cancellationToken);

    Task DeleteAsync(CancellationToken cancellationToken);
}
