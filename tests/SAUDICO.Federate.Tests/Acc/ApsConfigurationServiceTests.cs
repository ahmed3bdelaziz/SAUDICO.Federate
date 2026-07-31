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
        ""callbackUri"": ""http://localhost:8080/"",
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
        WriteBase(BaseJson.Replace("http://localhost:8080/", "https://example.com/callback"));
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
    public void RootCallbackUri_IsAcceptedAndLoadedVerbatim_NoPathAppended()
    {
        WriteBase(BaseJson);
        ApsConfigurationService service = new ApsConfigurationService(directory);
        ApsConfiguration config = service.Load();

        Assert.Equal("http://localhost:8080/", config.CallbackUri);
        ApsConfigurationValidationResult result = service.Validate(config);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Diagnose_IsSafeAndNeverIncludesClientIdValue()
    {
        WriteBase(BaseJson);
        ApsConfigurationService service = new ApsConfigurationService(directory);
        ApsConfiguration config = service.Load();

        ApsConfigurationDiagnostics diagnostics = service.Diagnose(config);

        Assert.True(diagnostics.IsEnabled);
        Assert.True(diagnostics.IsClientIdConfigured);
        Assert.True(diagnostics.IsCallbackValid);
        foreach (string message in diagnostics.ValidationMessages)
        {
            Assert.DoesNotContain(config.ClientId, message, StringComparison.Ordinal);
        }
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

    [Fact]
    public void DefaultConfigDirectory_IsBasedOnAssemblyLocation()
    {
        // Verifies the resolution is anchored to where SAUDICO.Federate.ACC.dll itself
        // lives, not AppContext.BaseDirectory. Both happen to coincide in a flat
        // test-runner output folder, but they diverge inside Revit (where
        // AppContext.BaseDirectory resolves to Revit's own directory, not the add-in's) —
        // that divergence is exactly the bug this fixes, so the assembly-location
        // basis is what this test pins down.
        string assemblyDirectory = Path.GetDirectoryName(typeof(ApsConfigurationService).Assembly.Location)!;
        string expected = Path.Combine(assemblyDirectory, "config");

        Assert.Equal(expected, ApsConfigurationService.GetDefaultConfigDirectory());
    }

    [Fact]
    public void MissingBaseFile_ThrowsWithBaseFileNotFoundReason()
    {
        ApsConfigurationService service = new ApsConfigurationService(directory);

        ApsConfigurationException ex = Assert.Throws<ApsConfigurationException>(() => service.Load());

        Assert.Equal(ApsConfigurationLoadFailureReason.BaseFileNotFound, ex.Reason);
    }

    [Fact]
    public void EmptyBaseFile_ThrowsWithBaseFileEmptyReason()
    {
        WriteBase("");
        ApsConfigurationService service = new ApsConfigurationService(directory);

        ApsConfigurationException ex = Assert.Throws<ApsConfigurationException>(() => service.Load());

        Assert.Equal(ApsConfigurationLoadFailureReason.BaseFileEmpty, ex.Reason);
    }

    [Fact]
    public void MalformedBaseJson_ThrowsSafelyInsteadOfRawJsonException()
    {
        WriteBase("{ this is not valid json ,,, }");
        ApsConfigurationService service = new ApsConfigurationService(directory);

        ApsConfigurationException ex = Assert.Throws<ApsConfigurationException>(() => service.Load());

        Assert.Equal(ApsConfigurationLoadFailureReason.MalformedJson, ex.Reason);
    }

    [Fact]
    public void MalformedLocalOverrideJson_ThrowsSafelyInsteadOfRawJsonException()
    {
        WriteBase(BaseJson);
        WriteLocal("{ not valid ,,, ");
        ApsConfigurationService service = new ApsConfigurationService(directory);

        ApsConfigurationException ex = Assert.Throws<ApsConfigurationException>(() => service.Load());

        Assert.Equal(ApsConfigurationLoadFailureReason.LocalOverrideMalformed, ex.Reason);
    }

    [Fact]
    public void Utf8BomBaseFile_LoadsCorrectly()
    {
        string path = Path.Combine(directory, "apssettings.json");
        File.WriteAllText(path, BaseJson, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        ApsConfiguration config = new ApsConfigurationService(directory).Load();

        Assert.Equal("base-client-id", config.ClientId);
    }

    [Fact]
    public void LocalOverrideWithoutSchemaVersion_StillLoads_BaseSchemaVersionPreserved()
    {
        WriteBase(BaseJson);
        WriteLocal(@"{ ""clientId"": ""local-client-id"" }");

        ApsConfiguration config = new ApsConfigurationService(directory).Load();

        Assert.Equal(1, config.SchemaVersion);
        Assert.Equal("local-client-id", config.ClientId);
    }

    [Fact]
    public void LocalOverrideWithoutEndpointsOrScopes_BaseEndpointsAndScopesSurviveMerge()
    {
        WriteBase(BaseJson);
        WriteLocal(@"{ ""enabled"": true, ""clientId"": ""local-client-id"", ""callbackUri"": ""http://localhost:8080/"" }");

        ApsConfiguration config = new ApsConfigurationService(directory).Load();

        Assert.Equal("https://developer.api.autodesk.com/authentication/v2/authorize", config.AuthorizationEndpoint);
        Assert.Equal("https://developer.api.autodesk.com/authentication/v2/token", config.TokenEndpoint);
        Assert.Equal("https://api.userprofile.autodesk.com/userinfo", config.UserProfileEndpoint);
        Assert.Equal(3, config.Scopes.Count);
    }

    [Fact]
    public void LocalClientId_OverridesEmptyBaseValue()
    {
        WriteBase(BaseJson.Replace("\"base-client-id\"", "\"\""));
        WriteLocal(@"{ ""enabled"": true, ""clientId"": ""local-client-id"" }");

        ApsConfiguration config = new ApsConfigurationService(directory).Load();

        Assert.Equal("local-client-id", config.ClientId);
    }

    [Fact]
    public void MissingLocalOverrideProperties_DoNotEraseBaseValues()
    {
        WriteBase(BaseJson);
        WriteLocal(@"{ ""clientId"": ""local-client-id"" }");

        ApsConfiguration config = new ApsConfigurationService(directory).Load();

        Assert.True(config.Enabled);
        Assert.Equal("http://localhost:8080/", config.CallbackUri);
    }
}
