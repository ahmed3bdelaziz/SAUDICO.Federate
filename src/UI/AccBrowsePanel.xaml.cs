using System.Windows;
using System.Windows.Controls;

namespace SAUDICO.Federate.UI;

/// <summary>
/// Read-only ACC browser view. Code-behind contains no browsing logic —
/// everything lives in <see cref="AccBrowseViewModel"/>; the only glue here
/// is translating a list double-click into the existing OpenCommand.
/// </summary>
public partial class AccBrowsePanel : UserControl
{
    public AccBrowsePanel(AccBrowseViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnItemDoubleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccBrowseViewModel viewModel && viewModel.OpenCommand.CanExecute(null))
        {
            viewModel.OpenCommand.Execute(null);
        }
    }
}
