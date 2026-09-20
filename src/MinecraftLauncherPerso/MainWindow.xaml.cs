using System.Windows;
using System.Windows.Input;
using CmlLib.Core;
using CmlLib.Core.ProcessBuilder;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Auth;
using MinecraftLauncherPerso.Services.Configuration;
using MinecraftLauncherPerso.Services.Forge;
using MinecraftLauncherPerso.Services.Java;
using MinecraftLauncherPerso.Services.Launch;
using MinecraftLauncherPerso.Services.ModSync;

namespace MinecraftLauncherPerso;

public partial class MainWindow : Window
{
    private readonly IJavaManager _javaManager;
    private readonly IForgeManager _forgeManager;
    private readonly IModSyncService _modSyncService;
    private readonly IAuthService _authService;
    private readonly IGameLauncher _gameLauncher;
    private readonly SettingsManager _settingsManager;
    private LauncherSettings _settings;

    // Référence gardée en vie pour toute la durée de la partie : sans elle, le process/wrapper
    // serait éligible au GC et les événements de sortie du jeu s'arrêteraient.
    private ProcessWrapper? _activeGame;

    public MainWindow()
    {
        InitializeComponent();

        _settingsManager = new SettingsManager();
        _settings = _settingsManager.Load();

        _javaManager = new JavaManager();
        _forgeManager = new ForgeManager();
        _modSyncService = new ModSyncService();
        _authService = new MicrosoftAuthService(_settings.MicrosoftClientId);
        _gameLauncher = new GameLauncher();

        MaxRamTextBox.Text = _settings.MaxRamMb.ToString();
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        PlayButton.IsEnabled = false;
        StatusLogTextBox.Clear();
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = 0;

        if (int.TryParse(MaxRamTextBox.Text, out var maxRamMb))
        {
            _settings.MaxRamMb = maxRamMb;
            _settingsManager.Save(_settings);
        }

        try
        {
            // 1. Java 8 : seule étape avec une progression chiffrée (téléchargement), pilote la barre.
            var javaProgress = new Progress<JavaSetupProgress>(report =>
            {
                ProgressBar.Value = report.PercentComplete;
                AppendLog(report.Message);
            });
            var javaPath = await _javaManager.EnsureJava8Async(javaProgress);
            AppendLog($"Java 8 prêt : {javaPath}");

            // Les étapes suivantes ne rapportent que du texte (pas de fraction) : barre indéterminée.
            ProgressBar.IsIndeterminate = true;

            var minecraftPath = new MinecraftPath(_settings.GameDirectory);
            var launcher = new MinecraftLauncher(minecraftPath);

            // 2. Forge
            var forgeProgress = new Progress<string>(AppendLog);
            var versionId = await _forgeManager.EnsureForgeInstalledAsync(
                launcher, _settings.MinecraftVersion, _settings.ForgeVersion, forgeProgress);
            AppendLog($"Forge prêt : {versionId}");

            // 3. Synchronisation mods/config depuis le VPS
            var syncProgress = new Progress<string>(AppendLog);
            await _modSyncService.SyncAsync(_settings.ModpackZipUrl, _settings.GameDirectory, syncProgress);

            // 4. Authentification Microsoft directe (Xbox Live -> XSTS -> Minecraft)
            var authProgress = new Progress<string>(AppendLog);
            var session = await _authService.GetActiveSessionAsync(authProgress);
            AppendLog($"Connecté en tant que {session.Username}.");

            // 5. Verrouille la liste multijoueur sur Astral Nexus (voir LauncherSettings.ServerHost)
            if (!string.IsNullOrWhiteSpace(_settings.ServerHost))
            {
                ServerListWriter.WriteSingleServer(_settings.GameDirectory, _settings.ServerName, _settings.ServerHost);
            }

            // 6. Lancement
            AppendLog("Lancement du jeu...");
            var gameOutput = new Progress<string>(AppendLog);
            _activeGame = await _gameLauncher.LaunchAsync(
                launcher, versionId, session, javaPath, _settings.MinRamMb, _settings.MaxRamMb,
                _settings.ServerHost, _settings.ServerPort, gameOutput);

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            AppendLog("Jeu lancé.");
        }
        catch (Exception ex)
        {
            ProgressBar.IsIndeterminate = false;
            AppendLog($"Erreur : {ex.Message}");
        }
        finally
        {
            PlayButton.IsEnabled = true;
        }
    }

    // Fenêtre sans chrome Windows (WindowStyle="None") : on réimplémente le déplacement et les
    // boutons réduire/fermer nous-mêmes.
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AppendLog(string message)
    {
        // Chaque Progress<T> ci-dessus a été construit sur le thread UI : IProgress<T>.Report
        // marshale déjà son callback sur ce contexte, même quand Report() est appelé depuis un
        // thread d'arrière-plan (ex. lecture de la sortie du jeu) — pas besoin de Dispatcher ici.
        StatusLogTextBox.AppendText(message + Environment.NewLine);
        StatusLogTextBox.ScrollToEnd();
    }
}
