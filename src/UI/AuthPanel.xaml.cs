using System.Windows.Controls;

namespace SAUDICO.Federate.UI;

/// <summary>
/// Isolated APS sign-in panel. Contains no authentication business logic —
/// everything happens in <see cref="AuthViewModel"/> and SAUDICO.Federate.ACC.
/// Not yet embedded into the main Federation Manager window; that wiring is
/// deferred to the future ACC browser milestone.
/// </summary>
public partial class AuthPanel : UserControl
{
    public AuthPanel(AuthViewModel viewModel)
    {
        LogMarker("AuthPanelConstructorEntered");

        LogMarker("BeforeInitializeComponent");
        InitializeComponent();
        LogMarker("AfterInitializeComponent");

        DataContext = viewModel;
        LogMarker("DataContextAssigned");

        LogMarker("AuthPanelConstructorCompleted");
    }

    private static void LogMarker(string marker)
    {
        try
        {
            Serilog.Log.Information("ACC browser lifecycle marker: {Marker}", marker);
        }
        catch
        {
            // Logging must never be the reason the panel fails to load.
        }
    }
}
