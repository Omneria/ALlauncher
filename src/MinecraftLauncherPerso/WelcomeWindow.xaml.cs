using System.Windows;
using System.Windows.Input;

namespace MinecraftLauncherPerso;

/// <summary>
/// Assistant de premier lancement (v1.8.0) : affiché une seule fois, quand settings.json n'existait
/// pas encore avant SettingsManager.Load() (voir MainWindow). Un nouvel ami qui rejoint le serveur
/// n'a jusqu'ici aucune indication de ce que fait le launcher au premier démarrage — RAM/dossier de
/// jeu par défaut choisis silencieusement, aucune explication du flux Java -> Forge -> mods -> auth.
/// Volontairement minimal (3 pages) : pas de connexion Microsoft déclenchée depuis cet assistant
/// (évite de dupliquer ce flux), juste une orientation vers "SE CONNECTER"/"JOUER" sur le dashboard.
/// </summary>
public partial class WelcomeWindow : Window
{
    private const int PageCount = 3;

    private int _currentPage;

    /// <summary>Dossier de jeu choisi (éventuellement modifié depuis la valeur par défaut), à
    /// reporter sur LauncherSettings.GameDirectory par l'appelant.</summary>
    public string GameDirectory { get; private set; }

    public WelcomeWindow(string serverName, string defaultGameDirectory)
    {
        InitializeComponent();
        GameDirectory = defaultGameDirectory;
        WelcomeTitleText.Text = $"BIENVENUE SUR {serverName.ToUpperInvariant()}";
        GameDirectoryTextBox.Text = defaultGameDirectory;
        UpdatePageVisibility();
    }

    private void UpdatePageVisibility()
    {
        Page0.Visibility = _currentPage == 0 ? Visibility.Visible : Visibility.Collapsed;
        Page1.Visibility = _currentPage == 1 ? Visibility.Visible : Visibility.Collapsed;
        Page2.Visibility = _currentPage == 2 ? Visibility.Visible : Visibility.Collapsed;

        StepIndicatorText.Text = $"{_currentPage + 1} / {PageCount}";
        PreviousButton.Visibility = _currentPage == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = _currentPage == PageCount - 1 ? "TERMINER" : "SUIVANT";
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage == PageCount - 1)
        {
            Close();
            return;
        }

        _currentPage++;
        UpdatePageVisibility();
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage == 0)
        {
            return;
        }

        _currentPage--;
        UpdatePageVisibility();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            InitialDirectory = GameDirectory,
        };

        if (dialog.ShowDialog(this) == true)
        {
            GameDirectory = dialog.FolderName;
            GameDirectoryTextBox.Text = GameDirectory;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
