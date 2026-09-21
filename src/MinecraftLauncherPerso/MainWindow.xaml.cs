using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Documents;
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
using MinecraftLauncherPerso.Services.News;
using MinecraftLauncherPerso.Services.Status;
using MinecraftLauncherPerso.Services.Update;

namespace MinecraftLauncherPerso;

public partial class MainWindow : Window
{
    private const string SkinEditorUrl = "https://www.minecraft.net/en-us/msaprofile/mygames/editskin";

    private static readonly HttpClient SkinHttpClient = CreateSkinHttpClient();

    private static HttpClient CreateSkinHttpClient()
    {
        var client = new HttpClient();
        // Comme pour l'API GitHub et Xbox Live ailleurs dans ce fichier : certains services
        // rejettent (403) les requêtes sans User-Agent, traitées comme du trafic automatisé
        // suspect. Sans ça, un avatar qui échouait à charger le faisait silencieusement (le
        // catch ci-dessous n'affichait rien), donnant l'impression que la connexion Microsoft
        // n'avait tout simplement pas récupéré le skin.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MinecraftLauncherPerso/1.0");
        return client;
    }

    private readonly IJavaManager _javaManager;
    private readonly IForgeManager _forgeManager;
    private readonly IModSyncService _modSyncService;
    private readonly IAuthService _authService;
    private readonly IGameLauncher _gameLauncher;
    private readonly IServerStatusService _serverStatusService;
    private readonly IUpdateService _updateService;
    private readonly INewsService _newsService;
    private readonly SettingsManager _settingsManager;
    private readonly DispatcherTimer _serverStatusTimer;
    private readonly DispatcherTimer _updateCheckTimer;
    private LauncherSettings _settings;
    private UpdateInfo? _pendingUpdate;
    private ServerStatus? _lastServerStatus;

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
        _newsService = new NewsService();

        _serverStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _serverStatusTimer.Tick += async (_, _) => await RefreshServerStatusAsync();

        // Re-vérifie une éventuelle mise à jour du launcher toutes les minutes : sans ça, il
        // fallait fermer/rouvrir le launcher pour la détecter (elle n'était vérifiée qu'au
        // démarrage). CheckForUpdateAsync ne fait rien de plus si une mise à jour est déjà
        // détectée et en attente, pour ne pas re-notifier/re-sonner toutes les minutes.
        _updateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _updateCheckTimer.Tick += async (_, _) => await CheckForUpdateAsync();

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ServerEyebrowText.Text = _settings.ServerName.ToUpperInvariant();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "LAUNCHER" : $"LAUNCHER v{version.Major}.{version.Minor}.{version.Build}";

        await ShowNewsAsync();
        _serverStatusTimer.Start();
        _updateCheckTimer.Start();
        await RefreshServerStatusAsync();
        await CheckForUpdateAsync();

        // Réaffiche automatiquement le profil connecté si une session Microsoft valide est déjà
        // en cache (silencieux : jamais de navigateur ouvert ici) — sans ça, "SE CONNECTER"
        // réapparaissait à chaque redémarrage du launcher même une fois déjà connecté, alors que
        // la session elle-même survivait bien (msal-cache-v2.bin), seul l'état affiché à l'écran
        // était perdu.
        var cachedSession = await _authService.TryGetCachedSessionAsync();
        if (cachedSession is not null)
        {
            ShowConnectedPlayer(cachedSession);
        }
    }

    private async Task ShowNewsAsync()
    {
        // La carte actus reste toujours visible (mise en page deux colonnes de la barre
        // latérale) ; seul son contenu change — le texte par défaut ("Aucune actualité pour le
        // moment.") posé dans le XAML reste affiché tant qu'aucun news.txt n'est disponible.
        var news = await _newsService.FetchNewsAsync(_settings.ModpackZipUrl);
        if (news is null)
        {
            return;
        }

        NewsText.Text = news;
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastServerStatus is { IsOnline: false })
        {
            var result = MessageBox.Show(
                "Le serveur Astral Nexus semble hors ligne pour le moment. Lancer quand même le jeu ?",
                "Serveur hors ligne",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        PlayButton.IsEnabled = false;
        ViewLogsButton.Visibility = Visibility.Collapsed;
        StatusLogTextBox.Clear();
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = 0;
        LoadingPanel.Visibility = Visibility.Visible;

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
            ShowConnectedPlayer(session);

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
            LoadingPanel.Visibility = Visibility.Collapsed;
            // Bouton laissé désactivé tant que cette partie tourne : le jeu n'empêche pas
            // plusieurs instances de lui-même, seul GameLauncher.GameExited le réactive (voir
            // ci-dessous), pour éviter de pouvoir lancer un deuxième Minecraft par-dessus.
            return;
        }
        catch (Exception ex)
        {
            ProgressBar.IsIndeterminate = false;
            AppendLog($"Erreur : {ex.Message}");
            ShowLoadingError($"Erreur : {ex.Message}");
        }

        PlayButton.IsEnabled = true;
    }

    /// <summary>
    /// Déclenché par IGameLauncher.GameExited, sur un thread d'arrière-plan (Process.Exited) :
    /// toute mise à jour de l'UI doit repasser par le Dispatcher. Réactive le bouton "Jouer"
    /// (désactivé depuis le lancement, voir PlayButton_Click) dans tous les cas, et signale en
    /// plus une sortie anormale (code non nul).
    /// </summary>
    private void GameLauncher_GameExited(object? sender, int exitCode)
    {
        Dispatcher.Invoke(() =>
        {
            PlayButton.IsEnabled = true;

            if (exitCode != 0)
            {
                AppendLog($"Le jeu s'est arrêté de façon inattendue (code {exitCode}).");
                ViewLogsButton.Visibility = Visibility.Visible;
                SystemSounds.Hand.Play();
            }

            LoadingPanel.Visibility = Visibility.Collapsed;
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
            ServerStatusText.Text = "Non configuré";
            ServerStatusDot.Fill = (Brush)FindResource("InkDimBrush");
            ServerCountText.Inlines.Clear();
            ServerCountText.Inlines.Add(new Run("—"));
            return;
        }

        var status = await _serverStatusService.PingAsync(_settings.ServerHost, _settings.ServerPort);
        _lastServerStatus = status;

        ServerStatusText.Text = status.IsOnline ? "EN LIGNE" : "HORS LIGNE";
        ServerStatusText.ToolTip = status.IsOnline ? null : status.ErrorDetail;
        var statusBrush = (Brush)FindResource(status.IsOnline ? "CyanBrush" : "MagentaBrush");
        ServerStatusDot.Fill = statusBrush;

        ServerCountText.Inlines.Clear();
        if (status.IsOnline)
        {
            ServerCountText.Inlines.Add(new Run(status.OnlinePlayers.ToString()));
            ServerCountText.Inlines.Add(new Run($"/{status.MaxPlayers}")
            {
                FontSize = 16,
                Foreground = (Brush)FindResource("InkDimBrush"),
            });
        }
        else
        {
            ServerCountText.Inlines.Add(new Run("—") { Foreground = statusBrush });
        }
    }

    private async Task CheckForUpdateAsync()
    {
        if (_pendingUpdate is not null)
        {
            // Déjà détectée (et en attente ou en cours de téléchargement) lors d'un appel
            // précédent : pas la peine de re-solliciter l'API GitHub ni de rejouer le son/la
            // notification toutes les minutes.
            return;
        }

        var update = await _updateService.CheckForUpdateAsync();
        if (update is null)
        {
            return;
        }

        _pendingUpdate = update;
        UpdateBannerButton.Content = $"MISE À JOUR {update.Version} DISPONIBLE";
        UpdateBannerButton.Visibility = Visibility.Visible;
        SystemSounds.Asterisk.Play();
    }

    private async void UpdateBannerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null)
        {
            return;
        }

        UpdateBannerButton.IsEnabled = false;
        // Sans ça, la progression (téléchargement, redémarrage) n'apparaissait nulle part :
        // AppendLog met bien à jour le texte du panneau de chargement, mais le panneau lui-même
        // restait masqué tant que PlayButton_Click ne l'avait pas explicitement affiché.
        LoadingPanel.Visibility = Visibility.Visible;
        ProgressBar.IsIndeterminate = true;
        try
        {
            await _updateService.ApplyUpdateAndRestartAsync(_pendingUpdate, new Progress<string>(AppendLog));
        }
        catch (Exception ex)
        {
            ProgressBar.IsIndeterminate = false;
            AppendLog($"Échec de la mise à jour : {ex.Message}");
            ShowLoadingError($"Échec de la mise à jour : {ex.Message}");
            UpdateBannerButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Connexion Microsoft/Minecraft indépendante du bouton "Jouer" : permet de voir/valider son
    /// identité avant de lancer le jeu (GetActiveSessionAsync réutilise silencieusement la session
    /// en cache si elle existe déjà, donc un second appel côté PlayButton_Click ne rouvre pas de
    /// navigateur).
    /// </summary>
    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false;

        try
        {
            var session = await _authService.GetActiveSessionAsync();
            ShowConnectedPlayer(session);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Échec de la connexion : {ex.Message}",
                "Connexion Minecraft",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            LoginButton.IsEnabled = true;
        }
    }

    private void ShowConnectedPlayer(MinecraftSession session)
    {
        LoginButton.Visibility = Visibility.Collapsed;
        SidebarAccountPanel.Visibility = Visibility.Visible;
        PlayerNameText.Text = session.Username;
        _ = LoadPlayerAvatarAsync(session.Uuid);
    }

    /// <summary>
    /// Deux services de rendu d'avatar indépendants (crafatar puis minotar en secours) : si l'un
    /// des deux est temporairement indisponible/bloqué, l'autre a de bonnes chances de fonctionner
    /// quand même plutôt que de laisser tomber l'avatar entièrement.
    /// </summary>
    private static readonly Func<string, string>[] AvatarUrlBuilders =
    {
        uuid => $"https://crafatar.com/avatars/{uuid}?size=40&overlay",
        uuid => $"https://minotar.net/avatar/{uuid}/40.png",
    };

    private async Task LoadPlayerAvatarAsync(string uuid)
    {
        var errors = new List<string>();

        foreach (var buildUrl in AvatarUrlBuilders)
        {
            var url = buildUrl(uuid);
            try
            {
                var bytes = await SkinHttpClient.GetByteArrayAsync(url);

                var image = new BitmapImage();
                using var stream = new MemoryStream(bytes);
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();

                PlayerAvatarImage.Source = image;
                PlayerAvatarBorder.Visibility = Visibility.Visible;
                AvatarErrorHint.Visibility = Visibility.Collapsed;
                return;
            }
            catch (Exception ex)
            {
                errors.Add($"{url} : {ex.Message}");
            }
        }

        // Les deux services ont échoué : pas bloquant, on garde le pseudo texte, mais on le
        // rend visible (au lieu du silence complet d'avant) via un indicateur survolable, en
        // plus du journal, pour pouvoir diagnostiquer sans avoir à rouvrir le code.
        var combinedError = string.Join(Environment.NewLine, errors);
        AppendLog($"Avatar Minecraft indisponible :{Environment.NewLine}{combinedError}");
        AvatarErrorHint.ToolTip = combinedError;
        AvatarErrorHint.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Clic sur l'avatar du profil connecté : ouvre la page officielle de modification du skin
    /// dans le navigateur par défaut (pas d'éditeur intégré au launcher — minecraft.net gère déjà
    /// l'upload/la prévisualisation, la session de connexion du navigateur suffit).
    /// </summary>
    private void PlayerAvatarBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(SkinEditorUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog($"Impossible d'ouvrir l'éditeur de skin : {ex.Message}");
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_settings) { Owner = this };
        window.ShowDialog();

        if (window.SettingsSaved)
        {
            _settingsManager.Save(_settings);
        }
    }

    // Item "PARAMÈTRES" de la barre latérale : simple relais vers le même handler que l'icône ⚙
    // de la barre de titre (MouseLeftButtonUp, pas Click, car ce n'est pas un Button mais un
    // Border cliquable comme les autres items de nav).
    private void ParametresNavItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        SettingsButton_Click(sender, e);

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

        // Le journal brut reste hors-écran (StatusLogTextBox) ; seul le dernier message est
        // affiché, façon écran de chargement, dans le panneau visible (LoadingPanel).
        LoadingSpinner.Visibility = Visibility.Visible;
        LoadingStatusText.Foreground = (Brush)FindResource("InkDimBrush");
        LoadingStatusText.Text = message;
    }

    private void ShowLoadingError(string message)
    {
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingSpinner.Visibility = Visibility.Collapsed;
        LoadingStatusText.Foreground = (Brush)FindResource("MagentaBrush");
        LoadingStatusText.Text = message;
    }
}
