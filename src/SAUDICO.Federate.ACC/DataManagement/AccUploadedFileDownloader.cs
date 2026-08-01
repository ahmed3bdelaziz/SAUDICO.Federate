using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace SAUDICO.Federate.ACC.DataManagement;

/// <summary>
/// Streams a signed S3 URL to a local file. Separate from
/// <see cref="Http.IApsHttpTransport"/> on purpose: that transport has a
/// 30-second timeout suited to small JSON calls, whereas an RVT download can
/// legitimately run for many minutes. The signed URL already carries its own
/// authorization, so no bearer token is ever attached here.
/// </summary>
public interface IAccBinaryDownloader
{
    Task DownloadToFileAsync(string url, string destinationPath, IProgress<long>? progress, CancellationToken cancellationToken);
}

public sealed class HttpAccBinaryDownloader : IAccBinaryDownloader
{
    private readonly HttpClient client;

    public HttpAccBinaryDownloader()
        : this(new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
    {
    }

    /// <summary>Test seam: inject a client with a fake handler.</summary>
    public HttpAccBinaryDownloader(HttpClient client)
    {
        this.client = client;
    }

    public async Task DownloadToFileAsync(string url, string destinationPath, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using FileStream destination = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        byte[] buffer = new byte[81920];
        long total = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            total += read;
            progress?.Report(total);
        }
    }
}

/// <summary>Raised when an uploaded ACC file could not be made available locally. Carries only safe, user-facing text.</summary>
public sealed class AccDownloadException : Exception
{
    public AccDownloadException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// Makes a plain uploaded ACC RVT available as a local temporary copy so it
/// can be exported through the existing file-based pipeline.
///
/// This is strictly read-only against ACC: version metadata GET, an OSS
/// signed-download GET, then an HTTP GET of the signed URL. Nothing is ever
/// uploaded, renamed, moved, published, or deleted in ACC, and the ACC-hosted
/// original is never opened by Revit — only the local copy is, so the
/// original cannot be modified even in principle.
///
/// Revit Cloud Worksharing (C4R) models are NOT handled here: they have no
/// storage object to download and are opened directly by
/// Region/ProjectGuid/ModelGuid instead.
/// </summary>
public sealed class AccUploadedFileDownloader
{
    private readonly IAccDataManagementClient client;
    private readonly IAccBinaryDownloader downloader;

    public AccUploadedFileDownloader(IAccDataManagementClient client, IAccBinaryDownloader downloader)
    {
        this.client = client;
        this.downloader = downloader;
    }

    /// <summary>Root of the per-session temporary download cache. Never inside the user's output folder, and never an ACC path.</summary>
    public static string DefaultCacheRoot =>
        Path.Combine(Path.GetTempPath(), "SAUDICO", "Federate", "AccDownloads");

    /// <summary>
    /// Downloads the given version to a fresh temporary file and returns its
    /// full path. The caller owns the file and must delete it when done.
    /// </summary>
    public async Task<string> DownloadAsync(
        string projectId,
        string versionId,
        string displayName,
        string cacheRoot,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        string? storageUrn = await client.GetVersionStorageUrnAsync(projectId, versionId, cancellationToken).ConfigureAwait(false);

        if (storageUrn == null)
        {
            throw new AccDownloadException(
                "This ACC file has no downloadable storage location, so it cannot be exported.");
        }

        if (!AccStorageUrn.TryParse(storageUrn, out string bucketKey, out string objectKey))
        {
            throw new AccDownloadException(
                "This ACC file's storage location was not in the expected format, so it cannot be exported.");
        }

        AccSignedDownload signed = await client.GetSignedDownloadAsync(bucketKey, objectKey, cancellationToken).ConfigureAwait(false);

        if (!signed.HasSingleUrl)
        {
            // "chunked" is a real, documented status; assembling multi-part
            // chunks was not verified against a live response, so it is
            // refused honestly rather than half-implemented.
            throw new AccDownloadException(
                "This ACC file is stored in multiple parts, which SAUDICO Federate cannot yet download. Export it from a Revit cloud model or a local copy instead.");
        }

        string directory = Path.Combine(cacheRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string fileName = SafeFileName(displayName);
        string destination = Path.Combine(directory, fileName);

        Log.Information(
            "ACC download started for a queued uploaded file (ReportedSizeBytes={Size})", signed.Size);

        try
        {
            await downloader.DownloadToFileAsync(signed.Url!, destination, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(directory);
            throw;
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(directory);
            throw new AccDownloadException("This ACC file could not be downloaded.", ex);
        }

        if (!File.Exists(destination) || new FileInfo(destination).Length == 0)
        {
            TryDeleteDirectory(directory);
            throw new AccDownloadException("This ACC file downloaded as empty and cannot be exported.");
        }

        Log.Information("ACC download completed for a queued uploaded file");
        return destination;
    }

    /// <summary>Strips any path information from an APS-supplied display name so it can never escape the cache directory.</summary>
    private static string SafeFileName(string displayName)
    {
        string name = Path.GetFileName(displayName);

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "AccModel.rvt";
        }

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        if (!name.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
        {
            name += ".rvt";
        }

        return name;
    }

    public static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch
        {
            // Best-effort cleanup only — a leftover temp file must never fail a job.
        }
    }
}
