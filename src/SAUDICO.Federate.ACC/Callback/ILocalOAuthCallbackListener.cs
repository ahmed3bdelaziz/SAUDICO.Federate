using System;
using System.Threading;
using System.Threading.Tasks;

namespace SAUDICO.Federate.ACC.Callback;

public interface ILocalOAuthCallbackListener
{
    /// <summary>
    /// Starts listening on <paramref name="callbackUri"/> and returns the single
    /// callback result. The caller must start this before launching the system
    /// browser. Completes exactly once, then stops and disposes the listener.
    /// </summary>
    Task<OAuthCallbackResult> ListenAsync(Uri callbackUri, TimeSpan timeout, CancellationToken cancellationToken);
}
