using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace MinecraftLauncherPerso.Controls;

public partial class LegalPage : UserControl
{
    public LegalPage()
    {
        InitializeComponent();
    }

    /// <summary>Demande de retour à la page précédente.</summary>
    public event EventHandler? BackRequested;

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
