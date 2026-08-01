using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.DataManagement;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

public sealed class AccStorageUrnTests
{
    [Fact]
    public void TryParse_ValidUrn_SplitsBucketAndObjectKey()
    {
        bool ok = AccStorageUrn.TryParse(
            "urn:adsk.objects:os.object:wip.dm.prod/abc123.rvt", out string bucket, out string obj);

        Assert.True(ok);
        Assert.Equal("wip.dm.prod", bucket);
        Assert.Equal("abc123.rvt", obj);
    }

    [Fact]
    public void TryParse_ObjectKeyContainingSlashes_OnlyFirstSeparatorIsTheBucketBoundary()
    {
        bool ok = AccStorageUrn.TryParse(
            "urn:adsk.objects:os.object:wip.dm.prod/folder/sub/model.rvt", out string bucket, out string obj);

        Assert.True(ok);
        Assert.Equal("wip.dm.prod", bucket);
        Assert.Equal("folder/sub/model.rvt", obj);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("urn:adsk.objects:os.object:")]
    [InlineData("urn:adsk.objects:os.object:onlybucket")]
    [InlineData("urn:adsk.objects:os.object:bucket/")]
    [InlineData("https://example.com/file.rvt")]
    public void TryParse_InvalidUrn_ReturnsFalse(string? urn)
    {
        Assert.False(AccStorageUrn.TryParse(urn, out _, out _));
    }
}

public sealed class AccSignedDownloadTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("fallback")]
    [InlineData("COMPLETE")]
    public void HasSingleUrl_CompleteOrFallbackWithUrl_IsTrue(string status)
    {
        AccSignedDownload signed = new AccSignedDownload { Status = status, Url = "https://s3.example/x" };

        Assert.True(signed.HasSingleUrl);
    }

    [Fact]
    public void HasSingleUrl_ChunkedStatus_IsFalse_SoChunkedDownloadsAreRefusedNotGuessed()
    {
        AccSignedDownload signed = new AccSignedDownload { Status = "chunked", Url = null };

        Assert.False(signed.HasSingleUrl);
    }

    [Fact]
    public void HasSingleUrl_CompleteButNoUrl_IsFalse()
    {
        AccSignedDownload signed = new AccSignedDownload { Status = "complete", Url = null };

        Assert.False(signed.HasSingleUrl);
    }
}

/// <summary>Records what the downloader asked for, without any real HTTP.</summary>
internal sealed class FakeDownloadClient : IAccDataManagementClient
{
    public string? StorageUrn { get; set; } = "urn:adsk.objects:os.object:wip.dm.prod/model.rvt";
    public AccSignedDownload Signed { get; set; } = new AccSignedDownload { Status = "complete", Url = "https://s3.example/signed", Size = 10 };
    public string? RequestedBucket { get; private set; }
    public string? RequestedObjectKey { get; private set; }

    public Task<string?> GetVersionStorageUrnAsync(string projectId, string versionId, CancellationToken ct) =>
        Task.FromResult(StorageUrn);

    public Task<AccSignedDownload> GetSignedDownloadAsync(string bucketKey, string objectKey, CancellationToken ct)
    {
        RequestedBucket = bucketKey;
        RequestedObjectKey = objectKey;
        return Task.FromResult(Signed);
    }

    public Task<IReadOnlyList<AccBrowseNode>> GetHubsAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<AccBrowseNode>> GetProjectsAsync(string h, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<AccBrowseNode>> GetTopFoldersAsync(string h, string p, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<AccBrowseNode>> GetFolderContentsAsync(string p, string f, CancellationToken ct) => throw new NotSupportedException();
    public Task<AccSearchOutcome> SearchFolderRecursiveAsync(string p, string f, CancellationToken ct) => throw new NotSupportedException();
    public Task<AccSearchOutcome> SearchProjectAsync(string h, string p, CancellationToken ct) => throw new NotSupportedException();
}

internal sealed class FakeBinaryDownloader : IAccBinaryDownloader
{
    private readonly byte[] payload;
    public string? RequestedUrl { get; private set; }

    public FakeBinaryDownloader(byte[]? payload = null) => this.payload = payload ?? new byte[] { 1, 2, 3, 4 };

    public async Task DownloadToFileAsync(string url, string destinationPath, IProgress<long>? progress, CancellationToken ct)
    {
        RequestedUrl = url;
        await File.WriteAllBytesAsync(destinationPath, payload, ct);
        progress?.Report(payload.Length);
    }
}

public sealed class AccUploadedFileDownloaderTests : IDisposable
{
    private readonly string cacheRoot;

    public AccUploadedFileDownloaderTests()
    {
        cacheRoot = Path.Combine(Path.GetTempPath(), "SAUDICO-AccDownloadTests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose() => AccUploadedFileDownloader.TryDeleteDirectory(cacheRoot);

    [Fact]
    public async Task DownloadAsync_HappyPath_WritesFileAndUsesParsedBucketAndSignedUrl()
    {
        FakeDownloadClient client = new();
        FakeBinaryDownloader binary = new();
        AccUploadedFileDownloader downloader = new(client, binary);

        string path = await downloader.DownloadAsync("proj", "ver", "Tower.rvt", cacheRoot, null, CancellationToken.None);

        Assert.True(File.Exists(path));
        Assert.Equal("Tower.rvt", Path.GetFileName(path));
        Assert.Equal("wip.dm.prod", client.RequestedBucket);
        Assert.Equal("model.rvt", client.RequestedObjectKey);
        Assert.Equal("https://s3.example/signed", binary.RequestedUrl);
    }

    [Fact]
    public async Task DownloadAsync_NoStorageRelationship_ThrowsSafeMessage()
    {
        FakeDownloadClient client = new() { StorageUrn = null };
        AccUploadedFileDownloader downloader = new(client, new FakeBinaryDownloader());

        AccDownloadException ex = await Assert.ThrowsAsync<AccDownloadException>(
            () => downloader.DownloadAsync("proj", "ver", "Tower.rvt", cacheRoot, null, CancellationToken.None));

        Assert.Contains("no downloadable storage location", ex.Message);
    }

    [Fact]
    public async Task DownloadAsync_ChunkedStatus_IsRefusedWithClearMessage()
    {
        FakeDownloadClient client = new() { Signed = new AccSignedDownload { Status = "chunked", Url = null } };
        AccUploadedFileDownloader downloader = new(client, new FakeBinaryDownloader());

        AccDownloadException ex = await Assert.ThrowsAsync<AccDownloadException>(
            () => downloader.DownloadAsync("proj", "ver", "Tower.rvt", cacheRoot, null, CancellationToken.None));

        Assert.Contains("multiple parts", ex.Message);
    }

    [Fact]
    public async Task DownloadAsync_EmptyDownload_FailsAndCleansUp()
    {
        FakeDownloadClient client = new();
        AccUploadedFileDownloader downloader = new(client, new FakeBinaryDownloader(Array.Empty<byte>()));

        await Assert.ThrowsAsync<AccDownloadException>(
            () => downloader.DownloadAsync("proj", "ver", "Tower.rvt", cacheRoot, null, CancellationToken.None));

        Assert.True(!Directory.Exists(cacheRoot) || Directory.GetDirectories(cacheRoot).Length == 0);
    }

    [Theory]
    [InlineData("../../escape.rvt", "escape.rvt")]
    [InlineData("C:\\somewhere\\Tower.rvt", "Tower.rvt")]
    [InlineData("NoExtension", "NoExtension.rvt")]
    public async Task DownloadAsync_DisplayNameCanNeverEscapeTheCacheDirectory(string displayName, string expectedFileName)
    {
        AccUploadedFileDownloader downloader = new(new FakeDownloadClient(), new FakeBinaryDownloader());

        string path = await downloader.DownloadAsync("proj", "ver", displayName, cacheRoot, null, CancellationToken.None);

        Assert.Equal(expectedFileName, Path.GetFileName(path));
        Assert.StartsWith(Path.GetFullPath(cacheRoot), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class HttpAccBinaryDownloaderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly byte[] body;
        public StubHandler(byte[] body) => this.body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Null(request.Headers.Authorization); // a signed S3 URL must never carry a bearer token
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    [Fact]
    public async Task DownloadToFileAsync_StreamsBodyToDisk_WithoutAuthorizationHeader()
    {
        byte[] payload = { 9, 8, 7, 6, 5 };
        string path = Path.Combine(Path.GetTempPath(), "SAUDICO-dl-" + Guid.NewGuid().ToString("N") + ".rvt");

        try
        {
            HttpAccBinaryDownloader downloader = new(new HttpClient(new StubHandler(payload)));
            await downloader.DownloadToFileAsync("https://s3.example/signed", path, null, CancellationToken.None);

            Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
