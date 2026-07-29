using System.Collections.Generic;

namespace SAUDICO.Federate.ACC.Configuration;

public sealed class ApsConfigurationValidationResult
{
    public bool IsValid { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = System.Array.Empty<string>();

    public static ApsConfigurationValidationResult Success() =>
        new() { IsValid = true };

    public static ApsConfigurationValidationResult Failure(IReadOnlyList<string> errors) =>
        new() { IsValid = false, Errors = errors };
}
