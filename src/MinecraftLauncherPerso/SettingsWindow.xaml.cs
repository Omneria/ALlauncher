using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        ValidationErrorText.Visibility = Visibility.Collapsed;
        var errors = new List<string>();

        var minRamValid = int.TryParse(MinRamTextBox.Text, out var minRam) && minRam > 0;
        SetFieldValid(MinRamTextBox, minRamValid);
        if (!minRamValid)
        {
            errors.Add("RAM minimum : entier positif attendu.");
        }

        var maxRamValid = int.TryParse(MaxRamTextBox.Text, out var maxRam) && maxRam > 0;
        SetFieldValid(MaxRamTextBox, maxRamValid);
        if (!maxRamValid)
        {
            errors.Add("RAM maximum : entier positif attendu.");
        }

        if (minRamValid && maxRamValid && minRam > maxRam)
        {
            SetFieldValid(MinRamTextBox, false);
            SetFieldValid(MaxRamTextBox, false);
            errors.Add("La RAM minimum ne peut pas dépasser la RAM maximum.");
        }

        var widthValid = int.TryParse(ScreenWidthTextBox.Text, out var width) && width >= 0;
        SetFieldValid(ScreenWidthTextBox, widthValid);
        if (!widthValid)
        {
            errors.Add("Largeur : entier positif ou 0 (automatique) attendu.");
        }

        var heightValid = int.TryParse(ScreenHeightTextBox.Text, out var height) && height >= 0;
        SetFieldValid(ScreenHeightTextBox, heightValid);
        if (!heightValid)
        {
            errors.Add("Hauteur : entier positif ou 0 (automatique) attendu.");
        }

        if (errors.Count > 0)
        {
            ValidationErrorText.Text = string.Join(" ", errors);
            ValidationErrorText.Visibility = Visibility.Visible;
            return;
        }

        _settings.MinRamMb = minRam;
        _settings.MaxRamMb = maxRam;
        _settings.ScreenWidth = width;
        _settings.ScreenHeight = height;

        if (!string.IsNullOrWhiteSpace(GameDirectoryTextBox.Text))
        {
            _settings.GameDirectory = GameDirectoryTextBox.Text;
        }

        SettingsSaved = true;
        Close();
    }

    private void SetFieldValid(TextBox box, bool valid)
    {
        if (valid)
        {
            box.ClearValue(TextBox.BorderBrushProperty);
        }
        else
        {
            box.BorderBrush = (Brush)FindResource("MagentaBrush");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void LegalButton_Click(object sender, RoutedEventArgs e)
    {
        new LegalWindow { Owner = this }.ShowDialog();
    }
}
