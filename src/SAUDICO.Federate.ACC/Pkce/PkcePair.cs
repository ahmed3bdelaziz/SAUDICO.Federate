namespace SAUDICO.Federate.ACC.Pkce;

/// <summary>
/// A single-use PKCE verifier/challenge pair. Never persisted, never logged,
/// never reused across login attempts.
/// </summary>
public sealed class PkcePair
{
    public string CodeVerifier { get; set; } = "";
    public string CodeChallenge { get; set; } = "";
    public string CodeChallengeMethod { get; set; } = "S256";
}
