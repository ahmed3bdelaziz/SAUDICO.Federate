using System.Text.RegularExpressions;
using SAUDICO.Federate.ACC.Pkce;
using Xunit;

namespace SAUDICO.Federate.Tests.Acc;

public sealed class PkceServiceTests
{
    private readonly PkceService service = new PkceService();

    [Fact]
    public void Verifier_HasValidLengthAndCharacterSet()
    {
        PkcePair pair = service.Create();

        Assert.InRange(pair.CodeVerifier.Length, 43, 128);
        Assert.Matches(new Regex("^[A-Za-z0-9\\-._~]+$"), pair.CodeVerifier);
    }

    [Fact]
    public void KnownVector_ProducesExpectedS256Challenge()
    {
        // RFC 7636 Appendix B example verifier/challenge pair.
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        string challenge = PkceService.ComputeS256Challenge(verifier);

        Assert.Equal(expectedChallenge, challenge);
    }

    [Fact]
    public void State_IsRandomEachTime()
    {
        string first = service.GenerateState();
        string second = service.GenerateState();

        Assert.NotEqual(first, second);
        Assert.True(first.Length >= 32);
    }

    [Fact]
    public void CodeChallengeMethod_IsS256()
    {
        PkcePair pair = service.Create();
        Assert.Equal("S256", pair.CodeChallengeMethod);
    }
}
