using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Hardware;

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

        // Borne haute des sliders RAM = RAM physique réelle de la machine (au lieu d'un plafond
        // arbitraire) : impossible de configurer plus que ce que la machine peut physiquement
        // fournir. Repli sur 16384 Mo si indétectable (même valeur de repli que
        // LauncherSettings.RecommendMaxRamMb).
        var totalRamMb = SystemInfo.GetTotalPhysicalMemoryMb() ?? 16384;
        MinRamSlider.Maximum = totalRamMb;
        MaxRamSlider.Maximum = totalRamMb;

        // Clamp au cas où settings.json contiendrait une valeur au-delà de cette RAM détectée
        // (ex. settings copiés depuis une autre machine plus puissante).
        MinRamSlider.Value = Math.Clamp(_settings.MinRamMb, MinRamSlider.Minimum, totalRamMb);
        MaxRamSlider.Value = Math.Clamp(_settings.MaxRamMb, MaxRamSlider.Minimum, totalRamMb);

        ScreenWidthTextBox.Text = _settings.ScreenWidth.ToString();
        ScreenHeightTextBox.Text = _settings.ScreenHeight.ToString();
        GameDirectoryTextBox.Text = _settings.GameDirectory;
    }

    // Empêche par construction RAM min > RAM max (au lieu de valider seulement à l'enregistrement) :
    // pousse l'autre slider plutôt que de laisser l'utilisateur configurer une plage invalide.
    private void MinRamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxRamSlider is null) // se déclenche aussi pendant InitializeComponent, avant que MaxRamSlider existe
        {
            return;
        }

        if (MinRamSlider.Value > MaxRamSlider.Value)
        {
            MaxRamSlider.Value = MinRamSlider.Value;
        }

        MinRamValueText.Text = $"{(int)MinRamSlider.Value} Mo";
    }

    private void MaxRamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MinRamSlider is null)
        {
            return;
        }

        if (MaxRamSlider.Value < MinRamSlider.Value)
        {
            MinRamSlider.Value = MaxRamSlider.Value;
        }

        MaxRamValueText.Text = $"{(int)MaxRamSlider.Value} Mo";
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
        // RAM min/max n'a plus besoin d'être validée ici : les sliders l'empêchent déjà par
        // construction (MinRamSlider_ValueChanged/MaxRamSlider_ValueChanged ci-dessus), contrairement
        // aux champs texte libres restants ci-dessous.
        ValidationErrorText.Visibility = Visibility.Collapsed;
        var errors = new List<string>();

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

        _settings.MinRamMb = (int)MinRamSlider.Value;
        _settings.MaxRamMb = (int)MaxRamSlider.Value;
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
