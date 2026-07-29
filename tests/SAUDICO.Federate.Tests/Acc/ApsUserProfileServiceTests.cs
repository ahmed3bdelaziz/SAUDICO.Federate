using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.Errors;
using SAUDICO.Federate.ACC.Http;
using SAUDICO.Federate.ACC.Profile;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

public sealed class ApsUserProfileServiceTests
{
    private static ApsUserProfileService MakeService(HttpStatusCode status, string body)
    {
        FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

        HttpClient client = new HttpClient(handler);
        return new ApsUserProfileService(new ApsHttpTransport(client));
    }

    [Fact]
    public async Task ValidResponse_MapsAllFields()
    {
        ApsUserProfileService service = MakeService(HttpStatusCode.OK,
            "{\"sub\":\"abc-123\",\"name\":\"Jane Doe\",\"email\":\"jane@example.com\",\"picture\":\"https://example.com/p.png\"}");

        ApsUserProfile profile = await service.GetCurrentUserAsync("https://api.userprofile.autodesk.com/userinfo", "fake-token", CancellationToken.None);

        Assert.Equal("abc-123", profile.UserId);
        Assert.Equal("Jane Doe", profile.DisplayName);
        Assert.Equal("jane@example.com", profile.Email);
        Assert.Equal("https://example.com/p.png", profile.ProfileImageUri);
    }

    [Fact]
    public async Task OptionalFieldsAbsent_LeavesThemNull()
    {
        ApsUserProfileService service = MakeService(HttpStatusCode.OK, "{\"sub\":\"abc-123\",\"name\":\"Jane Doe\"}");

        ApsUserProfile profile = await service.GetCurrentUserAsync("https://api.userprofile.autodesk.com/userinfo", "fake-token", CancellationToken.None);

        Assert.Equal("abc-123", profile.UserId);
        Assert.Null(profile.Email);
        Assert.Null(profile.ProfileImageUri);
    }

    [Fact]
    public async Task Unauthorized_MapsToSessionExpired()
    {
        ApsUserProfileService service = MakeService(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_token\"}");

        await Assert.ThrowsAsync<ApsSessionExpiredException>(() =>
            service.GetCurrentUserAsync("https://api.userprofile.autodesk.com/userinfo", "fake-token", CancellationToken.None));
    }
}
