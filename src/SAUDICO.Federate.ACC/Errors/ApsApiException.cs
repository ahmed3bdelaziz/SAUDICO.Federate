using System;

namespace SAUDICO.Federate.ACC.Errors;

/// <summary>
/// A non-success HTTP response from an APS endpoint. Carries only the
/// documented OAuth/HTTP error code — never the raw response body,
/// request body, headers, tokens, or codes.
/// </summary>
public sealed class ApsApiException : Exception
{
    public int? StatusCode { get; }
    public string? ApsErrorCode { get; }

    public ApsApiException(string message, int? statusCode, string? apsErrorCode)
        : base(message)
    {
        StatusCode = statusCode;
        ApsErrorCode = apsErrorCode;
    }

    public ApsApiException(string message, int? statusCode, string? apsErrorCode, Exception inner)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ApsErrorCode = apsErrorCode;
    }
}
