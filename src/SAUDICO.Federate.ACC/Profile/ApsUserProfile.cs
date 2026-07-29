namespace SAUDICO.Federate.ACC.Profile;

public sealed class ApsUserProfile
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public string? ProfileImageUri { get; set; }
}
