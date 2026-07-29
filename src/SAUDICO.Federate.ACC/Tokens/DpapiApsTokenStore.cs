using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SAUDICO.Federate.ACC.Tokens;

/// <summary>
/// Persists only the refresh token, DPAPI-protected with
/// <see cref="DataProtectionScope.CurrentUser"/> so another Windows user
/// account cannot decrypt it. Stored at
/// %LOCALAPPDATA%\SAUDICO\Federate\Auth\aps-token.dat. Never stores the
/// access token or any plaintext.
/// </summary>
public sealed class DpapiApsTokenStore : IApsTokenStore
{
    private sealed class PersistedToken
    {
        public string RefreshToken { get; set; } = "";
        public DateTime SavedAtUtc { get; set; }
    }

    private readonly string path;

    public DpapiApsTokenStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SAUDICO", "Federate", "Auth", "aps-token.dat"))
    {
    }

    public DpapiApsTokenStore(string path)
    {
        this.path = path;
    }

    public Task SaveAsync(ApsToken token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            return Task.CompletedTask;
        }

        PersistedToken persisted = new PersistedToken
        {
            RefreshToken = token.RefreshToken!,
            SavedAtUtc = DateTime.UtcNow
        };

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(persisted);
        byte[] protectedBytes = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        File.WriteAllBytes(temp, protectedBytes);

        if (File.Exists(path))
        {
            File.Replace(temp, path, null);
        }
        else
        {
            File.Move(temp, path);
        }

        return Task.CompletedTask;
    }

    public Task<ApsToken?> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(path))
        {
            return Task.FromResult<ApsToken?>(null);
        }

        try
        {
            byte[] protectedBytes = File.ReadAllBytes(path);
            byte[] plaintext = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            PersistedToken? persisted = JsonSerializer.Deserialize<PersistedToken>(plaintext);

            if (persisted == null || string.IsNullOrEmpty(persisted.RefreshToken))
            {
                return Task.FromResult<ApsToken?>(null);
            }

            return Task.FromResult<ApsToken?>(new ApsToken
            {
                AccessToken = "",
                RefreshToken = persisted.RefreshToken,
                ExpiresAtUtc = DateTime.MinValue
            });
        }
        catch (CryptographicException)
        {
            return Task.FromResult<ApsToken?>(null);
        }
        catch (JsonException)
        {
            return Task.FromResult<ApsToken?>(null);
        }
        catch (IOException)
        {
            return Task.FromResult<ApsToken?>(null);
        }
    }

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort: logout must still complete locally.
        }

        return Task.CompletedTask;
    }
}
