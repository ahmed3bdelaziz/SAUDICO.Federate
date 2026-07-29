using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using SAUDICO.Federate.ACC.Errors;

namespace SAUDICO.Federate.ACC.Configuration;

public sealed class ApsConfigurationService : IApsConfigurationService
{
    private const int SupportedSchemaVersion = 1;

    private static readonly string[] AllowedScopes =
    {
        "data:read", "user:read", "user-profile:read", "viewables:read", "openid"
    };

    private static readonly string[] AllowedAuthorizeTokenHosts = { "developer.api.autodesk.com" };
    private static readonly string[] AllowedUserProfileHosts = { "api.userprofile.autodesk.com" };

    private readonly string configDirectory;

    public ApsConfigurationService()
        : this(Path.Combine(AppContext.BaseDirectory, "config"))
    {
    }

    public ApsConfigurationService(string configDirectory)
    {
        this.configDirectory = configDirectory;
    }

    public ApsConfiguration Load()
    {
        string basePath = Path.Combine(configDirectory, "apssettings.json");
        string localPath = Path.Combine(configDirectory, "apssettings.local.json");

        if (!File.Exists(basePath))
        {
            throw new ApsConfigurationException("APS configuration template (apssettings.json) is missing.");
        }

        JsonObject merged = ReadObject(basePath);

        if (File.Exists(localPath))
        {
            JsonObject local = ReadObject(localPath);
            foreach (KeyValuePair<string, JsonNode?> property in local)
            {
                merged[property.Key] = property.Value?.DeepClone();
            }
        }

        if (ContainsClientSecretKey(merged))
        {
            throw new ApsConfigurationException(
                "APS configuration must not define a clientSecret. This app is a public PKCE client.");
        }

        ApsConfiguration? configuration;
        try
        {
            configuration = merged.Deserialize<ApsConfiguration>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            throw new ApsConfigurationException("APS configuration could not be parsed: " + ex.Message);
        }

        if (configuration == null)
        {
            throw new ApsConfigurationException("APS configuration could not be parsed.");
        }

        if (configuration.SchemaVersion != SupportedSchemaVersion)
        {
            throw new ApsConfigurationException(
                $"Unsupported APS configuration schemaVersion {configuration.SchemaVersion}. Expected {SupportedSchemaVersion}.");
        }

        return configuration;
    }

    public ApsConfigurationValidationResult Validate(ApsConfiguration configuration)
    {
        List<string> errors = new List<string>();

        if (configuration.SchemaVersion != SupportedSchemaVersion)
        {
            errors.Add($"Unsupported schemaVersion {configuration.SchemaVersion}.");
        }

        if (!configuration.Enabled)
        {
            return errors.Count == 0
                ? ApsConfigurationValidationResult.Success()
                : ApsConfigurationValidationResult.Failure(errors);
        }

        if (string.IsNullOrWhiteSpace(configuration.ClientId))
        {
            errors.Add("Client ID is required when APS is enabled.");
        }

        ValidateCallbackUri(configuration.CallbackUri, errors);
        ValidateEndpoint(configuration.AuthorizationEndpoint, "authorizationEndpoint", AllowedAuthorizeTokenHosts, errors);
        ValidateEndpoint(configuration.TokenEndpoint, "tokenEndpoint", AllowedAuthorizeTokenHosts, errors);
        ValidateEndpoint(configuration.UserProfileEndpoint, "userProfileEndpoint", AllowedUserProfileHosts, errors);
        ValidateScopes(configuration.Scopes, errors);

        return errors.Count == 0
            ? ApsConfigurationValidationResult.Success()
            : ApsConfigurationValidationResult.Failure(errors);
    }

    private static void ValidateCallbackUri(string callbackUri, List<string> errors)
    {
        if (!Uri.TryCreate(callbackUri, UriKind.Absolute, out Uri? uri))
        {
            errors.Add("callbackUri must be an absolute URI.");
            return;
        }

        bool isLoopback = string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(uri.Host, out IPAddress? address) && IPAddress.IsLoopback(address));

        if (!isLoopback)
        {
            errors.Add("callbackUri host must be localhost or a loopback address.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp)
        {
            errors.Add("callbackUri must use http for the local loopback listener.");
        }
    }

    private static void ValidateEndpoint(string endpoint, string name, string[] allowedHosts, List<string> errors)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{name} must be an absolute https URI.");
            return;
        }

        bool allowed = false;
        foreach (string host in allowedHosts)
        {
            if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase))
            {
                allowed = true;
                break;
            }
        }

        if (!allowed)
        {
            errors.Add($"{name} host '{uri.Host}' is not a recognized Autodesk endpoint.");
        }
    }

    private static void ValidateScopes(List<string> scopes, List<string> errors)
    {
        if (scopes == null || scopes.Count == 0)
        {
            errors.Add("At least one scope is required.");
            return;
        }

        foreach (string scope in scopes)
        {
            bool allowed = false;
            foreach (string candidate in AllowedScopes)
            {
                if (string.Equals(scope, candidate, StringComparison.Ordinal))
                {
                    allowed = true;
                    break;
                }
            }

            if (!allowed)
            {
                errors.Add($"Scope '{scope}' is not a permitted read-only scope.");
            }
        }
    }

    private static bool ContainsClientSecretKey(JsonObject obj)
    {
        foreach (KeyValuePair<string, JsonNode?> property in obj)
        {
            if (string.Equals(property.Key, "clientSecret", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static JsonObject ReadObject(string path)
    {
        string text = File.ReadAllText(path);
        JsonNode? node = JsonNode.Parse(text);
        if (node is not JsonObject obj)
        {
            throw new ApsConfigurationException($"'{path}' must contain a JSON object.");
        }

        return obj;
    }
}
