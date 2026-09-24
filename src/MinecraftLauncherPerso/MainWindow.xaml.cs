using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CmlLib.Core;
using CmlLib.Core.ProcessBuilder;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Auth;
using MinecraftLauncherPerso.Services.Configuration;
using MinecraftLauncherPerso.Services.Diagnostics;
using MinecraftLauncherPerso.Services.Forge;
using MinecraftLauncherPerso.Services.Http;
using MinecraftLauncherPerso.Services.Java;
using MinecraftLauncherPerso.Services.Launch;
using MinecraftLauncherPerso.Services.Maintenance;
using MinecraftLauncherPerso.Services.ModSync;
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
    private readonly IServerStatusService _serverStatusService;
    private readonly IUpdateService _updateService;
    private readonly INewsService _newsService;
    private readonly NewsHistoryStore _newsHistoryStore;
    private readonly IMaintenanceService _maintenanceService;
    private readonly SettingsManager _settingsManager;
    private readonly DispatcherTimer _serverStatusTimer;
    private readonly DispatcherTimer _updateCheckTimer;
    private readonly DispatcherTimer _newsRefreshTimer;
    private readonly DispatcherTimer _maintenanceRefreshTimer;
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

    // Tampon borné de la sortie console du jeu (stdout/stderr), pour CrashDiagnosisService sur une
    // sortie anormale — pas la peine de le relire depuis le disque, gameOutput passe déjà par ici.
    private const int CrashBufferMaxLines = 400;
    private readonly Queue<string> _gameOutputBuffer = new();

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
        _serverStatusService = new ServerStatusService();
        _updateService = new GitHubUpdateService(SharedHttpClient.Instance);
        _newsService = new NewsService(SharedHttpClient.Instance);
        _newsHistoryStore = new NewsHistoryStore();
        _newsHistory = _newsHistoryStore.Load();
        _maintenanceService = new MaintenanceService(SharedHttpClient.Instance);

        _serverStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _serverStatusTimer.Tick += async (_, _) => await RefreshServerStatusAsync();

        // Re-vérifie une éventuelle mise à jour du launcher toutes les minutes : sans ça, il
        // fallait fermer/rouvrir le launcher pour la détecter (elle n'était vérifiée qu'au
        // démarrage). CheckForUpdateAsync ne fait rien de plus si une mise à jour est déjà
        // détectée et en attente, pour ne pas re-notifier/re-sonner toutes les minutes.
        _updateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _updateCheckTimer.Tick += async (_, _) => await CheckForUpdateAsync();

        // Contrairement au statut serveur et aux mises à jour, les actus n'étaient chargées
        // qu'au démarrage : une actu postée pendant que le launcher est déjà ouvert n'apparaissait
        // qu'au redémarrage suivant. Même principe de polling, cadence plus lâche (contenu qui
        // change rarement).
        _newsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _newsRefreshTimer.Tick += async (_, _) => await ShowNewsAsync();

        // Même cadence que les actus : une maintenance annoncée pendant que le launcher est déjà
        // ouvert doit apparaître sans avoir à redémarrer le launcher.
        _maintenanceRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _maintenanceRefreshTimer.Tick += async (_, _) => await RefreshMaintenanceBannerAsync();

        // L'avatar (cache disque depuis v1.6.0) n'était rafraîchi qu'aux moments où
        // ShowConnectedPlayer était appelée (connexion, restauration de session au démarrage) :
        // si le joueur change de skin sur minecraft.net pendant une session déjà longue du
        // launcher, rien ne le détecte avant le prochain redémarrage. Re-vérifie périodiquement
        // tant qu'un profil est affiché.
        _avatarRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _avatarRefreshTimer.Tick += async (_, _) =>
        {
            if (_connectedUuid is not null)
            {
                await LoadPlayerAvatarAsync(_connectedUuid);
            }
        };
        _avatarRefreshTimer.Start();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
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
        _settingsManager.Save(_settings);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ServerNameText.Text = _settings.ServerName.ToUpperInvariant();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "LAUNCHER" : $"LAUNCHER v{version.Major}.{version.Minor}.{version.Build}";
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

        await ShowNewsAsync();
        await RefreshMaintenanceBannerAsync();
        _serverStatusTimer.Start();
        _updateCheckTimer.Start();
        _newsRefreshTimer.Start();
        _maintenanceRefreshTimer.Start();
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

        StartModpackPrefetch();
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
        var news = await _newsService.FetchNewsAsync(_settings.ModpackZipUrl);
        if (news is not null)
        {
            _newsHistory = _newsHistoryStore.RecordIfNew(_newsHistory, news);
        }

        NewsEmptyText.Visibility = _newsHistory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NewsHistoryList.ItemsSource = _newsHistory
            .Select(entry => new NewsHistoryItem(entry.FetchedAt.ToLocalTime().ToString("dd/MM HH:mm"), entry.Content))
            .ToList();
    }

    /// <summary>Vue d'affichage d'une NewsHistoryEntry, avec l'horodatage déjà mis en forme pour le binding XAML.</summary>
    private sealed record NewsHistoryItem(string FetchedAtLabel, string Content);

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

        var message = await _maintenanceService.FetchMaintenanceMessageAsync(_settings.MaintenanceMessageUrl);
        if (message is null)
        {
            MaintenanceBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var rawLines = message.Replace("\r\n", "\n").Split('\n');
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

    // Biseau aux coins haut-gauche/bas-droit (mockup Option B), recalculé à chaque changement de
    // taille puisque, contrairement à PrimaryButtonTemplate (bouton à taille fixe), cette carte
    // épouse la largeur de la colonne du dashboard.
    private const double MaintenanceBannerBevel = 14;

    private void MaintenanceBanner_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        var height = e.NewSize.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var bevel = Math.Min(MaintenanceBannerBevel, Math.Min(width, height) / 2);
        MaintenanceBannerShape.Points = new PointCollection
        {
            new Point(bevel, 0),
            new Point(width, 0),
            new Point(width, height - bevel),
            new Point(width - bevel, height),
            new Point(0, height),
            new Point(0, bevel),
        };
    }

    // ACTUS/SERVEUR n'ouvrent pas un écran séparé (tout est déjà visible sur ce tableau de bord
    // à deux colonnes) : un clic fait juste pulser la carte correspondante pour donner un vrai
    // effet à ces items de nav, au lieu de rester des libellés inertes.
    private void ActusNavItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => FlashCard(NewsCardScale);

    private void ServeurNavItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => FlashCard(ServerCardScale);

    // Active/Espace au clavier pour les items de nav (Border, pas Button : pas de KeyDown par
    // défaut) — seule concession "accessibilité" ajoutée ici, le reste (lecteur d'écran, chrome
    // de fenêtre natif) resterait un chantier bien plus large.
    private void NavItem_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Space)
        {
            return;
        }

        if (ReferenceEquals(sender, NavActus))
        {
            FlashCard(NewsCardScale);
        }
        else if (ReferenceEquals(sender, NavServeur))
        {
            FlashCard(ServerCardScale);
        }
        else if (ReferenceEquals(sender, NavParametres))
        {
            SettingsButton_Click(sender, e);
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
        _gameOutputBuffer.Clear();
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

        try
        {
            // Chaque étape est enveloppée séparément : auparavant, un seul catch générique en bas
            // de méthode affichait juste ex.Message, sans dire quelle étape (Java ? Forge ? sync
            // mods ? auth ?) avait échoué — impossible à diagnostiquer pour l'utilisateur sans
            // aller lire StatusLogTextBox en détail.

            // 1. Java 17 : seule étape avec une progression chiffrée (téléchargement), pilote la barre.
            var javaProgress = new Progress<JavaSetupProgress>(report =>
            {
                ProgressBar.Value = report.PercentComplete;
                AppendLog(report.Message);
            });
            string javaPath;
            try
            {
                javaPath = await _javaManager.EnsureJavaAsync(javaProgress);
            }
            catch (Exception ex)
            {
                throw new LauncherStepException("Préparation de Java 17", ex);
            }
            AppendLog($"Java 17 prêt : {javaPath}");

            var minecraftPath = new MinecraftPath(_settings.GameDirectory);
            var launcher = new MinecraftLauncher(minecraftPath);

            // 2. Forge : CmlLib expose déjà une progression en octets (ByteProgress), jusqu'ici
            // seulement transformée en texte — une vraie barre par téléchargement plutôt qu'un
            // indicateur indéterminé pendant toute l'étape.
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
            var forgeProgress = new Progress<string>(AppendLog);
            var forgeDownloadProgress = new Progress<double>(fraction => ProgressBar.Value = fraction * 100);
            string versionId;
            try
            {
                versionId = await _forgeManager.EnsureForgeInstalledAsync(
                    launcher, _settings.MinecraftVersion, _settings.ForgeVersion, forgeProgress, forgeDownloadProgress);
            }
            catch (Exception ex)
            {
                throw new LauncherStepException("Installation de Forge", ex);
            }
            AppendLog($"Forge prêt : {versionId}");

            // 3. Synchronisation mods/config depuis le VPS : même principe, vraie barre plutôt
            // qu'indéterminée (ne progresse que pendant un téléchargement effectif ; reste à 0 si
            // le modpack est déjà à jour, ce qui ne dure qu'un instant de toute façon).
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
            var syncProgress = new Progress<string>(AppendLog);
            var syncDownloadProgress = new Progress<double>(fraction => ProgressBar.Value = fraction * 100);
            try
            {
                await _modSyncService.SyncAsync(_settings.ModpackZipUrl, _settings.ModpackManifestUrl, _settings.GameDirectory, syncProgress, syncDownloadProgress);
            }
            catch (Exception ex)
            {
                throw new LauncherStepException("Synchronisation des mods", ex);
            }
            RefreshLastSyncText();

            // Étapes suivantes (auth, lancement) : pas de progression chiffrée, barre indéterminée.
            ProgressBar.IsIndeterminate = true;

            // 4. Authentification Microsoft directe (Xbox Live -> XSTS -> Minecraft)
            var authProgress = new Progress<string>(AppendLog);
            MinecraftSession session;
            try
            {
                session = await _authService.GetActiveSessionAsync(authProgress);
            }
            catch (Exception ex)
            {
                throw new LauncherStepException("Connexion Microsoft", ex);
            }
            AppendLog($"Connecté en tant que {session.Username}.");
            ShowConnectedPlayer(session);

            // 5. Verrouille la liste multijoueur sur Astral Nexus (voir LauncherSettings.ServerHost)
            try
            {
                if (!string.IsNullOrWhiteSpace(_settings.ServerHost))
                {
                    ServerListWriter.WriteSingleServer(_settings.GameDirectory, _settings.ServerName, _settings.ServerHost);
                }
            }
            catch (Exception ex)
            {
                throw new LauncherStepException("Écriture de la liste des serveurs", ex);
            }

            // 6. Lancement
            AppendLog("Lancement du jeu...");
            var gameOutput = new Progress<string>(line =>
            {
                AppendLog(line);
                BufferGameOutput(line);
            });
            try
            {
                _activeGame = await _gameLauncher.LaunchAsync(launcher, versionId, session, javaPath, _settings, gameOutput);
            }
            catch (Exception ex)
            {
                throw new LauncherStepException("Lancement du jeu", ex);
            }

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            AppendLog("Jeu lancé.");
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
            _playButtonWatchdog?.Stop();
            PlayButton.IsEnabled = true;

            if (exitCode != 0)
            {
                AppendLog($"Le jeu s'est arrêté de façon inattendue (code {exitCode}).");
                ViewLogsButton.Visibility = Visibility.Visible;
                SystemSounds.Hand.Play();
                ShowCrashDiagnosisIfAny();
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
    private void ShowCrashDiagnosisIfAny()
    {
        var diagnosis = CrashDiagnosisService.Diagnose(_gameOutputBuffer);
        if (diagnosis is null)
        {
            return;
        }

        AppendLog($"Diagnostic : {diagnosis.Message}");

        if (!diagnosis.SuggestsRepair)
        {
            MessageBox.Show(diagnosis.Message, "Le jeu s'est arrêté", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"{diagnosis.Message}\n\nLancer une réparation du modpack maintenant ?",
            "Le jeu s'est arrêté",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            SettingsButton_Click(this, new RoutedEventArgs());
        }
    }

    private void BufferGameOutput(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        _gameOutputBuffer.Enqueue(line);
        while (_gameOutputBuffer.Count > CrashBufferMaxLines)
        {
            _gameOutputBuffer.Dequeue();
        }
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

    /// <summary>
    /// Enveloppe l'exception d'origine d'une étape du pipeline de lancement (Java, Forge, sync
    /// mods, auth, écriture servers.dat, lancement du jeu) avec le nom de l'étape : sans ça, le
    /// seul catch générique en bas de PlayButton_Click affichait juste ex.Message, impossible à
    /// rattacher à une étape précise pour l'utilisateur.
    /// </summary>
    private sealed class LauncherStepException(string step, Exception inner)
        : Exception($"{step} : {inner.Message}", inner)
    {
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
        if (wasOffline && status.IsOnline && _settings.DesktopNotificationsEnabled)
        {
            DesktopNotificationService.Show(_settings.ServerName, "Le serveur est de nouveau en ligne.");
        }

        ServerStatusText.Text = status.IsOnline ? "EN LIGNE" : "HORS LIGNE";
        // Sur toute la ligne (pastille + texte), pas juste le texte : cible de survol trop étroite
        // sinon pour qu'on tombe dessus de façon fiable.
        ServerStatusRow.ToolTip = status.IsOnline ? null : status.ErrorDetail;
        ServerStatusRow.Cursor = status.IsOnline ? null : Cursors.Help;
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
        var window = new SettingsWindow(_settings, _modSyncService) { Owner = this };
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

    /// <summary>
    /// Bascule un mode compact (pas de redimensionnement manuel) : masque la colonne ACTUS pour ne
    /// garder que la colonne serveur + lancement, et rétrécit la fenêtre en conséquence — utile sur
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
        NewsColumn.Width = compact ? new GridLength(0) : new GridLength(1.3, GridUnitType.Star);
        GapColumn.Width = compact ? new GridLength(0) : new GridLength(18);
        Width = compact ? Math.Max(MinWidth, 640) : _normalWidth;
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
