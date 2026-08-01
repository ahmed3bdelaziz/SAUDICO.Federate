using System;

namespace SAUDICO.Federate.ACC.DataManagement;

/// <summary>
/// The result of an OSS <c>signeds3download</c> request. Property names and
/// the <see cref="Status"/> value set are taken verbatim from the official
/// <c>aps-sdk-openapi/oss/oss.yaml</c> spec: the response carries a
/// <c>status</c> of <c>complete</c>, <c>chunked</c>, or <c>fallback</c>,
/// with <c>url</c> populated for <c>complete</c>/<c>fallback</c> and
/// <c>urls</c> (per-chunk) for <c>chunked</c>.
/// </summary>
public sealed class AccSignedDownload
{
    public string Status { get; init; } = "";

    /// <summary>The single signed S3 URL. Null when <see cref="Status"/> is <c>chunked</c>.</summary>
    public string? Url { get; init; }

    /// <summary>Total object size in bytes, when APS reported one.</summary>
    public long? Size { get; init; }

    /// <summary>
    /// True only when a single URL is genuinely available. A <c>chunked</c>
    /// response is deliberately NOT stitched together here — multi-part
    /// chunk assembly was not verified against a live response this session,
    /// so it is refused with a clear message rather than guessed at.
    /// </summary>
    public bool HasSingleUrl =>
        !string.IsNullOrEmpty(Url) &&
        (string.Equals(Status, "complete", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Status, "fallback", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Splits an OSS object URN into its bucket key and object key. Format
/// (verified against the official Data Management "IDs in the Data
/// Management API" guidance): <c>urn:adsk.objects:os.object:{bucket}/{objectKey}</c>
/// — the object key itself may contain further "/" characters, so only the
/// FIRST separator after the prefix is treated as the bucket boundary.
/// </summary>
public static class AccStorageUrn
{
    private const string Prefix = "urn:adsk.objects:os.object:";

    public static bool TryParse(string? urn, out string bucketKey, out string objectKey)
    {
        bucketKey = "";
        objectKey = "";

        if (string.IsNullOrWhiteSpace(urn) || !urn!.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string remainder = urn.Substring(Prefix.Length);
        int slash = remainder.IndexOf('/');
        if (slash <= 0 || slash == remainder.Length - 1)
        {
            return false;
        }

        bucketKey = remainder.Substring(0, slash);
        objectKey = remainder.Substring(slash + 1);
        return true;
    }
}
