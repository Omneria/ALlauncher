using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CmlLib.Core;
using CmlLib.Core.ProcessBuilder;
using MinecraftLauncherPerso.Controls;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Auth;
using MinecraftLauncherPerso.Services.Changelog;
using MinecraftLauncherPerso.Services.Configuration;
using MinecraftLauncherPerso.Services.Diagnostics;
using MinecraftLauncherPerso.Services.Forge;
using MinecraftLauncherPerso.Services.GitHub;
using MinecraftLauncherPerso.Services.Hardware;
using MinecraftLauncherPerso.Services.Http;
using MinecraftLauncherPerso.Services.Java;
using MinecraftLauncherPerso.Services.Launch;
using MinecraftLauncherPerso.Services.Maintenance;
using MinecraftLauncherPerso.Services.ModSync;
using MinecraftLauncherPerso.Services.Network;
using MinecraftLauncherPerso.Services.News;
using MinecraftLauncherPerso.Services.Notifications;
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
    private readonly LaunchPipeline _launchPipeline;
    private readonly IServerStatusService _serverStatusService;
    private readonly IUpdateService _updateService;
    private readonly INewsService _newsService;
    private readonly NewsHistoryStore _newsHistoryStore;
    private readonly IMaintenanceService _maintenanceService;
    private readonly IReleaseChangelogService _changelogService;
    private readonly SettingsManager _settingsManager;
    private readonly DispatcherTimer _serverStatusTimer;
    private readonly DispatcherTimer _updateCheckTimer;
    private readonly DispatcherTimer _newsRefreshTimer;
    private readonly DispatcherTimer _maintenanceRefreshTimer;
    private readonly DispatcherTimer _changelogRefreshTimer;
    private readonly DispatcherTimer _connectivityTimer;
    private readonly IConnectivityService _connectivityService;

    /// <summary>Vrai tant que le bandeau "pas de connexion internet" est affiché (v1.14.0).</summary>
    private bool _isOffline;
    private LauncherSettings _settings;
    private UpdateInfo? _pendingUpdate;
    private ServerStatus? _lastServerStatus;
    private DispatcherTimer? _playButtonWatchdog;
    private List<NewsHistoryEntry> _newsHistory = [];
    private bool _isCompactMode;
    private double _normalWidth = 1240;
    private string? _connectedUuid;
    private readonly DispatcherTimer _avatarRefreshTimer;

    // Référence gardée en vie pour toute la durée de la partie : sans elle, le process/wrapper
    // serait éligible au GC et les événements de sortie du jeu s'arrêteraient.
    private ProcessWrapper? _activeGame;

    // Annulation du pipeline de lancement en cours (bouton ANNULER), null hors lancement.
    private CancellationTokenSource? _launchCts;

    // Tampon borné de la sortie console du jeu (stdout/stderr), pour CrashDiagnosisService sur une
    // sortie anormale — pas la peine de le relire depuis le disque, gameOutput passe déjà par ici.
    private const int CrashBufferMaxLines = 400;
    private readonly Queue<string> _gameOutputBuffer = new();

    // La sortie du jeu arrive sur les threads de lecture du process (pas via le Dispatcher, voir
    // PlayButton_Click) : le tampon est protégé par ce verrou.
    private readonly object _gameOutputLock = new();

    // Préchargement du modpack en arrière-plan (v1.8.0) : lancé dès MainWindow_Loaded si une mise à
    // jour est détectée, annulé si le joueur clique sur JOUER avant la fin pour éviter que les deux
    // n'écrivent en même temps dans le même fichier .part (voir ModSyncService.PrefetchAsync).
    private CancellationTokenSource? _prefetchCts;
    private Task? _prefetchTask;
    private readonly bool _isFirstLaunch;

    public MainWindow()
    {
        InitializeComponent();

        _settingsManager = new SettingsManager();
        _isFirstLaunch = !_settingsManager.SettingsFileExists();
        _settings = _settingsManager.Load();

        // Taille de fenêtre et mode compact mémorisés d'un lancement à l'autre (v1.8.0) : sans ça,
        // la fenêtre (redimensionnable depuis ce même changement) revenait systématiquement à sa
        // taille par défaut au redémarrage malgré un redimensionnement manuel.
        _normalWidth = _settings.LauncherWindowWidth;
        Width = _settings.LauncherWindowWidth;
        Height = _settings.LauncherWindowHeight;
        if (_settings.IsCompactMode)
        {
            ApplyCompactMode(compact: true);
        }

        // HttpClient partagé plutôt qu'un new HttpClient() par service (voir SharedHttpClient) :
        // tous des singletons créés une seule fois ici, au démarrage.
        _javaManager = new JavaManager(httpClient: SharedHttpClient.Instance);
        _forgeManager = new ForgeManager();
        _modSyncService = new ModSyncService(SharedHttpClient.Instance);
        _authService = new MicrosoftAuthService(_settings.MicrosoftClientId, httpClient: SharedHttpClient.Instance);
        _gameLauncher = new GameLauncher();
        _gameLauncher.GameExited += GameLauncher_GameExited;
        _launchPipeline = new LaunchPipeline(_javaManager, _forgeManager, _modSyncService, _authService, _gameLauncher);
        _serverStatusService = new ServerStatusService();
        // Un seul appel à l'API GitHub pour la mise à jour et le changelog (v1.12.0).
        var releasesClient = new GitHubReleasesClient(SharedHttpClient.Instance);
        _updateService = new GitHubUpdateService(SharedHttpClient.Instance, releasesClient);
        _newsService = new NewsService(SharedHttpClient.Instance);
        _newsHistoryStore = new NewsHistoryStore();
        _newsHistory = _newsHistoryStore.Load();
        _maintenanceService = new MaintenanceService(SharedHttpClient.Instance);
        _changelogService = new ReleaseChangelogService(releasesClient, SharedHttpClient.Instance);
        _connectivityService = new ConnectivityService(SharedHttpClient.Instance);

        // Toutes les cadences de rafraîchissement du contenu affiché sont alignées sur 1 minute
        // (v1.10.0) — auparavant un mélange de 30s/1 min/5 min/30 min sans logique d'ensemble
        // claire ; un seul délai à connaître pour savoir dans quel délai un changement (statut
        // serveur, actu, maintenance, changelog, avatar) apparaît sans avoir à redémarrer le
        // launcher. Le watchdog du bouton JOUER (6h, PlayButtonWatchdog) n'est pas concerné : ce
        // n'est pas un rafraîchissement de contenu mais un filet de sécurité contre un bouton
        // resté bloqué, sans rapport avec la fraîcheur d'une donnée affichée.
        // Chaque tick passe par RunSafeAsync : un "async void" (handler d'événement) qui lève
        // remonte directement au filet global de App.xaml.cs — un simple échec de rafraîchissement
        // d'actus ne doit jamais devenir une boîte de dialogue d'erreur, encore moins fermer le
        // launcher. Les services sont déjà best-effort, ceci est la seconde ceinture.
        _serverStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _serverStatusTimer.Tick += async (_, _) => await RunSafeAsync("Statut du serveur", RefreshServerStatusAsync);

        // CheckForUpdateAsync ne fait rien de plus si une mise à jour est déjà détectée et en
        // attente, pour ne pas re-notifier/re-sonner à chaque minute.
        _updateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _updateCheckTimer.Tick += async (_, _) => await RunSafeAsync("Vérification de mise à jour", CheckForUpdateAsync);

        _newsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _newsRefreshTimer.Tick += async (_, _) => await RunSafeAsync("Actus", ShowNewsAsync);

        _maintenanceRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _maintenanceRefreshTimer.Tick += async (_, _) => await RunSafeAsync("Bannière de maintenance", RefreshMaintenanceBannerAsync);

        _changelogRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _changelogRefreshTimer.Tick += async (_, _) => await RunSafeAsync("Changelog", ShowChangelogAsync);

        _connectivityTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _connectivityTimer.Tick += async (_, _) => await RunSafeAsync("Connexion internet", RefreshConnectivityAsync);

        // L'avatar (cache disque depuis v1.6.0) n'était rafraîchi qu'aux moments où
        // ShowConnectedPlayer était appelée (connexion, restauration de session au démarrage) :
        // si le joueur change de skin sur minecraft.net pendant une session déjà longue du
        // launcher, rien ne le détecte avant le prochain redémarrage. Re-vérifie périodiquement
        // tant qu'un profil est affiché.
        _avatarRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _avatarRefreshTimer.Tick += async (_, _) =>
        {
            if (_connectedUuid is not null)
            {
                var uuid = _connectedUuid;
                await RunSafeAsync("Avatar", () => LoadPlayerAvatarAsync(uuid));
            }
        };
        _avatarRefreshTimer.Start();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    /// <summary>
    /// Exécute une opération best-effort du tableau de bord en journalisant son échec au lieu de
    /// le laisser remonter (voir le commentaire sur les timers dans le constructeur).
    /// </summary>
    private static async Task RunSafeAsync(string operation, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", $"{operation} : échec inattendu, ignoré.", ex);
        }
    }

    /// <summary>Mémorise la taille de fenêtre et le mode compact courants pour le prochain lancement.</summary>
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Un préchargement du modpack en arrière-plan (StartModpackPrefetch) ne doit pas continuer
        // à écrire sur le disque après la fermeture du launcher ni bloquer la fermeture.
        _prefetchCts?.Cancel();

        // WindowState.Normal uniquement : si la fenêtre est minimisée/maximisée à la fermeture,
        // Width/Height ne reflètent pas sa taille "normale" réelle, ça écraserait la valeur utile
        // avec la taille minimisée/maximisée au prochain démarrage.
        if (WindowState == WindowState.Normal)
        {
            _settings.LauncherWindowWidth = _isCompactMode ? _normalWidth : Width;
            _settings.LauncherWindowHeight = Height;
        }

        _settings.IsCompactMode = _isCompactMode;

        try
        {
            _settingsManager.Save(_settings);
        }
        catch (Exception ex)
        {
            // Disque plein, dossier %AppData% verrouillé... : perdre la taille de fenêtre est sans
            // gravité, une erreur bloquante à la fermeture ne l'est pas.
            Logger.Error("MainWindow", "Sauvegarde des réglages à la fermeture impossible.", ex);
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ServerNameText.Text = _settings.ServerName.ToUpperInvariant();
        // "-dev.<build>" sur un exe de test (v1.12.0), pour ne pas le confondre avec la release.
        VersionText.Text = $"LAUNCHER v{AppVersion.Display}";
        RefreshLastSyncText();

        // Assistant de premier lancement (v1.8.0) : seulement si settings.json n'existait pas
        // encore au tout début du constructeur (voir _isFirstLaunch) — après ce point, Load() l'a
        // déjà créé (comportement existant), donc ce drapeau ne redeviendra jamais vrai.
        if (_isFirstLaunch)
        {
            var welcome = new WelcomeWindow(_settings.ServerName, _settings.GameDirectory) { Owner = this };
            welcome.ShowDialog();
            _settings.GameDirectory = welcome.GameDirectory;
            _settingsManager.Save(_settings);
        }

        _serverStatusTimer.Start();
        _updateCheckTimer.Start();
        _newsRefreshTimer.Start();
        _maintenanceRefreshTimer.Start();
        _changelogRefreshTimer.Start();
        _connectivityTimer.Start();

        // La sonde de connexion tourne en même temps que les chargements : hors ligne, le bandeau
        // apparaît après quelques secondes au lieu d'attendre la fin de tous leurs délais.
        await Task.WhenAll(
            LoadDashboardAsync(),
            RunSafeAsync("Connexion internet", RefreshConnectivityAsync));

        StartModpackPrefetch();
    }

    /// <summary>
    /// Tous les chargements du tableau de bord en parallèle : enchaînés l'un après l'autre (comme
    /// avant la v1.11.0), un VPS lent ou GitHub injoignable retardait l'affichage de TOUT le tableau
    /// de bord du temps de chaque timeout cumulé. Sûr côté UI : chaque tâche ne touche aux contrôles
    /// qu'après ses propres await, toujours sur le thread du Dispatcher. Relancé en entier au
    /// retour de la connexion (voir RefreshConnectivityAsync).
    /// </summary>
    private Task LoadDashboardAsync() =>
        Task.WhenAll(
            RunSafeAsync("Actus", ShowNewsAsync),
            RunSafeAsync("Changelog", ShowChangelogAsync),
            RunSafeAsync("Bannière de maintenance", RefreshMaintenanceBannerAsync),
            RunSafeAsync("Statut du serveur", RefreshServerStatusAsync),
            RunSafeAsync("Vérification de mise à jour", CheckForUpdateAsync),
            RunSafeAsync("Restauration de session", RestoreCachedSessionAsync),
            RunSafeAsync("Réglages du pack", LoadPackSettingsAsync));

    /// <summary>
    /// Affiche ou masque le bandeau "pas de connexion internet" (v1.14.0). Sondes : le VPS
    /// (manifest du modpack) et l'API GitHub, deux hébergeurs indépendants : si aucun ne répond,
    /// c'est la connexion du joueur qui est en cause, pas un serveur en panne. Au retour de la
    /// connexion, tout le tableau de bord est rechargé d'un coup plutôt que d'attendre, carte par
    /// carte, le rafraîchissement suivant.
    /// </summary>
    private async Task RefreshConnectivityAsync()
    {
        var online = await _connectivityService.IsOnlineAsync(
            [_settings.ModpackManifestUrl, _settings.MaintenanceMessageUrl, GitHubApiProbeUrl]);

        if (!online)
        {
            if (!_isOffline)
            {
                Logger.Warn("MainWindow", "Aucune connexion internet détectée.");
            }

            _isOffline = true;
            OfflineBanner.Visibility = Visibility.Visible;
            return;
        }

        OfflineBanner.Visibility = Visibility.Collapsed;
        if (_isOffline)
        {
            _isOffline = false;
            Logger.Info("MainWindow", "Connexion internet rétablie, rechargement du tableau de bord.");
            Toasts.Show("Connexion rétablie", "Le tableau de bord se recharge.", ToastKind.Success);
            await LoadDashboardAsync();
        }
    }

    private const string GitHubApiProbeUrl = "https://api.github.com/";

    private async void OfflineRetryButton_Click(object sender, RoutedEventArgs e)
    {
        OfflineRetryButton.IsEnabled = false;
        OfflineRetryButton.Content = "VÉRIFICATION...";
        try
        {
            await RunSafeAsync("Connexion internet", RefreshConnectivityAsync);
        }
        finally
        {
            OfflineRetryButton.IsEnabled = true;
            OfflineRetryButton.Content = "RÉESSAYER";
        }
    }

    /// <summary>
    /// Réaffiche automatiquement le profil connecté si une session Microsoft valide est déjà en
    /// cache (silencieux : jamais de navigateur ouvert ici) — sans ça, "SE CONNECTER"
    /// réapparaissait à chaque redémarrage du launcher même une fois déjà connecté, alors que la
    /// session elle-même survivait bien (msal-cache-v2.bin), seul l'état affiché à l'écran était
    /// perdu.
    /// </summary>
    private async Task RestoreCachedSessionAsync()
    {
        var cachedSession = await _authService.TryGetCachedSessionAsync();
        if (cachedSession is not null)
        {
            ShowConnectedPlayer(cachedSession);
        }
    }

    /// <summary>
    /// Précharge le modpack en arrière-plan dès qu'une mise à jour est détectée, plutôt que
    /// d'attendre le clic sur JOUER pour commencer le téléchargement — voir
    /// IModSyncService.PrefetchAsync. Best-effort et silencieux (pas de barre de progression
    /// dédiée) : PlayButton_Click annule ce préchargement avant de démarrer sa propre synchro pour
    /// éviter que les deux n'écrivent en même temps dans le même fichier partiel.
    /// </summary>
    private void StartModpackPrefetch()
    {
        _prefetchCts = new CancellationTokenSource();
        var token = _prefetchCts.Token;
        _prefetchTask = _modSyncService.PrefetchAsync(_settings.ModpackZipUrl, _settings.ModpackManifestUrl, _settings.GameDirectory, cancellationToken: token);
        _ = _prefetchTask.ContinueWith(
            t => Logger.Warn("MainWindow", $"Préchargement du modpack interrompu : {t.Exception?.GetBaseException().Message}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private async Task ShowNewsAsync()
    {
        // La carte actus reste toujours visible (mise en page deux colonnes de la barre
        // latérale) ; seul son contenu change. Historique (pas juste la dernière actu) : chaque
        // contenu distinct observé est horodaté et conservé localement (NewsHistoryStore), puisque
        // news.txt côté VPS ne garde lui-même aucun historique.
        // Mises à jour du pack (changelog.json, v1.12.0) : tuile MODPACK du hub (v2.0.0, avant
        // mêlées aux actus), pour que les joueurs voient ce qui arrive pendant le déploiement du
        // pack en plusieurs salves sans qu'il faille l'annoncer à la main dans news.txt.
        var newsTask = _newsService.FetchNewsAsync(_settings.ModpackZipUrl);
        var changelogTask = _modSyncService.FetchChangelogAsync(_settings.ModpackManifestUrl);
        var news = await newsTask;
        var changelog = await changelogTask;
        if (news is not null)
        {
            _newsHistory = _newsHistoryStore.RecordIfNew(_newsHistory, news);
        }

        var newsItems = _newsHistory
            .OrderByDescending(entry => entry.FetchedAt)
            .Take(MaxNewsItems)
            .Select(entry => new NewsHistoryItem(FormatNewsDate(entry.FetchedAt), entry.Content))
            .ToList();
        var modpackItems = changelog
            .OrderByDescending(entry => entry.Date)
            .Take(MaxNewsItems)
            .Select(entry => new NewsHistoryItem(FormatNewsDate(entry.Date), string.Join("\n", entry.Lines)))
            .ToList();

        NewsEmptyText.Visibility = newsItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NewsHistoryList.ItemsSource = newsItems;
        ModpackEmptyText.Visibility = modpackItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModpackList.ItemsSource = modpackItems;
    }

    private const int MaxNewsItems = 20;

    private static string FormatNewsDate(DateTimeOffset date) => date.ToLocalTime().ToString("dd/MM HH:mm");

    /// <summary>Vue d'affichage d'une actu (news.txt) ou d'une mise à jour du pack
    /// (changelog.json), avec l'horodatage déjà mis en forme pour le binding XAML.</summary>
    private sealed record NewsHistoryItem(string FetchedAtLabel, string Content);

    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// Changelog (releases GitHub du launcher, pas un fichier changelog.txt séparé côté VPS) : même
    /// source et mêmes règles de nettoyage des notes que la carte "Changelog" de la landing page
    /// (ReleaseChangelogService). Best-effort comme les actus : une requête échouée laisse le
    /// dernier contenu affiché plutôt que de vider la carte.
    /// </summary>
    private async Task ShowChangelogAsync()
    {
        var releases = await _changelogService.GetRecentReleasesAsync(count: 4);
        if (releases.Count == 0)
        {
            ChangelogEmptyText.Visibility = ChangelogList.ItemsSource is null ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        ChangelogEmptyText.Visibility = Visibility.Collapsed;
        ChangelogList.ItemsSource = releases
            .Select(release => new ChangelogItem(
                release.Tag,
                release.Url,
                release.PublishedAt.ToLocalTime().ToString("d MMMM yyyy", FrenchCulture),
                release.IsLatest ? Visibility.Visible : Visibility.Collapsed,
                release.Notes))
            .ToList();
    }

    /// <summary>Vue d'affichage d'une ReleaseChangelogEntry pour le binding XAML. "VersionTag"
    /// plutôt que "Tag" pour ne pas se confondre avec FrameworkElement.Tag, qui porte l'URL.</summary>
    private sealed record ChangelogItem(string VersionTag, string Url, string DateLabel, Visibility LatestBadgeVisibility, IReadOnlyList<string> Notes);

    /// <summary>Clic sur un numéro de version de la carte CHANGELOG : ouvre la release GitHub
    /// correspondante (notes complètes, exe joint) dans le navigateur.</summary>
    private void ChangelogVersion_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrEmpty(url))
        {
            OpenExternalUrl(url);
        }
    }

    private void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog($"Impossible d'ouvrir {url} : {ex.Message}");
        }
    }

    // Ligne optionnelle "FIN: <valeur>" (n'importe où après le titre) : affichée dans le bloc
    // "FIN ESTIMÉE" à droite de la carte (mockup Option B), plutôt qu'un champ dédié qui aurait
    // imposé un format de fichier rigide (title\nend\nsubtitle) et cassé les maintenance.txt déjà
    // en place (title\nsubtitle) — cette ligne reste totalement optionnelle et retirée du sous-titre.
    private static readonly Regex MaintenanceEndTimeLinePattern =
        new(@"^\s*FIN\s*:\s*(?<value>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Bannière de maintenance (v1.9.0) : affiche/masque MaintenanceBanner selon qu'un
    /// maintenance.txt non vide est servi à MaintenanceMessageUrl. Ne fait rien si ce réglage n'est
    /// pas configuré (comportement identique à ModpackManifestUrl : optionnel, désactivé par défaut).
    /// Première ligne du fichier affichée en titre, une éventuelle ligne "FIN: ..." en fin de
    /// maintenance estimée (voir MaintenanceEndTimeLinePattern), le reste en sous-titre atténué —
    /// permet un message court en une ligne comme un message détaillé sur plusieurs, sans format
    /// imposé côté VPS (pas de "champ titre" séparé à gérer).
    /// </summary>
    private async Task RefreshMaintenanceBannerAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.MaintenanceMessageUrl))
        {
            return;
        }

        var status = await _maintenanceService.FetchMaintenanceMessageAsync(_settings.MaintenanceMessageUrl);
        if (!status.IsKnown)
        {
            // VPS injoignable : état inconnu, on laisse la bannière exactement comme elle est
            // (affichée ou non) plutôt que de la faire disparaître pendant la coupure même qu'elle
            // annonce. Elle sera masquée dès que le VPS répondra à nouveau sans maintenance.txt.
            return;
        }

        if (status.Message is null)
        {
            MaintenanceBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var rawLines = status.Message.Replace("\r\n", "\n").Split('\n');
        MaintenanceBannerTitle.Text = rawLines[0].Trim();

        string? endTime = null;
        var subtitleLines = new List<string>();
        for (var i = 1; i < rawLines.Length; i++)
        {
            var match = MaintenanceEndTimeLinePattern.Match(rawLines[i]);
            if (match.Success && endTime is null)
            {
                endTime = match.Groups["value"].Value;
            }
            else
            {
                subtitleLines.Add(rawLines[i]);
            }
        }

        var subtitle = string.Join("\n", subtitleLines).Trim();
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            MaintenanceBannerSubtitle.Text = subtitle;
            MaintenanceBannerSubtitle.Visibility = Visibility.Visible;
        }
        else
        {
            MaintenanceBannerSubtitle.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrWhiteSpace(endTime))
        {
            MaintenanceBannerEndTimeValue.Text = endTime;
            MaintenanceBannerEndTimeBlock.Visibility = Visibility.Visible;
        }
        else
        {
            MaintenanceBannerEndTimeBlock.Visibility = Visibility.Collapsed;
        }

        MaintenanceBanner.Visibility = Visibility.Visible;
    }

    // ACTUS/SERVEUR n'ouvrent pas un écran séparé (tout est déjà visible sur ce tableau de bord
    // à deux colonnes) : un clic fait juste pulser la carte correspondante pour donner un vrai
    // effet à ces items de nav, au lieu de rester des libellés inertes.
    private void ActusNavItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => FlashDashboardCard(NewsCardScale);

    private void ServeurNavItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => OpenServerPage();

    private void HubNavItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ShowPage(AppPage.Hub);

    private void FlashDashboardCard(ScaleTransform scale)
    {
        ShowPage(AppPage.Hub);
        FlashCard(scale);
    }

    // Active/Espace au clavier pour les items de nav (Border, pas Button : pas de KeyDown par
    // défaut) — seule concession "accessibilité" ajoutée ici, le reste (lecteur d'écran, chrome
    // de fenêtre natif) resterait un chantier bien plus large.
    private void NavItem_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Space)
        {
            return;
        }

        if (ReferenceEquals(sender, NavJouer))
        {
            ShowPage(AppPage.Hub);
        }
        else if (ReferenceEquals(sender, NavActus))
        {
            FlashDashboardCard(NewsCardScale);
        }
        else if (ReferenceEquals(sender, NavServeur))
        {
            OpenServerPage();
        }
        else if (ReferenceEquals(sender, NavParametres))
        {
            OpenSettings();
        }
    }

    private static void FlashCard(ScaleTransform scale)
    {
        var pulse = new DoubleAnimation
        {
            From = 1.0,
            To = 1.03,
            Duration = TimeSpan.FromMilliseconds(160),
            AutoReverse = true,
            EasingFunction = new QuadraticEase(),
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    /// <summary>Réglages publiés par le pack (RAM conseillée/minimum, Forge), lus au démarrage
    /// pour l'avertissement de RAM et l'indication des Paramètres. Null tant qu'inconnus.</summary>
    private ModpackPackSettings? _packSettings;

    /// <summary>L'avertissement "RAM insuffisante" n'est montré qu'une fois par session : un PC
    /// de 8 Go ne peut rien y changer, inutile de le lui répéter à chaque partie.</summary>
    private bool _ramWarningShown;

    private async Task LoadPackSettingsAsync()
    {
        _packSettings = await _modSyncService.FetchPackSettingsAsync(_settings.ModpackManifestUrl);
    }

    /// <summary>
    /// Vrai si le lancement peut continuer. Avertit (une fois par session) quand la RAM max
    /// réglée est sous le minimum demandé par le pack : le jeu risque de manquer de mémoire en
    /// pleine partie, sans message clair.
    /// </summary>
    private async Task<bool> ConfirmRamIsEnoughAsync()
    {
        var minRamMb = _packSettings?.MinRamMb;
        if (_ramWarningShown || minRamMb is not > 0 || _settings.MaxRamMb >= minRamMb)
        {
            return true;
        }

        _ramWarningShown = true;
        var totalMb = SystemInfo.GetTotalPhysicalMemoryMb();
        var canRaise = totalMb is null || totalMb.Value - minRamMb.Value >= 3072;
        var advice = canRaise
            ? "Augmente la RAM dans Paramètres (poignée de droite du réglage RAM)."
            : $"Ton PC a {totalMb / 1024.0:0.#} Go de RAM : ferme les autres programmes (navigateur, Discord...) pendant la partie.";
        return await Dialogs.ConfirmAsync(
            "RAM INSUFFISANTE POUR LE MODPACK",
            $"Le modpack demande au moins {minRamMb / 1024.0:0.#} Go de RAM, le launcher est réglé sur {_settings.MaxRamMb / 1024.0:0.#} Go.\n\n{advice}\n\nLancer quand même ?",
            "LANCER QUAND MÊME");
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmRamIsEnoughAsync())
        {
            return;
        }

        if (_lastServerStatus is { IsOnline: false } && !await Dialogs.ConfirmAsync(
            "SERVEUR HORS LIGNE",
            $"Le serveur {_settings.ServerName} semble hors ligne pour le moment. Lancer quand même le jeu ?",
            "LANCER QUAND MÊME"))
        {
            return;
        }

        PlayButton.IsEnabled = false;
        ViewLogsButton.Visibility = Visibility.Collapsed;
        RetryLaunchButton.Visibility = Visibility.Collapsed;
        LoadingSpinner.Visibility = Visibility.Collapsed;
        LoadingStatusText.Foreground = (Brush)FindResource("InkDimBrush");
        LoadingStatusText.Text = "";
        LaunchSteps.Reset(LaunchStepStates.StepsFor(includeServerList: !string.IsNullOrWhiteSpace(_settings.ServerHost)));
        LaunchSteps.Update(current: null, failed: null, detail: null);
        LaunchSteps.Visibility = Visibility.Visible;
        lock (_gameOutputLock)
        {
            _gameOutputBuffer.Clear();
        }
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = 0;
        LoadingPanel.Visibility = Visibility.Visible;

        // Un préchargement en arrière-plan a pu démarrer depuis MainWindow_Loaded (voir
        // StartModpackPrefetch) : on l'annule et on attend sa fin avant de lancer la vraie synchro
        // ci-dessous, pour éviter que les deux n'ouvrent le même fichier .part en même temps
        // (FileShare.None côté ModSyncService).
        if (_prefetchTask is not null)
        {
            _prefetchCts?.Cancel();
            try
            {
                await _prefetchTask;
            }
            catch (Exception)
            {
                // Peu importe pourquoi le préchargement s'est arrêté (annulation normale ou
                // véritable échec réseau) : SyncAsync ci-dessous a sa propre gestion d'erreur.
            }

            _prefetchTask = null;
        }

        // Annulable (v1.11.0) : un téléchargement Forge/modpack qui n'avance plus n'obligeait
        // jusqu'ici qu'à fermer le launcher. Le même token traverse toutes les étapes du pipeline.
        var launchCts = new CancellationTokenSource();
        _launchCts = launchCts;
        CancelLaunchButton.IsEnabled = true;
        CancelLaunchButton.Visibility = Visibility.Visible;

        // Progression "dernière valeur gagnante", appliquée 10 fois par seconde par un timer du
        // Dispatcher au lieu d'un message par rapport : voir CoalescingProgress (gel du launcher
        // pendant les téléchargements, v1.11.0).
        LaunchStep? lastStep = null;
        var launchProgress = new CoalescingProgress<LaunchProgress>(report =>
        {
            // Changement d'étape : la barre repart de zéro (Java, Forge et la synchro ont chacun
            // leur propre progression chiffrée ; auth et lancement n'en ont pas → indéterminée).
            if (report.Step != lastStep)
            {
                lastStep = report.Step;
                ProgressBar.Value = 0;
                ProgressBar.IsIndeterminate = report.Step is LaunchStep.Auth or LaunchStep.ServerList or LaunchStep.Launch;
                if (report.Step == LaunchStep.Auth)
                {
                    // La synchro vient de se terminer : "dernière synchro" doit avancer.
                    RefreshLastSyncText();
                }
            }

            // Fraction déjà garantie finie et bornée par LaunchProgress (NaN = exception WPF).
            if (report.Fraction is { } fraction)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = fraction * 100;
            }

            LaunchSteps.Update(report.Step, failed: null, report.Message);
        });
        var progressTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        progressTimer.Tick += (_, _) => launchProgress.Flush();
        progressTimer.Start();

        // Sortie console du jeu : uniquement mise en tampon pour CrashDiagnosisService, jamais
        // affichée (le panneau de chargement est déjà masqué quand elle arrive). Rapport
        // synchrone, depuis les threads de lecture du process : un Progress<T> posterait un
        // message au thread UI pour chacune des milliers de lignes d'un démarrage moddé.
        var gameOutput = new SynchronousProgress<string>(BufferGameOutput);

        try
        {
            // Task.Run : le pipeline tourne entièrement hors du thread UI. Sans ça, chaque
            // continuation des services (hash des fichiers, écriture, callbacks de CmlLib) revenait
            // sur le thread UI, qui passait son temps à les exécuter au lieu de rafraîchir la
            // fenêtre. Seul onSessionAcquired touche l'interface : repassé par le Dispatcher.
            var settings = _settings;
            var result = await Task.Run(() => _launchPipeline.RunAsync(
                settings,
                launchProgress,
                session => Dispatcher.Invoke(() => ShowConnectedPlayer(session)),
                gameOutput,
                launchCts.Token));
            progressTimer.Stop();
            launchProgress.Flush();
            _activeGame = result.Game;

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            LoadingPanel.Visibility = Visibility.Collapsed;
            // Bouton laissé désactivé tant que cette partie tourne : le jeu n'empêche pas
            // plusieurs instances de lui-même, seul GameLauncher.GameExited le réactive (voir
            // ci-dessous), pour éviter de pouvoir lancer un deuxième Minecraft par-dessus.
            // Garde-fou : si cet événement ne se déclenchait jamais pour une raison quelconque, le
            // bouton resterait grisé indéfiniment sans recours pour l'utilisateur autre que
            // redémarrer le launcher — ce timer le réactive de force après un long délai.
            StartPlayButtonWatchdog();
            return;
        }
        catch (OperationCanceledException) when (launchCts.IsCancellationRequested)
        {
            // Annulation demandée par le joueur : pas une erreur. Un téléchargement interrompu
            // reprend là où il en était au prochain clic (.part reprenable, manifest par fichier).
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
            AppendLog("Lancement annulé.");
            LoadingPanel.Visibility = Visibility.Collapsed;
            Toasts.Show("Lancement annulé", "Un téléchargement interrompu reprend là où il s'est arrêté.");
        }
        catch (Exception ex)
        {
            ProgressBar.IsIndeterminate = false;
            AppendLog($"Erreur : {ex.Message}");
            if (ex is LaunchStepException failure)
            {
                LaunchSteps.Update(current: null, failed: failure.Step, detail: failure.InnerException?.Message ?? failure.Message);
            }

            ShowLoadingError($"Erreur : {ex.Message}");
            RetryLaunchButton.Visibility = Visibility.Visible;
        }
        finally
        {
            // Arrêté ici aussi pour les chemins d'annulation/erreur ; le dernier rapport en attente
            // n'est pas appliqué (il écraserait le message d'erreur ou d'annulation affiché).
            progressTimer.Stop();
            CancelLaunchButton.Visibility = Visibility.Collapsed;
            _launchCts = null;
            launchCts.Dispose();
        }

        PlayButton.IsEnabled = true;
    }

    /// <summary>Bouton ANNULER du panneau de chargement : interrompt le pipeline de lancement à
    /// l'étape en cours (voir LaunchPipeline). Sans effet une fois le jeu démarré.</summary>
    private void CancelLaunchButton_Click(object sender, RoutedEventArgs e)
    {
        CancelLaunchButton.IsEnabled = false;
        AppendLog("Annulation en cours...");
        _launchCts?.Cancel();
    }

    /// <summary>Bouton RÉESSAYER, affiché après un échec du lancement : relance le pipeline. Les
    /// téléchargements déjà faits (Java, Forge, mods) ne sont pas refaits.</summary>
    private void RetryLaunchButton_Click(object sender, RoutedEventArgs e)
    {
        RetryLaunchButton.Visibility = Visibility.Collapsed;
        PlayButton_Click(sender, e);
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
            _playButtonWatchdog?.Stop();
            PlayButton.IsEnabled = true;

            if (exitCode != 0)
            {
                AppendLog($"Le jeu s'est arrêté de façon inattendue (code {exitCode}).");
                ViewLogsButton.Visibility = Visibility.Visible;
                SystemSounds.Hand.Play();
                _ = RunSafeAsync("Diagnostic du crash", ShowCrashDiagnosisIfAnyAsync);
            }

            LoadingPanel.Visibility = Visibility.Collapsed;
        });
    }

    /// <summary>
    /// Sortie anormale du jeu (code non nul) : jusqu'ici, seul le code de sortie brut était
    /// affiché, sans aucune piste pour l'utilisateur. Cherche un motif connu (RAM insuffisante,
    /// mod corrompu...) dans la sortie console déjà bufferisée (voir BufferGameOutput) et
    /// propose une réparation directement si le diagnostic la suggère.
    /// </summary>
    private async Task ShowCrashDiagnosisIfAnyAsync()
    {
        List<string> lines;
        lock (_gameOutputLock)
        {
            lines = [.. _gameOutputBuffer];
        }

        var diagnosis = CrashDiagnosisService.Diagnose(lines);
        if (diagnosis is null)
        {
            return;
        }

        AppendLog($"Diagnostic : {diagnosis.Message}");

        const string ViewLogsId = "logs";
        const string RepairId = "repair";
        var message = diagnosis.SuggestsRepair
            ? $"{diagnosis.Message}\n\nLancer une réparation du modpack maintenant ?"
            : diagnosis.Message;
        var primary = diagnosis.SuggestsRepair
            ? new DialogButton("RÉPARER", RepairId, DialogButtonStyle.Primary)
            : new DialogButton("FERMER", DialogHost.OkId, DialogButtonStyle.Primary);

        var choice = await Dialogs.ShowAsync(new DialogRequest(
            "LE JEU S'EST ARRÊTÉ",
            message,
            [new DialogButton("VOIR LES LOGS", ViewLogsId), primary],
            DialogHost.CancelId));

        if (choice == ViewLogsId)
        {
            ViewLogsButton_Click(this, new RoutedEventArgs());
        }
        else if (choice == RepairId)
        {
            OpenSettings(SettingsSection.Maintenance);
        }
    }

    private void BufferGameOutput(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        lock (_gameOutputLock)
        {
            _gameOutputBuffer.Enqueue(line);
            while (_gameOutputBuffer.Count > CrashBufferMaxLines)
            {
                _gameOutputBuffer.Dequeue();
            }
        }
    }

    /// <summary>IProgress qui appelle son callback immédiatement, sur le thread appelant (sans
    /// passer par le Dispatcher, contrairement à Progress&lt;T&gt;). Le callback doit être
    /// thread-safe.</summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    /// <summary>
    /// Filet de sécurité pour le bouton JOUER, désactivé pendant toute la durée de la partie et
    /// normalement réactivé par GameLauncher_GameExited (voir ci-dessus). Si cet événement ne se
    /// déclenchait jamais pour une raison quelconque, le bouton resterait grisé indéfiniment sans
    /// aucun recours pour l'utilisateur — ce timer à usage unique le réactive de force après un
    /// délai large (bien au-delà d'une session de jeu normale), plutôt que de laisser ce risque
    /// sans filet.
    /// </summary>
    private void StartPlayButtonWatchdog()
    {
        _playButtonWatchdog?.Stop();
        _playButtonWatchdog = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _playButtonWatchdog.Tick += (_, _) =>
        {
            _playButtonWatchdog!.Stop();
            if (!PlayButton.IsEnabled)
            {
                AppendLog("Bouton JOUER réactivé automatiquement (sécurité : aucune détection de fermeture du jeu depuis 6h).");
                PlayButton.IsEnabled = true;
            }
        };
        _playButtonWatchdog.Start();
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

        var wasOffline = _lastServerStatus is { IsOnline: false };

        var status = await _serverStatusService.PingAsync(_settings.ServerHost, _settings.ServerPort);
        _lastServerStatus = status;

        // Seulement sur une vraie transition hors ligne -> en ligne (pas au tout premier check,
        // où _lastServerStatus est encore null = "inconnu", pas "hors ligne") : sans ça, la toute
        // première détection "en ligne" au démarrage déclencherait une notif à chaque lancement.
        if (wasOffline && status.IsOnline)
        {
            Toasts.Show("Serveur de nouveau en ligne", status.OnlinePlayers > 0 ? $"{status.OnlinePlayers} joueur(s) connecté(s)" : null, ToastKind.Success);
            if (_settings.DesktopNotificationsEnabled)
            {
                DesktopNotificationService.Show(_settings.ServerName, "Le serveur est de nouveau en ligne.");
            }
        }

        // Version du serveur comparée à celle que le launcher installe (v1.12.0) : un serveur passé
        // à une autre version de Minecraft refuserait la connexion avec un message peu clair.
        var expectedProtocol = ServerStatusService.ExpectedProtocol(_settings.MinecraftVersion);
        var versionMismatch = status.IsOnline && expectedProtocol is not null
            && status.ProtocolVersion is not null && status.ProtocolVersion != expectedProtocol;

        ServerStatusText.Text = !status.IsOnline ? "HORS LIGNE"
            : versionMismatch ? "EN LIGNE · VERSION DIFFÉRENTE"
            : status.LatencyMs is { } latency ? $"EN LIGNE · {latency} ms"
            : "EN LIGNE";
        // Sur toute la ligne (pastille + texte), pas juste le texte : cible de survol trop étroite
        // sinon pour qu'on tombe dessus de façon fiable.
        ServerStatusRow.ToolTip = !status.IsOnline ? status.ErrorDetail
            : versionMismatch ? $"Le serveur annonce {status.VersionName ?? "une autre version"} (protocole {status.ProtocolVersion}), le launcher installe Minecraft {_settings.MinecraftVersion} : la connexion risque d'être refusée."
            : null;
        ServerStatusRow.Cursor = ServerStatusRow.ToolTip is null ? null : Cursors.Help;
        var statusBrush = (Brush)FindResource(!status.IsOnline ? "MagentaBrush" : versionMismatch ? "AmberBrush" : "CyanBrush");
        ServerStatusDot.Fill = statusBrush;

        ServerMotdText.Text = status.IsOnline ? status.Motd ?? "" : "";
        ServerMotdText.ToolTip = string.IsNullOrEmpty(ServerMotdText.Text) ? null : ServerMotdText.Text;
        ServerMotdText.Visibility = string.IsNullOrEmpty(ServerMotdText.Text) ? Visibility.Collapsed : Visibility.Visible;

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

        if (ServerPageView.IsVisible)
        {
            await RefreshServerPageAsync();
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
        LaunchSteps.Visibility = Visibility.Collapsed;
        RetryLaunchButton.Visibility = Visibility.Collapsed;
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
            LoginButton.IsEnabled = true;
            await Dialogs.AlertAsync("CONNEXION MINECRAFT", $"Échec de la connexion : {ex.Message}");
        }
    }

    private void ShowConnectedPlayer(MinecraftSession session)
    {
        LoginButton.Visibility = Visibility.Collapsed;
        SidebarAccountPanel.Visibility = Visibility.Visible;
        PlayerNameText.Text = session.Username;
        _connectedUuid = session.Uuid;
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

    private static readonly string AvatarCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MinecraftLauncherPerso", "avatar-cache");

    private async Task LoadPlayerAvatarAsync(string uuid)
    {
        var cachePath = Path.Combine(AvatarCacheDirectory, $"{uuid}.png");

        // Affiche immédiatement le dernier avatar connu en cache (évite d'attendre le réseau à
        // chaque connexion/redémarrage) pendant qu'on tente un rafraîchissement en tâche de fond ;
        // auparavant l'avatar était retéléchargé à chaque appel, sans aucun cache disque.
        if (File.Exists(cachePath))
        {
            try
            {
                ShowAvatarBytes(await File.ReadAllBytesAsync(cachePath));
            }
            catch (Exception)
            {
                // Cache corrompu/illisible : ignoré, le chemin réseau ci-dessous prend le relais.
            }
        }

        var errors = new List<string>();

        foreach (var buildUrl in AvatarUrlBuilders)
        {
            var url = buildUrl(uuid);
            try
            {
                var bytes = await SkinHttpClient.GetByteArrayAsync(url);
                ShowAvatarBytes(bytes);

                try
                {
                    Directory.CreateDirectory(AvatarCacheDirectory);
                    await File.WriteAllBytesAsync(cachePath, bytes);
                }
                catch (Exception)
                {
                    // Échec d'écriture du cache (disque plein, permissions...) : sans conséquence,
                    // l'avatar est déjà affiché depuis les bytes téléchargés ci-dessus.
                }

                return;
            }
            catch (Exception ex)
            {
                errors.Add($"{url} : {ex.Message}");
            }
        }

        var combinedError = string.Join(Environment.NewLine, errors);
        if (File.Exists(cachePath))
        {
            // Le réseau a échoué mais la version en cache affichée plus haut reste valable : pas
            // la peine d'alarmer l'utilisateur pour un simple échec de rafraîchissement.
            AppendLog($"Rafraîchissement de l'avatar impossible, version en cache conservée :{Environment.NewLine}{combinedError}");
            return;
        }

        // Les deux services ont échoué et aucun cache n'existe : pas bloquant, on garde le pseudo
        // texte, mais on le rend visible (au lieu du silence complet d'avant) via un indicateur
        // survolable, en plus du journal, pour pouvoir diagnostiquer sans avoir à rouvrir le code.
        AppendLog($"Avatar Minecraft indisponible :{Environment.NewLine}{combinedError}");
        AvatarErrorHint.ToolTip = combinedError;
        AvatarErrorHint.Visibility = Visibility.Visible;
    }

    private void ShowAvatarBytes(byte[] bytes)
    {
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
    }

    /// <summary>
    /// Clic sur l'avatar du profil connecté : ouvre la page officielle de modification du skin
    /// dans le navigateur par défaut (pas d'éditeur intégré au launcher — minecraft.net gère déjà
    /// l'upload/la prévisualisation, la session de connexion du navigateur suffit).
    /// </summary>
    private void PlayerAvatarBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        OpenExternalUrl(SkinEditorUrl);

    private enum AppPage
    {
        Hub,
        Server,
        Settings,
        Logs,
        Legal,
    }

    private bool _pagesWired;

    /// <summary>
    /// Pages (v2.0.0) : Paramètres, Journaux et Mentions légales remplacent le tableau de bord dans
    /// la même fenêtre (elles étaient auparavant trois fenêtres modales séparées).
    /// </summary>
    private void ShowPage(AppPage page)
    {
        var hub = page == AppPage.Hub;
        HubPage.Visibility = hub ? Visibility.Visible : Visibility.Collapsed;
        PageHost.Visibility = hub ? Visibility.Collapsed : Visibility.Visible;
        ServerPageView.Visibility = page == AppPage.Server ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageView.Visibility = page == AppPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        LogsPageView.Visibility = page == AppPage.Logs ? Visibility.Visible : Visibility.Collapsed;
        LegalPageView.Visibility = page == AppPage.Legal ? Visibility.Visible : Visibility.Collapsed;

        MarkNavItem(NavJouer, NavJouerText, hub);
        MarkNavItem(NavServeur, NavServeurText, page == AppPage.Server);
        MarkNavItem(NavParametres, NavParametresText, page is AppPage.Settings or AppPage.Logs or AppPage.Legal);
    }

    private void MarkNavItem(Border item, TextBlock label, bool active)
    {
        item.BorderBrush = active ? (Brush)FindResource("CyanBrush") : Brushes.Transparent;
        item.Background = active ? new SolidColorBrush(Color.FromArgb(0x12, 0x00, 0xE5, 0xC7)) : Brushes.Transparent;
        label.Style = (Style)FindResource(active ? "NavItemActiveTextStyle" : "NavItemTextStyle");
    }

    private void WirePages()
    {
        if (_pagesWired)
        {
            return;
        }

        _pagesWired = true;
        SettingsPageView.Closed += (_, saved) =>
        {
            if (saved)
            {
                _settingsManager.Save(_settings);
            }

            ShowPage(AppPage.Hub);
        };
        SettingsPageView.ViewLogsRequested += (_, _) =>
        {
            LogsPageView.Initialize(_settings.GameDirectory);
            ShowPage(AppPage.Logs);
        };
        SettingsPageView.LegalRequested += (_, _) => ShowPage(AppPage.Legal);
        LogsPageView.BackRequested += (_, _) => ShowPage(AppPage.Settings);
        LegalPageView.BackRequested += (_, _) => ShowPage(AppPage.Settings);
    }

    private readonly ServerHistoryService _serverHistoryService = new(SharedHttpClient.Instance);
    private ServerHistory? _serverHistory;
    private bool _serverPageWired;

    private void OpenServerPage()
    {
        if (!_serverPageWired)
        {
            _serverPageWired = true;
            ServerPageView.NotificationsToggled += (_, enabled) =>
            {
                _settings.DesktopNotificationsEnabled = enabled;
                _settingsManager.Save(_settings);
            };
        }

        ServerPageView.Update(_settings, _lastServerStatus, _serverHistory, DateTimeOffset.UtcNow);
        ShowPage(AppPage.Server);
        _ = RunSafeAsync("Page serveur", RefreshServerPageAsync);
    }

    private async Task RefreshServerPageAsync()
    {
        _serverHistory = await _serverHistoryService.FetchAsync(_settings.ModpackManifestUrl) ?? _serverHistory;
        ServerPageView.Update(_settings, _lastServerStatus, _serverHistory, DateTimeOffset.UtcNow);
    }

    private void OpenSettings(SettingsSection section = SettingsSection.Game)
    {
        WirePages();
        SettingsPageView.Initialize(_settings, _modSyncService, _packSettings, section);
        ShowPage(AppPage.Settings);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    // Item "PARAMÈTRES" de la barre latérale : simple relais vers le même handler que l'icône ⚙
    // de la barre de titre (MouseLeftButtonUp, pas Click, car ce n'est pas un Button mais un
    // Border cliquable comme les autres items de nav).
    private void ParametresNavItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => OpenSettings();

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

    /// <summary>
    /// Bascule un mode compact (pas de redimensionnement manuel) : masque les tuiles et le blason pour ne
    /// garder que la carte serveur + lancement, et rétrécit la fenêtre en conséquence — utile sur
    /// un petit écran ou pour garder le launcher discret pendant qu'une partie tourne déjà (voir
    /// aussi le redimensionnement libre ajouté au même moment, WindowChrome dans le XAML).
    /// </summary>
    private void CompactModeButton_Click(object sender, RoutedEventArgs e) => ApplyCompactMode(!_isCompactMode);

    private void ApplyCompactMode(bool compact)
    {
        if (compact && !_isCompactMode)
        {
            _normalWidth = Width;
        }

        _isCompactMode = compact;
        TilesGrid.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        HeroCrest.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        HeroCrestColumn.Width = compact ? new GridLength(0) : GridLength.Auto;
        HeroCrestGap.Width = compact ? new GridLength(0) : new GridLength(24);
        Grid.SetRow(HeroPlayBlock, compact ? 1 : 0);
        Grid.SetColumn(HeroPlayBlock, compact ? 0 : 2);
        Grid.SetColumnSpan(HeroPlayBlock, compact ? 5 : 1);
        HeroPlayBlock.Margin = compact ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        Width = compact ? Math.Max(MinWidth, 640) : _normalWidth;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AppendLog(string message)
    {
        // À appeler sur le thread UI. Seul le dernier message est affiché, façon écran de
        // chargement. Il n'y a plus de journal texte caché derrière : un TextBox masqué recevait
        // chaque ligne de progression et chaque ligne de sortie du jeu sans jamais être affiché,
        // grossissait sans limite et coûtait de plus en plus cher à chaque ajout (une des causes
        // du gel pendant les téléchargements). Les erreurs utiles vont dans launcher.log.
        LoadingSpinner.Visibility = LaunchSteps.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        LoadingStatusText.Foreground = (Brush)FindResource("InkDimBrush");
        LoadingStatusText.Text = message;
    }

    /// <summary>Indicateur visible ("dernière synchro : il y a...") lu depuis le cache local du
    /// modpack, sans requête réseau — appelé au démarrage et après chaque tentative de sync.</summary>
    private void RefreshLastSyncText()
    {
        var lastSyncedAt = _modSyncService.GetLastSyncedAt(_settings.GameDirectory);
        LastSyncText.Text = lastSyncedAt is null
            ? "SYNCHRO : JAMAIS"
            : $"SYNCHRO : {FormatRelativeTime(lastSyncedAt.Value).ToUpperInvariant()}";
    }

    private static string FormatRelativeTime(DateTimeOffset instant)
    {
        var elapsed = DateTimeOffset.Now - instant;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "à l'instant";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"il y a {(int)elapsed.TotalMinutes} min";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"il y a {(int)elapsed.TotalHours} h";
        }

        return $"il y a {(int)elapsed.TotalDays} j";
    }

    private void ShowLoadingError(string message)
    {
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingSpinner.Visibility = Visibility.Collapsed;
        LoadingStatusText.Foreground = (Brush)FindResource("MagentaBrush");
        LoadingStatusText.Text = message;
    }
}
