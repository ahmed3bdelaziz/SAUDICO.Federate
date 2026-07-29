using System.Threading;
using System.Threading.Tasks;

namespace SAUDICO.Federate.ACC.Profile;

public interface IApsUserProfileService
{
    Task<ApsUserProfile> GetCurrentUserAsync(
        string userProfileEndpoint, string accessToken, CancellationToken cancellationToken);
}
