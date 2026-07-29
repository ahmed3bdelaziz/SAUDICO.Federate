using System;
using System.Security.Cryptography;
using System.Text;

namespace SAUDICO.Federate.ACC.Pkce;

/// <summary>
/// RFC 7636 PKCE generator. Verifier is 32 cryptographically random bytes,
/// Base64URL-encoded without padding (43 characters, well within the
/// officially permitted 43-128 character range and character set).
/// Challenge is Base64URL(SHA-256(ASCII(verifier))), per Autodesk's
/// "Generate the Code Challenge" guidance.
/// </summary>
public sealed class PkceService : IPkceService
{
    public PkcePair Create()
    {
        string verifier = Base64Url(RandomBytes(32));
        string challenge = ComputeS256Challenge(verifier);

        return new PkcePair
        {
            CodeVerifier = verifier,
            CodeChallenge = challenge,
            CodeChallengeMethod = "S256"
        };
    }

    public string GenerateState()
    {
        return Base64Url(RandomBytes(32));
    }

    /// <summary>Exposed as a static, testable operation so unit tests can verify a known RFC 7636 vector.</summary>
    public static string ComputeS256Challenge(string codeVerifier)
    {
        return Base64Url(Sha256(Encoding.ASCII.GetBytes(codeVerifier)));
    }

    private static byte[] RandomBytes(int count)
    {
        byte[] bytes = new byte[count];
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return bytes;
    }

    private static byte[] Sha256(byte[] data)
    {
        using SHA256 sha256 = SHA256.Create();
        return sha256.ComputeHash(data);
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
