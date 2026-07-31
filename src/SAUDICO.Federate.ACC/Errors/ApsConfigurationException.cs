using System;
using System.Collections.Generic;

namespace SAUDICO.Federate.ACC.Errors;

public enum ApsConfigurationLoadFailureReason
{
    BaseFileNotFound,
    BaseFileInaccessible,
    BaseFileEmpty,
    MalformedJson,
    DeserializationFailure,
    UnsupportedSchemaVersion,
    ClientSecretNotPermitted,
    LocalOverrideMalformed
}

public sealed class ApsConfigurationException : Exception
{
    public IReadOnlyList<string> Errors { get; }
    public ApsConfigurationLoadFailureReason? Reason { get; }

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

    public ApsConfigurationException(string message, ApsConfigurationLoadFailureReason reason)
        : base(message)
    {
        Errors = new[] { message };
        Reason = reason;
    }

    public ApsConfigurationException(string message, ApsConfigurationLoadFailureReason reason, Exception inner)
        : base(message, inner)
    {
        Errors = new[] { message };
        Reason = reason;
    }
}
