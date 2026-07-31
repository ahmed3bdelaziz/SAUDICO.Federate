using System;

namespace SAUDICO.Federate.ACC.DataManagement;

/// <summary>
/// A safe, user-facing error from the read-only ACC browser. <see cref="Exception.Message"/>
/// is always one of <see cref="AccDataManagementErrorMapper"/>'s friendly
/// strings — never a raw APS error body, status text, or any sensitive
/// value. The original <see cref="Exception.InnerException"/> (typically an
/// <see cref="SAUDICO.Federate.ACC.Errors.ApsApiException"/>) is preserved for logging only.
/// </summary>
public sealed class AccDataManagementException : Exception
{
    public AccDataManagementException(string message)
        : base(message)
    {
    }

    public AccDataManagementException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
