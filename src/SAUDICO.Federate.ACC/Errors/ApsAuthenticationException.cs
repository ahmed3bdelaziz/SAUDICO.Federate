using System;

namespace SAUDICO.Federate.ACC.Errors;

public enum ApsAuthenticationFailureReason
{
    ConfigurationInvalid,
    CallbackBindFailed,
    StateMismatch,
    Timeout,
    Cancelled,
    InvalidGrant,
    Network,
    UnexpectedResponse
}

public class ApsAuthenticationException : Exception
{
    public ApsAuthenticationFailureReason Reason { get; }

    public ApsAuthenticationException(string message, ApsAuthenticationFailureReason reason)
        : base(message)
    {
        Reason = reason;
    }

    public ApsAuthenticationException(string message, ApsAuthenticationFailureReason reason, Exception inner)
        : base(message, inner)
    {
        Reason = reason;
    }
}
