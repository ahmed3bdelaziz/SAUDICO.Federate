namespace SAUDICO.Federate.ACC.OAuthState;

public sealed class InMemoryOAuthStateStore : IOAuthStateStore
{
    private readonly object gate = new object();
    private OAuthAuthorizationState? pending;

    public void Begin(OAuthAuthorizationState state)
    {
        lock (gate)
        {
            pending = state;
        }
    }

    public bool TryConsume(string state, out OAuthAuthorizationState? matched)
    {
        lock (gate)
        {
            if (pending != null && pending.State == state)
            {
                matched = pending;
                pending = null;
                return true;
            }

            matched = null;
            return false;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            pending = null;
        }
    }
}
