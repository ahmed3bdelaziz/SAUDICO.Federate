using System.Collections.Generic;

namespace SAUDICO.Federate.ACC.Configuration;

/// <summary>
/// Non-secret APS application configuration. Intentionally has no ClientSecret
/// property: this app is registered as a public (Desktop/Mobile/SPA) PKCE client.
/// </summary>
public sealed class ApsConfiguration
{
    public int SchemaVersion { get; set; } = 1;
    public string Environment { get; set; } = "Development";
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = "";
    public string CallbackUri { get; set; } = "";
    public string AuthorizationEndpoint { get; set; } = "";
    public string TokenEndpoint { get; set; } = "";
    public string UserProfileEndpoint { get; set; } = "";
    public List<string> Scopes { get; set; } = new();
}
