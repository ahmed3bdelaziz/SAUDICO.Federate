namespace SAUDICO.Federate.ACC.Configuration;

/// <summary>
/// The intentionally partial shape of config/apssettings.local.json. Only
/// the properties actually present in the file are non-null; anything
/// absent leaves the corresponding base <see cref="ApsConfiguration"/>
/// value untouched. Deliberately has no SchemaVersion/Scopes/Endpoints —
/// the local override must never be required to repeat them.
/// </summary>
public sealed class ApsConfigurationOverride
{
    public bool? Enabled { get; set; }
    public string? ClientId { get; set; }
    public string? CallbackUri { get; set; }
}
