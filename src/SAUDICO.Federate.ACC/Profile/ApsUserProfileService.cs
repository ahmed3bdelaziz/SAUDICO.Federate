using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.Errors;
using SAUDICO.Federate.ACC.Http;

namespace SAUDICO.Federate.ACC.Profile;

public sealed class ApsUserProfileService : IApsUserProfileService
{
    private readonly IApsHttpTransport transport;

    public ApsUserProfileService(IApsHttpTransport transport)
    {
        this.transport = transport;
    }

    public async Task<ApsUserProfile> GetCurrentUserAsync(
        string userProfileEndpoint, string accessToken, CancellationToken cancellationToken)
    {
        JsonDocument response;
        try
        {
            response = await transport
                .GetJsonAsync(userProfileEndpoint, accessToken, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ApsApiException ex) when (ex.StatusCode == 401)
        {
            throw new ApsSessionExpiredException("Your Autodesk session has expired. Sign in again.", ex);
        }

        using (response)
        {
            JsonElement root = response.RootElement;

            return new ApsUserProfile
            {
                UserId = GetString(root, "sub") ?? "",
                DisplayName = GetString(root, "name") ?? GetString(root, "preferred_username") ?? "",
                Email = GetString(root, "email"),
                ProfileImageUri = GetString(root, "picture")
            };
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement element) ? element.GetString() : null;
}
