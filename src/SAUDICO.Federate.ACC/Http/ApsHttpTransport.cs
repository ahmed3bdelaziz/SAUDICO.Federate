using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.Errors;

namespace SAUDICO.Federate.ACC.Http;

public sealed class ApsHttpTransport : IApsHttpTransport
{
    private readonly HttpClient client;

    public ApsHttpTransport()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
    {
    }

    /// <summary>Test seam: inject a client with a fake handler.</summary>
    public ApsHttpTransport(HttpClient client)
    {
        this.client = client;
        if (!this.client.DefaultRequestHeaders.UserAgent.TryParseAdd("SAUDICO-Federate-ACC/1.0"))
        {
            this.client.DefaultRequestHeaders.Add("User-Agent", "SAUDICO-Federate-ACC/1.0");
        }
    }

    public async Task<JsonDocument> PostFormAsync(
        string url, IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using FormUrlEncodedContent content = new FormUrlEncodedContent(form);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ApsApiException("SAUDICO Federate could not reach Autodesk Platform Services.", null, "network_error", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApsApiException("SAUDICO Federate could not reach Autodesk Platform Services.", null, "timeout", ex);
        }

        return await ReadJsonOrThrowAsync(response).ConfigureAwait(false);
    }

    public async Task<JsonDocument> GetJsonAsync(
        string url, string bearerToken, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ApsApiException("SAUDICO Federate could not reach Autodesk Platform Services.", null, "network_error", ex);
        }

        return await ReadJsonOrThrowAsync(response).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadJsonOrThrowAsync(HttpResponseMessage response)
    {
        using (response)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return string.IsNullOrWhiteSpace(body) ? JsonDocument.Parse("{}") : JsonDocument.Parse(body);
            }

            string? error = null;
            string? errorDescription = null;

            try
            {
                using JsonDocument errorDoc = JsonDocument.Parse(body);
                if (errorDoc.RootElement.TryGetProperty("error", out JsonElement errorElement))
                {
                    error = errorElement.GetString();
                }

                if (errorDoc.RootElement.TryGetProperty("error_description", out JsonElement descElement))
                {
                    errorDescription = descElement.GetString();
                }
            }
            catch (JsonException)
            {
                // Non-JSON error body; fall through with no parsed error code.
            }

            string message = error != null
                ? $"Autodesk returned {error}" + (errorDescription != null ? $": {errorDescription}" : "")
                : $"Autodesk returned HTTP {(int)response.StatusCode}.";

            throw new ApsApiException(message, (int)response.StatusCode, error);
        }
    }
}
