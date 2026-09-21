using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MinecraftLauncherPerso.Services.Diagnostics;

namespace MinecraftLauncherPerso;

/// <summary>
/// Visualiseur de logs intégré (v1.8.0, onglet Paramètres) : jusqu'ici il fallait aller fouiller
/// %AppData%/MinecraftLauncherPerso à la main (launcher.log) ou le dossier logs/ du jeu pour aider
/// à diagnostiquer un problème. Affiche les deux au même endroit, avec une recherche texte simple
/// (filtre par ligne) et un bouton "copier" pour préparer un extrait à coller sur Discord quand un
/// joueur demande de l'aide.
/// </summary>
public partial class LogViewerWindow : Window
{
    private readonly string _gameLogPath;
    private string[] _currentLines = [];

    public LogViewerWindow(string gameDirectory)
    {
        InitializeComponent();
        _gameLogPath = Path.Combine(gameDirectory, "logs", "latest.log");
        ShowLauncherLog();
    }

    private void LauncherLogButton_Click(object sender, RoutedEventArgs e) => ShowLauncherLog();

    private void GameLogButton_Click(object sender, RoutedEventArgs e) => ShowGameLog();

    private void ShowLauncherLog()
    {
        SetActiveTab(LauncherLogButton, GameLogButton);
        LoadLog(Logger.LogFilePath, "Aucun journal launcher pour l'instant.");
    }

    private void ShowGameLog()
    {
        SetActiveTab(GameLogButton, LauncherLogButton);
        LoadLog(_gameLogPath, "Aucun journal de jeu pour l'instant (pas encore lancé, ou dossier de jeu introuvable).");
    }

    private void SetActiveTab(System.Windows.Controls.Button active, System.Windows.Controls.Button inactive)
    {
        active.Foreground = (Brush)FindResource("CyanBrush");
        inactive.Foreground = (Brush)FindResource("InkDimBrush");
    }

    private void LoadLog(string path, string emptyMessage)
    {
        try
        {
            _currentLines = File.Exists(path) ? File.ReadAllLines(path) : [];
        }
        catch (IOException)
        {
            // Fichier verrouillé par un autre process (ex. le jeu tourne encore et écrit dedans) :
            // pas bloquant, on affiche juste vide plutôt que de planter le visualiseur.
            _currentLines = [];
        }

        if (_currentLines.Length == 0)
        {
            LogTextBox.Text = emptyMessage;
            return;
        }

        ApplyFilter();
    }

    private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var filter = SearchTextBox.Text;
        var lines = string.IsNullOrWhiteSpace(filter)
            ? _currentLines
            : _currentLines.Where(l => l.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();

        LogTextBox.Text = lines.Length == 0
            ? "Aucune ligne ne correspond à la recherche."
            : string.Join(Environment.NewLine, lines);
        LogTextBox.ScrollToEnd();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(LogTextBox.Text);
        }
        catch (Exception)
        {
            // Presse-papier verrouillé par un autre process (arrive parfois sous Windows) :
            // sans conséquence grave, l'utilisateur peut toujours sélectionner/copier manuellement.
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
