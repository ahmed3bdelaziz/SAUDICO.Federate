using System.Windows;
using System.Windows.Controls;

namespace SAUDICO.Federate.UI;

/// <summary>
/// Temporary diagnostic shell used to isolate the Revit 2025 "Add ACC Models"
/// crash from the real ACC browser UI. Deliberately constructs no
/// authentication, configuration, DPAPI, HTTP, or callback-listener type —
/// plain WPF only. Remove this file and revert the "Add ACC Models" wiring
/// once the real crash cause is confirmed or ruled out.
/// </summary>
public sealed class DiagnosticShellWindow : Window
{
    public DiagnosticShellWindow()
    {
        LogMarker("DiagnosticWindowConstructorEntered");

        Title = "SAUDICO Federate - ACC Diagnostic Shell";
        Width = 360;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        TextBlock text = new TextBlock
        {
            Text = "ACC Browser diagnostic shell",
            Margin = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        Button closeButton = new Button
        {
            Content = "Close",
            Width = 80,
            Margin = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        closeButton.Click += (_, _) => Close();

        StackPanel panel = new StackPanel();
        panel.Children.Add(text);
        panel.Children.Add(closeButton);
        Content = panel;

        Loaded += (_, _) => LogMarker("WindowLoaded");
        ContentRendered += (_, _) => LogMarker("WindowContentRendered");
        Closed += (_, _) => LogMarker("WindowClosed");

        LogMarker("DiagnosticWindowConstructorCompleted");
    }

    private static void LogMarker(string marker)
    {
        try
        {
            Serilog.Log.Information("ACC diagnostic shell marker: {Marker}", marker);
        }
        catch
        {
            // Logging must never be the reason the diagnostic shell fails.
        }
    }
}
