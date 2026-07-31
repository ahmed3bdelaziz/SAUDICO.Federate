namespace SAUDICO.Federate.ACC.Configuration;

public interface IApsConfigurationService
{
    /// <summary>
    /// Loads config/apssettings.json, merges config/apssettings.local.json over it
    /// when present, and returns the merged, structurally-parsed configuration.
    /// Throws <see cref="Errors.ApsConfigurationException"/> for malformed JSON or
    /// an unsupported schemaVersion. Does not throw for semantic issues (missing
    /// Client ID, bad callback, forbidden scopes) — call <see cref="Validate"/> for those.
    /// </summary>
    ApsConfiguration Load();

    ApsConfigurationValidationResult Validate(ApsConfiguration configuration);

    /// <summary>Safe-to-log/display summary — never includes the Client ID or full URLs.</summary>
    ApsConfigurationDiagnostics Diagnose(ApsConfiguration configuration);
}
