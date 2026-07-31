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

    private static readonly JsonSerializerOptions CaseInsensitive = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string configDirectory;

    public ApsConfigurationService()
        : this(GetDefaultConfigDirectory())
    {
    }

    public ApsConfigurationService(string configDirectory)
    {
        this.configDirectory = configDirectory;
    }

    /// <summary>
    /// Resolves the config folder relative to where THIS assembly
    /// (SAUDICO.Federate.ACC.dll) actually sits on disk — i.e. the
    /// installed add-in directory. <see cref="AppContext.BaseDirectory"/>
    /// must never be used here: when Revit loads this assembly, that
    /// property resolves to Revit's own base directory, not the add-in's
    /// installed folder, causing the base template to never be found.
    /// </summary>
    public static string GetDefaultConfigDirectory()
    {
        string? assemblyDirectory = Path.GetDirectoryName(typeof(ApsConfigurationService).Assembly.Location);
        return Path.Combine(assemblyDirectory ?? AppContext.BaseDirectory, "config");
    }

    public ApsConfiguration Load()
    {
        string basePath = Path.Combine(configDirectory, "apssettings.json");
        string localPath = Path.Combine(configDirectory, "apssettings.local.json");

        ApsConfiguration configuration = ReadBase(basePath);

        if (File.Exists(localPath))
        {
            ApsConfigurationOverride @override = ReadOverride(localPath);
            Apply(configuration, @override);
        }

        if (configuration.SchemaVersion != SupportedSchemaVersion)
        {
            throw new ApsConfigurationException(
                $"Unsupported APS configuration schemaVersion {configuration.SchemaVersion}. Expected {SupportedSchemaVersion}.",
                ApsConfigurationLoadFailureReason.UnsupportedSchemaVersion);
        }

        return configuration;
    }

    private static ApsConfiguration ReadBase(string basePath)
    {
        if (!File.Exists(basePath))
        {
            throw new ApsConfigurationException(
                "APS configuration template (apssettings.json) is missing.",
                ApsConfigurationLoadFailureReason.BaseFileNotFound);
        }

        JsonObject obj = ReadObject(basePath, ApsConfigurationLoadFailureReason.MalformedJson);

        if (ContainsClientSecretKey(obj))
        {
            throw new ApsConfigurationException(
                "APS configuration must not define a clientSecret. This app is a public PKCE client.",
                ApsConfigurationLoadFailureReason.ClientSecretNotPermitted);
        }

        ApsConfiguration? configuration;
        try
        {
            configuration = obj.Deserialize<ApsConfiguration>(CaseInsensitive);
        }
        catch (JsonException ex)
        {
            throw new ApsConfigurationException(
                "APS base configuration could not be deserialized: " + ex.Message,
                ApsConfigurationLoadFailureReason.DeserializationFailure, ex);
        }

        if (configuration == null)
        {
            throw new ApsConfigurationException(
                "APS base configuration deserialized to nothing.",
                ApsConfigurationLoadFailureReason.DeserializationFailure);
        }

        return configuration;
    }

    private static ApsConfigurationOverride ReadOverride(string localPath)
    {
        JsonObject obj = ReadObject(localPath, ApsConfigurationLoadFailureReason.LocalOverrideMalformed);

        if (ContainsClientSecretKey(obj))
        {
            throw new ApsConfigurationException(
                "APS local override must not define a clientSecret.",
                ApsConfigurationLoadFailureReason.ClientSecretNotPermitted);
        }

        try
        {
            return obj.Deserialize<ApsConfigurationOverride>(CaseInsensitive) ?? new ApsConfigurationOverride();
        }
        catch (JsonException ex)
        {
            throw new ApsConfigurationException(
                "APS local override could not be deserialized: " + ex.Message,
                ApsConfigurationLoadFailureReason.LocalOverrideMalformed, ex);
        }
    }

    /// <summary>Applies only the properties actually present in the override — missing properties never erase base values.</summary>
    private static void Apply(ApsConfiguration configuration, ApsConfigurationOverride @override)
    {
        if (@override.Enabled.HasValue)
        {
            configuration.Enabled = @override.Enabled.Value;
        }

        if (@override.ClientId != null)
        {
            configuration.ClientId = @override.ClientId;
        }

        if (@override.CallbackUri != null)
        {
            configuration.CallbackUri = @override.CallbackUri;
        }
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

    public ApsConfigurationDiagnostics Diagnose(ApsConfiguration configuration)
    {
        List<string> messages = new List<string>();
        bool isClientIdConfigured = !string.IsNullOrWhiteSpace(configuration.ClientId);
        bool isCallbackValid = ValidateCallbackUri(configuration.CallbackUri, messages);

        if (configuration.Enabled && !isClientIdConfigured)
        {
            messages.Add("Client ID is required when APS is enabled.");
        }

        return new ApsConfigurationDiagnostics
        {
            IsEnabled = configuration.Enabled,
            IsClientIdConfigured = isClientIdConfigured,
            IsCallbackValid = isCallbackValid,
            ValidationMessages = messages
        };
    }

    private static bool ValidateCallbackUri(string callbackUri, List<string> errors)
    {
        if (!Uri.TryCreate(callbackUri, UriKind.Absolute, out Uri? uri))
        {
            errors.Add("callbackUri must be an absolute URI.");
            return false;
        }

        bool isLoopback = string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(uri.Host, out IPAddress? address) && IPAddress.IsLoopback(address));

        bool valid = true;

        if (!isLoopback)
        {
            errors.Add("callbackUri host must be localhost or a loopback address.");
            valid = false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp)
        {
            errors.Add("callbackUri must use http for the local loopback listener.");
            valid = false;
        }

        return valid;
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

    private static JsonObject ReadObject(string path, ApsConfigurationLoadFailureReason malformedReason)
    {
        string fileName = Path.GetFileName(path);
        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new ApsConfigurationException(
                $"'{fileName}' could not be read.", ApsConfigurationLoadFailureReason.BaseFileInaccessible, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ApsConfigurationException(
                $"'{fileName}' could not be read.", ApsConfigurationLoadFailureReason.BaseFileInaccessible, ex);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ApsConfigurationException(
                $"'{fileName}' is empty.", ApsConfigurationLoadFailureReason.BaseFileEmpty);
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new ApsConfigurationException(
                $"'{fileName}' is not valid JSON: {ex.Message}", malformedReason, ex);
        }

        if (node is not JsonObject obj)
        {
            throw new ApsConfigurationException($"'{fileName}' must contain a JSON object.", malformedReason);
        }

        return obj;
    }
}
