using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CmlLib.Core;
using CmlLib.Core.ProcessBuilder;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Auth;
using MinecraftLauncherPerso.Services.Configuration;
using MinecraftLauncherPerso.Services.Forge;
using MinecraftLauncherPerso.Services.Java;
using MinecraftLauncherPerso.Services.Launch;
using MinecraftLauncherPerso.Services.ModSync;
using MinecraftLauncherPerso.Services.Status;
using MinecraftLauncherPerso.Services.Update;

namespace MinecraftLauncherPerso;

public partial class MainWindow : Window
{
    private static readonly HttpClient SkinHttpClient = new();

    private readonly IJavaManager _javaManager;
    private readonly IForgeManager _forgeManager;
    private readonly IModSyncService _modSyncService;
    private readonly IAuthService _authService;
    private readonly IGameLauncher _gameLauncher;
    private readonly IServerStatusService _serverStatusService;
    private readonly IUpdateService _updateService;
    private readonly SettingsManager _settingsManager;
    private readonly DispatcherTimer _serverStatusTimer;
    private LauncherSettings _settings;
    private UpdateInfo? _pendingUpdate;

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
        _gameLauncher.GameExited += GameLauncher_GameExited;
        _serverStatusService = new ServerStatusService();
        _updateService = new GitHubUpdateService();

        MaxRamTextBox.Text = _settings.MaxRamMb.ToString();

        _serverStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _serverStatusTimer.Tick += async (_, _) => await RefreshServerStatusAsync();

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _serverStatusTimer.Start();
        await RefreshServerStatusAsync();
        await CheckForUpdateAsync();
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        PlayButton.IsEnabled = false;
        ViewLogsButton.Visibility = Visibility.Collapsed;
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
            PlayerNameText.Text = session.Username;
            _ = LoadPlayerAvatarAsync(session.Uuid);

            // 5. Verrouille la liste multijoueur sur Astral Nexus (voir LauncherSettings.ServerHost)
            if (!string.IsNullOrWhiteSpace(_settings.ServerHost))
            {
                ServerListWriter.WriteSingleServer(_settings.GameDirectory, _settings.ServerName, _settings.ServerHost);
            }

            // 6. Lancement
            AppendLog("Lancement du jeu...");
            var gameOutput = new Progress<string>(AppendLog);
            _activeGame = await _gameLauncher.LaunchAsync(launcher, versionId, session, javaPath, _settings, gameOutput);

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

    /// <summary>
    /// Déclenché par IGameLauncher.GameExited, sur un thread d'arrière-plan (Process.Exited) :
    /// toute mise à jour de l'UI doit repasser par le Dispatcher.
    /// </summary>
    private void GameLauncher_GameExited(object? sender, int exitCode)
    {
        if (exitCode == 0)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            AppendLog($"Le jeu s'est arrêté de façon inattendue (code {exitCode}).");
            ViewLogsButton.Visibility = Visibility.Visible;
        });
    }

    private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
    {
        var logPath = Path.Combine(_settings.GameDirectory, "logs", "latest.log");
        var target = File.Exists(logPath) ? logPath : Path.Combine(_settings.GameDirectory, "logs");

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog($"Impossible d'ouvrir les logs : {ex.Message}");
        }
    }

    private async Task RefreshServerStatusAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.ServerHost))
        {
            ServerStatusText.Text = "";
            return;
        }

        var status = await _serverStatusService.PingAsync(_settings.ServerHost, _settings.ServerPort);

        ServerStatusText.Text = status.IsOnline
            ? $"● En ligne — {status.OnlinePlayers}/{status.MaxPlayers} joueurs"
            : "● Hors ligne";
        ServerStatusText.Foreground = (Brush)FindResource(status.IsOnline ? "CyanBrush" : "MagentaBrush");
    }

    private async Task CheckForUpdateAsync()
    {
        var update = await _updateService.CheckForUpdateAsync();
        if (update is null)
        {
            return;
        }

        _pendingUpdate = update;
        UpdateBannerButton.Content = $"MISE À JOUR {update.Version} DISPONIBLE";
        UpdateBannerButton.Visibility = Visibility.Visible;
    }

    private async void UpdateBannerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null)
        {
            return;
        }

        UpdateBannerButton.IsEnabled = false;
        try
        {
            await _updateService.ApplyUpdateAndRestartAsync(_pendingUpdate, new Progress<string>(AppendLog));
        }
        catch (Exception ex)
        {
            AppendLog($"Échec de la mise à jour : {ex.Message}");
            UpdateBannerButton.IsEnabled = true;
        }
    }

    private async Task LoadPlayerAvatarAsync(string uuid)
    {
        try
        {
            var bytes = await SkinHttpClient.GetByteArrayAsync($"https://crafatar.com/avatars/{uuid}?size=40&overlay");

            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            PlayerAvatarImage.Source = image;
            PlayerAvatarImage.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            // Service d'avatar indisponible : pas grave, on garde juste le pseudo texte.
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_settings) { Owner = this };
        window.ShowDialog();

        if (window.SettingsSaved)
        {
            _settingsManager.Save(_settings);
            MaxRamTextBox.Text = _settings.MaxRamMb.ToString();
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
