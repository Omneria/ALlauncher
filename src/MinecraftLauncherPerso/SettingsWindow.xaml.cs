using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Hardware;

namespace MinecraftLauncherPerso;

public partial class SettingsWindow : Window
{
    // Palier d'incrément (et de "snap") du slider RAM fusionné, en Mo.
    private const double RamStepMb = 256;
    private const double ThumbSize = 14;

    private readonly LauncherSettings _settings;
    private double _ramFloorMb = 512;
    private double _ramCeilingMb = 16384;
    private double _minRamMb;
    private double _maxRamMb;

    /// <summary>True si l'utilisateur a cliqué "Enregistrer" (par opposition à fermer sans sauver).</summary>
    public bool SettingsSaved { get; private set; }

    public SettingsWindow(LauncherSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        // Borne haute du slider RAM = RAM physique réelle de la machine (au lieu d'un plafond
        // arbitraire) : impossible de configurer plus que ce que la machine peut physiquement
        // fournir. Repli sur 16384 Mo si indétectable (même valeur de repli que
        // LauncherSettings.RecommendMaxRamMb).
        _ramCeilingMb = SystemInfo.GetTotalPhysicalMemoryMb() ?? 16384;

        // Clamp au cas où settings.json contiendrait une valeur au-delà de cette RAM détectée
        // (ex. settings copiés depuis une autre machine plus puissante).
        _minRamMb = Math.Clamp(_settings.MinRamMb, _ramFloorMb, _ramCeilingMb);
        _maxRamMb = Math.Clamp(_settings.MaxRamMb, _ramFloorMb, _ramCeilingMb);

        ScreenWidthTextBox.Text = _settings.ScreenWidth.ToString();
        ScreenHeightTextBox.Text = _settings.ScreenHeight.ToString();
        GameDirectoryTextBox.Text = _settings.GameDirectory;
    }

    // Un seul slider à deux poignées (au lieu de deux sliders min/max distincts, remplacé v1.8.0) :
    // positionne les deux poignées + la portion pleine entre elles selon _minRamMb/_maxRamMb.
    // Ne peut se faire qu'une fois la largeur du Canvas connue (0 tant qu'aucun layout n'a eu
    // lieu), d'où l'appel depuis SizeChanged plutôt que juste dans le constructeur.
    private void RamRangeCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateRamRangeVisual();

    private double ValueToX(double valueMb)
    {
        var trackWidth = RamRangeCanvas.ActualWidth - ThumbSize;
        if (trackWidth <= 0 || _ramCeilingMb <= _ramFloorMb)
        {
            return 0;
        }

        return (valueMb - _ramFloorMb) / (_ramCeilingMb - _ramFloorMb) * trackWidth;
    }

    private double XToValue(double x)
    {
        var trackWidth = RamRangeCanvas.ActualWidth - ThumbSize;
        if (trackWidth <= 0)
        {
            return _ramFloorMb;
        }

        var raw = _ramFloorMb + x / trackWidth * (_ramCeilingMb - _ramFloorMb);
        var snapped = _ramFloorMb + Math.Round((raw - _ramFloorMb) / RamStepMb) * RamStepMb;
        return Math.Clamp(snapped, _ramFloorMb, _ramCeilingMb);
    }

    private void UpdateRamRangeVisual()
    {
        var minX = ValueToX(_minRamMb);
        var maxX = ValueToX(_maxRamMb);

        Canvas.SetLeft(RamMinThumb, minX);
        Canvas.SetLeft(RamMaxThumb, maxX);

        var thumbCenter = ThumbSize / 2;
        Canvas.SetLeft(RamRangeFill, minX + thumbCenter);
        RamRangeFill.Width = Math.Max(0, maxX - minX);

        RamRangeValueText.Text = $"{(int)_minRamMb} – {(int)_maxRamMb} Mo";
    }

    // Empêche par construction RAM min > RAM max : une poignée ne peut pas dépasser l'autre,
    // au lieu de valider seulement à l'enregistrement.
    private void RamMinThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var proposedX = Math.Clamp(Canvas.GetLeft(RamMinThumb) + e.HorizontalChange, 0, Canvas.GetLeft(RamMaxThumb));
        _minRamMb = Math.Min(XToValue(proposedX), _maxRamMb);
        UpdateRamRangeVisual();
    }

    private void RamMaxThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var trackWidth = RamRangeCanvas.ActualWidth - ThumbSize;
        var proposedX = Math.Clamp(Canvas.GetLeft(RamMaxThumb) + e.HorizontalChange, Canvas.GetLeft(RamMinThumb), trackWidth);
        _maxRamMb = Math.Max(XToValue(proposedX), _minRamMb);
        UpdateRamRangeVisual();
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
        // RAM min/max n'a plus besoin d'être validée ici : le slider double poignée l'empêche déjà
        // par construction (RamMinThumb_DragDelta/RamMaxThumb_DragDelta ci-dessus), contrairement
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

        _settings.MinRamMb = (int)_minRamMb;
        _settings.MaxRamMb = (int)_maxRamMb;
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
