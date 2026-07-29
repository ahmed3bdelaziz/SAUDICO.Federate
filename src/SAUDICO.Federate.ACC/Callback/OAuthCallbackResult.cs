namespace SAUDICO.Federate.ACC.Callback;

public sealed class OAuthCallbackResult
{
    public bool Success { get; set; }
    public string? Code { get; set; }
    public string? State { get; set; }
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }
    public bool IsCancelled { get; set; }
    public bool IsTimedOut { get; set; }

    public static OAuthCallbackResult Ok(string code, string state) =>
        new() { Success = true, Code = code, State = state };

    public static OAuthCallbackResult Denied(string? error, string? errorDescription) =>
        new() { Success = false, Error = error, ErrorDescription = errorDescription };

    public static OAuthCallbackResult TimedOut() =>
        new() { Success = false, IsTimedOut = true };

    public static OAuthCallbackResult Cancelled() =>
        new() { Success = false, IsCancelled = true };
}
