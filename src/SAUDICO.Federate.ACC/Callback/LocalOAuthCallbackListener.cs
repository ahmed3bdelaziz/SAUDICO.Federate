using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.Errors;

namespace SAUDICO.Federate.ACC.Callback;

/// <summary>
/// Loopback-only OAuth callback listener using <see cref="HttpListener"/>, per
/// Autodesk's desktop-app PKCE guidance.
///
/// Windows URL ACL note: binding to a specific loopback hostname (e.g.
/// "http://localhost:8080/api/auth/callback/") does NOT require a
/// "netsh http add urlacl" reservation or administrator elevation. That
/// requirement only applies to strong wildcard bindings ("+" or "*"), which
/// this listener never uses — it always binds the single, exact, configured
/// callback URI.
/// </summary>
public sealed class LocalOAuthCallbackListener : ILocalOAuthCallbackListener
{
    private const string SuccessHtml =
        "<html><body><p>Authentication completed. You can return to Revit.</p></body></html>";

    public async Task<OAuthCallbackResult> ListenAsync(Uri callbackUri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        string prefix = callbackUri.ToString();
        if (!prefix.EndsWith("/", StringComparison.Ordinal))
        {
            prefix += "/";
        }

        using HttpListener listener = new HttpListener();
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new ApsAuthenticationException(
                "SAUDICO Federate could not start the local Autodesk sign-in callback.",
                ApsAuthenticationFailureReason.CallbackBindFailed,
                ex);
        }

        try
        {
            using CancellationTokenSource timeoutCts = new CancellationTokenSource(timeout);
            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            Task<HttpListenerContext> contextTask = listener.GetContextAsync();
            Task completed = await Task.WhenAny(contextTask, WaitForCancellation(linkedCts.Token)).ConfigureAwait(false);

            if (completed != contextTask)
            {
                bool userCancelled = cancellationToken.IsCancellationRequested;
                return userCancelled ? OAuthCallbackResult.Cancelled() : OAuthCallbackResult.TimedOut();
            }

            HttpListenerContext context = await contextTask.ConfigureAwait(false);
            OAuthCallbackResult result = ParseCallback(context.Request);
            await WriteResponseAsync(context.Response, SuccessHtml).ConfigureAwait(false);
            return result;
        }
        finally
        {
            SafeStop(listener);
        }
    }

    private static Task WaitForCancellation(CancellationToken token)
    {
        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
        token.Register(() => tcs.TrySetResult(true));
        return tcs.Task;
    }

    private static OAuthCallbackResult ParseCallback(HttpListenerRequest request)
    {
        System.Collections.Specialized.NameValueCollection query = request.QueryString;
        string? code = query["code"];
        string? state = query["state"];
        string? error = query["error"];
        string? errorDescription = query["error_description"];

        if (!string.IsNullOrEmpty(error))
        {
            return OAuthCallbackResult.Denied(error, errorDescription);
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return OAuthCallbackResult.Denied("missing_parameters", "The callback did not include a code and state.");
        }

        return OAuthCallbackResult.Ok(code!, state!);
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, string html)
    {
        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        }
        finally
        {
            response.OutputStream.Close();
        }
    }

    private static void SafeStop(HttpListener listener)
    {
        try
        {
            if (listener.IsListening)
            {
                listener.Stop();
            }
        }
        catch
        {
            // Best-effort shutdown only.
        }
    }
}
