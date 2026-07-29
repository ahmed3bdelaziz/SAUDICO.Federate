namespace SAUDICO.Federate.ACC.Pkce;

public interface IPkceService
{
    PkcePair Create();
    string GenerateState();
}
