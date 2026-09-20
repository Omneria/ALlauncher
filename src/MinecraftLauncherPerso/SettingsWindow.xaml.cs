using System.Windows;
using System.Windows.Input;
using MinecraftLauncherPerso.Models;

namespace MinecraftLauncherPerso;

public partial class SettingsWindow : Window
{
    private readonly LauncherSettings _settings;

    /// <summary>True si l'utilisateur a cliqué "Enregistrer" (par opposition à fermer sans sauver).</summary>
    public bool SettingsSaved { get; private set; }

    public SettingsWindow(LauncherSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        MinRamTextBox.Text = _settings.MinRamMb.ToString();
        MaxRamTextBox.Text = _settings.MaxRamMb.ToString();
        ScreenWidthTextBox.Text = _settings.ScreenWidth.ToString();
        ScreenHeightTextBox.Text = _settings.ScreenHeight.ToString();
        GameDirectoryTextBox.Text = _settings.GameDirectory;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            InitialDirectory = GameDirectoryTextBox.Text,
        };

        if (dialog.ShowDialog(this) == true)
        {
            GameDirectoryTextBox.Text = dialog.FolderName;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(MinRamTextBox.Text, out var minRam))
        {
            _settings.MinRamMb = minRam;
        }

        if (int.TryParse(MaxRamTextBox.Text, out var maxRam))
        {
            _settings.MaxRamMb = maxRam;
        }

        if (int.TryParse(ScreenWidthTextBox.Text, out var width))
        {
            _settings.ScreenWidth = width;
        }

        if (int.TryParse(ScreenHeightTextBox.Text, out var height))
        {
            _settings.ScreenHeight = height;
        }

        if (!string.IsNullOrWhiteSpace(GameDirectoryTextBox.Text))
        {
            _settings.GameDirectory = GameDirectoryTextBox.Text;
        }

        SettingsSaved = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void LegalButton_Click(object sender, RoutedEventArgs e)
    {
        new LegalWindow { Owner = this }.ShowDialog();
    }
}
