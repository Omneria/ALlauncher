using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Diagnostics;
using MinecraftLauncherPerso.Services.Hardware;
using MinecraftLauncherPerso.Services.ModSync;

namespace MinecraftLauncherPerso;

public partial class SettingsWindow : Window
{
    // Palier d'incrément (et de "snap") du slider RAM fusionné, en Mo.
    private const double RamStepMb = 256;
    private const double ThumbSize = 14;

    private readonly LauncherSettings _settings;
    private readonly IModSyncService _modSyncService;
    private double _ramFloorMb = 512;
    private double _ramCeilingMb = 16384;
    private double _minRamMb;
    private double _maxRamMb;

    /// <summary>True si l'utilisateur a cliqué "Enregistrer" (par opposition à fermer sans sauver).</summary>
    public bool SettingsSaved { get; private set; }

    public SettingsWindow(LauncherSettings settings, IModSyncService modSyncService)
    {
        InitializeComponent();
        _settings = settings;
        _modSyncService = modSyncService;

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
        DesktopNotificationsCheckBox.IsChecked = _settings.DesktopNotificationsEnabled;
        UpdateRollbackButtonState();
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

        _settings.DesktopNotificationsEnabled = DesktopNotificationsCheckBox.IsChecked == true;

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

    /// <summary>
    /// "Réparation rapide" (v1.8.0) : jusqu'ici, un fichier de mod corrompu/supprimé par erreur
    /// n'était détecté qu'au clic sur JOUER (et seulement si un manifest.json existe côté VPS), sans
    /// aucun moyen manuel de le forcer. Ignore le cache ETag (IModSyncService.RepairAsync) pour
    /// retélécharger le modpack complet, même si le serveur affirme que rien n'a changé.
    /// </summary>
    private async void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        RepairButton.IsEnabled = false;
        RollbackButton.IsEnabled = false;
        RepairStatusText.Visibility = Visibility.Visible;
        RepairStatusText.Foreground = (Brush)FindResource("InkDimBrush");
        RepairStatusText.Text = "Réparation en cours...";

        var progress = new Progress<string>(message => RepairStatusText.Text = message);

        // Sauvegarde/restauration d'options.txt (réglages perso du joueur : touches, options vidéo,
        // langue...) autour de la réparation : ni le mode zip ni le mode manifest ne sont censés
        // toucher ce fichier (mods/config uniquement), mais un zip mal formé côté VPS pourrait
        // l'écraser par erreur — filet de sécurité peu coûteux plutôt qu'une hypothèse non vérifiée.
        var optionsPath = Path.Combine(_settings.GameDirectory, "options.txt");
        byte[]? optionsBackup = null;
        try
        {
            if (File.Exists(optionsPath))
            {
                optionsBackup = await File.ReadAllBytesAsync(optionsPath);
            }
        }
        catch (IOException)
        {
            // Best-effort : un échec de lecture ici ne doit pas empêcher la réparation elle-même.
        }

        try
        {
            await _modSyncService.RepairAsync(_settings.ModpackZipUrl, _settings.ModpackManifestUrl, _settings.GameDirectory, progress);

            if (optionsBackup is not null)
            {
                await File.WriteAllBytesAsync(optionsPath, optionsBackup);
            }

            RepairStatusText.Text = "Modpack réparé.";
        }
        catch (Exception ex)
        {
            RepairStatusText.Foreground = (Brush)FindResource("MagentaBrush");
            RepairStatusText.Text = $"Échec de la réparation : {ex.Message}";
        }
        finally
        {
            RepairButton.IsEnabled = true;
            RollbackButton.IsEnabled = true;
            UpdateRollbackButtonState();
        }
    }

    /// <summary>
    /// "Revenir à la version précédente" (v1.9.0) : restaure la sauvegarde d'un cran conservée par
    /// ModSyncService avant la dernière mise à jour appliquée (voir IModSyncService.RollbackAsync).
    /// Bouton désactivé quand aucune sauvegarde n'existe (voir UpdateRollbackButtonState, appelé au
    /// chargement de la fenêtre et après chaque réparation/synchro).
    /// </summary>
    private async void RollbackButton_Click(object sender, RoutedEventArgs e)
    {
        RollbackButton.IsEnabled = false;
        RepairButton.IsEnabled = false;
        RepairStatusText.Visibility = Visibility.Visible;
        RepairStatusText.Foreground = (Brush)FindResource("InkDimBrush");
        RepairStatusText.Text = "Retour à la version précédente...";

        var progress = new Progress<string>(message => RepairStatusText.Text = message);

        try
        {
            await _modSyncService.RollbackAsync(_settings.GameDirectory, progress);
        }
        catch (Exception ex)
        {
            RepairStatusText.Foreground = (Brush)FindResource("MagentaBrush");
            RepairStatusText.Text = $"Échec du retour en arrière : {ex.Message}";
        }
        finally
        {
            RepairButton.IsEnabled = true;
            UpdateRollbackButtonState();
        }
    }

    private void UpdateRollbackButtonState()
    {
        RollbackButton.IsEnabled = _modSyncService.HasRollbackAvailable(_settings.GameDirectory);
    }

    private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
    {
        new LogViewerWindow(_settings.GameDirectory) { Owner = this }.ShowDialog();
    }

    /// <summary>
    /// "Signaler un problème" (v1.9.0) : prépare un rapport prêt à coller sur Discord (version du
    /// launcher, config RAM/serveur, dernière synchro, dernières lignes de launcher.log) plutôt que
    /// de devoir demander à chaque joueur de recopier ces infos à la main quand il demande de l'aide.
    /// </summary>
    private async void ReportProblemButton_Click(object sender, RoutedEventArgs e)
    {
        var report = await BuildProblemReportAsync();

        try
        {
            Clipboard.SetText(report);
            RepairStatusText.Visibility = Visibility.Visible;
            RepairStatusText.Foreground = (Brush)FindResource("InkDimBrush");
            RepairStatusText.Text = "Rapport copié dans le presse-papier — colle-le sur Discord.";
        }
        catch (Exception)
        {
            // Presse-papier verrouillé par un autre process (arrive parfois sous Windows) :
            // l'utilisateur peut toujours ouvrir le visualiseur de logs et copier à la main.
        }
    }

    private async Task<string> BuildProblemReportAsync()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version is null ? "inconnue" : $"{version.Major}.{version.Minor}.{version.Build}";
        var lastSyncedAt = _modSyncService.GetLastSyncedAt(_settings.GameDirectory);

        var lines = new List<string>
        {
            "=== Rapport AL Launcher ===",
            $"Version launcher : {versionText}",
            $"OS : {Environment.OSVersion.VersionString}",
            $"RAM configurée : {_settings.MinRamMb}-{_settings.MaxRamMb} Mo",
            $"Serveur : {_settings.ServerHost}:{_settings.ServerPort}",
            $"Dernière synchro modpack : {(lastSyncedAt is null ? "jamais" : lastSyncedAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))}",
            "",
            "--- Dernières lignes de launcher.log ---",
        };

        try
        {
            if (File.Exists(Logger.LogFilePath))
            {
                var logLines = await File.ReadAllLinesAsync(Logger.LogFilePath);
                lines.AddRange(logLines.TakeLast(30));
            }
            else
            {
                lines.Add("(aucun launcher.log pour l'instant)");
            }
        }
        catch (IOException)
        {
            lines.Add("(launcher.log illisible)");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void LegalButton_Click(object sender, RoutedEventArgs e)
    {
        new LegalWindow { Owner = this }.ShowDialog();
    }
}
