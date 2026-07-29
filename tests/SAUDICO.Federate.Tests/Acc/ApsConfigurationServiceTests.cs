using System;
using System.IO;
using SAUDICO.Federate.ACC.Configuration;
using SAUDICO.Federate.ACC.Errors;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

public sealed class ApsConfigurationServiceTests : IDisposable
{
    private readonly string directory;

    private const string BaseJson = @"{
        ""schemaVersion"": 1,
        ""environment"": ""Development"",
        ""enabled"": true,
        ""clientId"": ""base-client-id"",
        ""callbackUri"": ""http://localhost:8080/api/auth/callback/"",
        ""authorizationEndpoint"": ""https://developer.api.autodesk.com/authentication/v2/authorize"",
        ""tokenEndpoint"": ""https://developer.api.autodesk.com/authentication/v2/token"",
        ""userProfileEndpoint"": ""https://api.userprofile.autodesk.com/userinfo"",
        ""scopes"": [""data:read"", ""user-profile:read"", ""openid""]
    }";

    public ApsConfigurationServiceTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "SAUDICO-ACC-Config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(directory, true); } catch { }
    }

    private void WriteBase(string json) => File.WriteAllText(Path.Combine(directory, "apssettings.json"), json);
    private void WriteLocal(string json) => File.WriteAllText(Path.Combine(directory, "apssettings.local.json"), json);

    [Fact]
    public void BaseConfig_Loads()
    {
        WriteBase(BaseJson);
        ApsConfiguration config = new ApsConfigurationService(directory).Load();

        Assert.Equal("base-client-id", config.ClientId);
        Assert.True(config.Enabled);
    }

    [Fact]
    public void LocalOverride_Wins()
    {
        WriteBase(BaseJson);
        WriteLocal(@"{ ""clientId"": ""local-client-id"" }");

        ApsConfiguration config = new ApsConfigurationService(directory).Load();

        Assert.Equal("local-client-id", config.ClientId);
    }

    [Fact]
    public void MissingClientId_FailsValidationWhenEnabled()
    {
        WriteBase(BaseJson.Replace("\"base-client-id\"", "\"\""));
        ApsConfigurationService service = new ApsConfigurationService(directory);
        ApsConfiguration config = service.Load();

        ApsConfigurationValidationResult result = service.Validate(config);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void InvalidCallback_FailsValidation()
    {
        WriteBase(BaseJson.Replace("http://localhost:8080/api/auth/callback/", "https://example.com/callback"));
        ApsConfigurationService service = new ApsConfigurationService(directory);
        ApsConfiguration config = service.Load();

        ApsConfigurationValidationResult result = service.Validate(config);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void WriteScope_FailsValidation()
    {
        WriteBase(BaseJson.Replace(@"""data:read"", ""user-profile:read"", ""openid""", @"""data:write"""));
        ApsConfigurationService service = new ApsConfigurationService(directory);
        ApsConfiguration config = service.Load();

        ApsConfigurationValidationResult result = service.Validate(config);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ClientSecretField_IsRejected()
    {
        WriteBase(BaseJson.TrimEnd('}', '\r', '\n', ' ') + @", ""clientSecret"": ""x"" }");

        Assert.Throws<ApsConfigurationException>(() => new ApsConfigurationService(directory).Load());
    }

    [Fact]
    public void DisabledConfig_DoesNotRequireClientId()
    {
        WriteBase(BaseJson.Replace("\"enabled\": true", "\"enabled\": false").Replace("\"base-client-id\"", "\"\""));
        ApsConfigurationService service = new ApsConfigurationService(directory);
        ApsConfiguration config = service.Load();

        ApsConfigurationValidationResult result = service.Validate(config);

        Assert.True(result.IsValid);
    }
}
