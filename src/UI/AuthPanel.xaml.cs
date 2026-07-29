using System.Windows.Controls;
using SAUDICO.Federate.ACC.Authentication;

namespace SAUDICO.Federate.UI;

/// <summary>
/// Isolated APS sign-in panel. Contains no authentication business logic —
/// everything happens in <see cref="AuthViewModel"/> and SAUDICO.Federate.ACC.
/// Not yet embedded into the main Federation Manager window; that wiring is
/// deferred to the future ACC browser milestone.
/// </summary>
public partial class AuthPanel : UserControl
{
    public AuthPanel(IApsAuthenticationService authenticationService)
    {
        InitializeComponent();
        DataContext = new AuthViewModel(authenticationService);
    }
}
