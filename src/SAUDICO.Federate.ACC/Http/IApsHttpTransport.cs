using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SAUDICO.Federate.ACC.Http;

/// <summary>
/// The single shared, safe HTTP transport for all APS calls. Centralizes
/// timeout, User-Agent, and error mapping so individual typed clients stay
/// small and never assemble raw HTTP plumbing themselves.
/// </summary>
public interface IApsHttpTransport
{
    Task<JsonDocument> PostFormAsync(
        string url, IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken);

    Task<JsonDocument> GetJsonAsync(
        string url, string bearerToken, CancellationToken cancellationToken);
}
