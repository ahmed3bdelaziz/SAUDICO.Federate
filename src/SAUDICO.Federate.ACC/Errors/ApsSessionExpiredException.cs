using System;

namespace SAUDICO.Federate.ACC.Errors;

/// <summary>
/// Raised when a refresh attempt fails with invalid_grant, or an API call
/// returns 401 with no way to recover without interactive sign-in.
/// </summary>
public sealed class ApsSessionExpiredException : Exception
{
    public ApsSessionExpiredException(string message)
        : base(message)
    {
    }

    public ApsSessionExpiredException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
