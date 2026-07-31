using System;
using System.Collections.Generic;

namespace SAUDICO.Federate.ACC.Configuration;

/// <summary>
/// Safe-to-log/display configuration summary. Never carries the Client ID,
/// full authorization/token URLs, or any secret — only booleans and
/// generic structural validation text.
/// </summary>
public sealed class ApsConfigurationDiagnostics
{
    public bool IsEnabled { get; set; }
    public bool IsClientIdConfigured { get; set; }
    public bool IsCallbackValid { get; set; }
    public IReadOnlyList<string> ValidationMessages { get; set; } = Array.Empty<string>();
}
