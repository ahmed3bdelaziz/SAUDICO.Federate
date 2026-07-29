namespace SAUDICO.Federate.ACC.OAuthState;

public interface IOAuthStateStore
{
    void Begin(OAuthAuthorizationState state);

    /// <summary>
    /// Consumes the current in-flight state exactly once. Returns false and
    /// clears nothing if <paramref name="state"/> does not match the
    /// currently pending attempt (wrong-state callback), or if there is no
    /// pending attempt (replay / second callback for an already-consumed
    /// attempt).
    /// </summary>
    bool TryConsume(string state, out OAuthAuthorizationState? matched);

    void Clear();
}
