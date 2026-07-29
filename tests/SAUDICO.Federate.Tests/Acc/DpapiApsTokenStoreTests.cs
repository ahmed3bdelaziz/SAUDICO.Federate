using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SAUDICO.Federate.ACC.Tokens;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

public sealed class DpapiApsTokenStoreTests : IDisposable
{
    private readonly string path;

    public DpapiApsTokenStoreTests()
    {
        path = Path.Combine(Path.GetTempPath(), "SAUDICO-ACC-Token-" + Guid.NewGuid().ToString("N") + ".dat");
    }

    public void Dispose()
    {
        try { File.Delete(path); } catch { }
        try { File.Delete(path + ".tmp"); } catch { }
    }

    private const string TestRefreshToken = "unit-test-refresh-token-value-should-not-appear-in-plaintext";

    [Fact]
    public async Task RoundTrip_RecoversRefreshToken()
    {
        DpapiApsTokenStore store = new DpapiApsTokenStore(path);
        await store.SaveAsync(new ApsToken { AccessToken = "access", RefreshToken = TestRefreshToken }, CancellationToken.None);

        ApsToken? loaded = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(TestRefreshToken, loaded!.RefreshToken);
        Assert.Equal("", loaded.AccessToken);
        Assert.True(loaded.ExpiresAtUtc <= DateTime.UtcNow);
    }

    [Fact]
    public async Task CorruptData_FailsSafely()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });

        DpapiApsTokenStore store = new DpapiApsTokenStore(path);
        ApsToken? loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Delete_RemovesStorage()
    {
        DpapiApsTokenStore store = new DpapiApsTokenStore(path);
        await store.SaveAsync(new ApsToken { AccessToken = "access", RefreshToken = TestRefreshToken }, CancellationToken.None);
        Assert.True(File.Exists(path));

        await store.DeleteAsync(CancellationToken.None);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task StoredBytes_DoNotContainPlaintextToken()
    {
        DpapiApsTokenStore store = new DpapiApsTokenStore(path);
        await store.SaveAsync(new ApsToken { AccessToken = "access", RefreshToken = TestRefreshToken }, CancellationToken.None);

        byte[] onDisk = File.ReadAllBytes(path);
        string asLatin1 = Encoding.GetEncoding(28591).GetString(onDisk);
        string asUtf8 = Encoding.UTF8.GetString(onDisk);

        Assert.DoesNotContain(TestRefreshToken, asLatin1, StringComparison.Ordinal);
        Assert.DoesNotContain(TestRefreshToken, asUtf8, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingFile_ReturnsNull()
    {
        DpapiApsTokenStore store = new DpapiApsTokenStore(path);
        ApsToken? loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded);
    }
}
