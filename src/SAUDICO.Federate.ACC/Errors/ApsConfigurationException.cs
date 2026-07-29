using System;
using System.Collections.Generic;

namespace SAUDICO.Federate.ACC.Errors;

public sealed class ApsConfigurationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ApsConfigurationException(string message)
        : base(message)
    {
        Errors = new[] { message };
    }

    public ApsConfigurationException(string message, IReadOnlyList<string> errors)
        : base(message)
    {
        Errors = errors;
    }
}
