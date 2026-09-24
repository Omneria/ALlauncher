using CmlLib.Core;
using CmlLib.Core.ProcessBuilder;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Auth;
using MinecraftLauncherPerso.Services.Forge;
using MinecraftLauncherPerso.Services.Java;
using MinecraftLauncherPerso.Services.ModSync;

namespace MinecraftLauncherPerso.Services.Launch;

/// <summary>Étapes du lancement, dans l'ordre où <see cref="LaunchPipeline"/> les exécute.</summary>
public enum LaunchStep
{
    Java,
    Forge,
    ModSync,
    Auth,
    ServerList,
    Launch,
}

/// <summary>
/// Progression rapportée par le pipeline : l'étape courante, un message lisible, et une fraction
/// 0-1 quand l'étape a une progression chiffrée (téléchargement), null sinon (l'UI affiche alors
/// un indicateur indéterminé).
/// </summary>
public sealed record LaunchProgress(LaunchStep Step, string Message, double? Fraction = null)
{
    /// <summary>
    /// Toujours null ou dans [0, 1] : une fraction non finie (NaN, infini) devient null, donc
    /// "progression inconnue", au lieu d'atteindre ProgressBar.Value, qui lève ArgumentException
    /// sur NaN. Vécu en v1.11.0 : CmlLib rapporte ByteProgress.ToRatio() = 0/0 = NaN quand la
    /// taille totale d'un téléchargement Forge est encore inconnue.
    /// </summary>
    public double? Fraction { get; init; } = Fraction is { } value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : null;
}

/// <summary>Ce que le pipeline a produit une fois le jeu démarré.</summary>
public sealed record LaunchResult(MinecraftSession Session, ProcessWrapper Game, string JavaPath, string VersionId);

/// <summary>
/// Enveloppe l'exception d'origine d'une étape du pipeline avec le nom de l'étape : sans ça, un
/// message brut ("403 Forbidden", "fichier introuvable") est impossible à rattacher à Java, Forge,
/// la synchro ou l'auth pour un joueur. Une annulation (OperationCanceledException) n'est jamais
/// enveloppée : elle remonte telle quelle pour que l'appelant la distingue d'une erreur.
/// </summary>
public sealed class LaunchStepException(LaunchStep step, Exception inner)
    : Exception($"{LaunchPipeline.DescribeStep(step)} : {inner.Message}", inner)
{
    public LaunchStep Step { get; } = step;
}

/// <summary>
/// Orchestration complète d'un clic sur JOUER — Java 17 → Forge → synchro mods/config → session
/// Microsoft → servers.dat → démarrage du jeu — sortie de MainWindow (v1.11.0) pour être testable
/// sans WPF (voir LaunchPipelineTests) et annulable proprement : le même CancellationToken
/// traverse toutes les étapes, et un <see cref="OperationCanceledException"/> arrête le pipeline
/// là où il en est, sans démarrer les étapes suivantes.
/// </summary>
public sealed class LaunchPipeline
{
    private readonly IJavaManager _javaManager;
    private readonly IForgeManager _forgeManager;
    private readonly IModSyncService _modSyncService;
    private readonly IAuthService _authService;
    private readonly IGameLauncher _gameLauncher;
    private readonly Func<string, MinecraftLauncher> _launcherFactory;

    /// <param name="launcherFactory">
    /// Construit le MinecraftLauncher (CmlLib) pour un dossier de jeu donné. Optionnel : les tests
    /// injectent un faux pour ne pas dépendre de CmlLib (les faux services ignorent cet argument).
    /// </param>
    public LaunchPipeline(
        IJavaManager javaManager,
        IForgeManager forgeManager,
        IModSyncService modSyncService,
        IAuthService authService,
        IGameLauncher gameLauncher,
        Func<string, MinecraftLauncher>? launcherFactory = null)
    {
        _javaManager = javaManager;
        _forgeManager = forgeManager;
        _modSyncService = modSyncService;
        _authService = authService;
        _gameLauncher = gameLauncher;
        _launcherFactory = launcherFactory ?? (gameDirectory => new MinecraftLauncher(new MinecraftPath(gameDirectory)));
    }

    /// <summary>
    /// Exécute toutes les étapes. Lève <see cref="LaunchStepException"/> (étape identifiée) en cas
    /// d'échec, <see cref="OperationCanceledException"/> si <paramref name="cancellationToken"/> est
    /// annulé entre ou pendant deux étapes.
    /// </summary>
    /// <param name="onSessionAcquired">Appelé dès que la session Microsoft est obtenue (avant le
    /// démarrage du jeu), pour afficher le profil connecté sans attendre la fin.</param>
    /// <param name="gameOutput">Sortie console du jeu, ligne par ligne, une fois démarré.</param>
    public async Task<LaunchResult> RunAsync(
        LauncherSettings settings,
        IProgress<LaunchProgress>? progress = null,
        Action<MinecraftSession>? onSessionAcquired = null,
        IProgress<string>? gameOutput = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Java 17 : seule étape avec une progression chiffrée dès le départ (téléchargement).
        var javaProgress = new Relay<JavaSetupProgress>(report =>
            progress?.Report(new LaunchProgress(LaunchStep.Java, report.Message, report.PercentComplete / 100)));
        AnnounceStep(progress, LaunchStep.Java);
        var javaPath = await RunStepAsync(LaunchStep.Java, cancellationToken,
            () => _javaManager.EnsureJavaAsync(javaProgress, cancellationToken));
        progress?.Report(new LaunchProgress(LaunchStep.Java, $"Java 17 prêt : {javaPath}", 1));

        var launcher = _launcherFactory(settings.GameDirectory);

        // 2. Forge : progression en octets exposée par CmlLib.
        var forgeProgress = new Relay<string>(message => progress?.Report(new LaunchProgress(LaunchStep.Forge, message)));
        var forgeDownloadProgress = new Relay<double>(fraction => progress?.Report(new LaunchProgress(LaunchStep.Forge, "Téléchargement de Forge...", fraction)));
        AnnounceStep(progress, LaunchStep.Forge);
        var versionId = await RunStepAsync(LaunchStep.Forge, cancellationToken,
            () => _forgeManager.EnsureForgeInstalledAsync(launcher, settings.MinecraftVersion, settings.ForgeVersion, forgeProgress, forgeDownloadProgress, cancellationToken));
        progress?.Report(new LaunchProgress(LaunchStep.Forge, $"Forge prêt : {versionId}", 1));

        // 3. Synchronisation mods/config depuis le VPS.
        var syncProgress = new Relay<string>(message => progress?.Report(new LaunchProgress(LaunchStep.ModSync, message)));
        var syncDownloadProgress = new Relay<double>(fraction => progress?.Report(new LaunchProgress(LaunchStep.ModSync, "Téléchargement du modpack...", fraction)));
        AnnounceStep(progress, LaunchStep.ModSync);
        await RunVoidStepAsync(LaunchStep.ModSync, cancellationToken,
            () => _modSyncService.SyncAsync(settings.ModpackZipUrl, settings.ModpackManifestUrl, settings.GameDirectory, syncProgress, syncDownloadProgress, cancellationToken));

        // 4. Session Microsoft (Xbox Live -> XSTS -> Minecraft) : pas de progression chiffrée.
        var authProgress = new Relay<string>(message => progress?.Report(new LaunchProgress(LaunchStep.Auth, message)));
        AnnounceStep(progress, LaunchStep.Auth);
        var session = await RunStepAsync(LaunchStep.Auth, cancellationToken,
            () => _authService.GetActiveSessionAsync(authProgress, cancellationToken));
        progress?.Report(new LaunchProgress(LaunchStep.Auth, $"Connecté en tant que {session.Username}."));
        onSessionAcquired?.Invoke(session);

        // 5. Verrouille la liste multijoueur sur le serveur configuré (voir LauncherSettings.ServerHost).
        if (!string.IsNullOrWhiteSpace(settings.ServerHost))
        {
            AnnounceStep(progress, LaunchStep.ServerList);
            await RunVoidStepAsync(LaunchStep.ServerList, cancellationToken, () =>
            {
                ServerListWriter.WriteSingleServer(settings.GameDirectory, settings.ServerName, ServerListWriter.FormatAddress(settings.ServerHost, settings.ServerPort));
                return Task.CompletedTask;
            });
        }

        // 6. Démarrage du jeu.
        AnnounceStep(progress, LaunchStep.Launch);
        var game = await RunStepAsync(LaunchStep.Launch, cancellationToken,
            () => _gameLauncher.LaunchAsync(launcher, versionId, session, javaPath, settings, gameOutput, cancellationToken));
        progress?.Report(new LaunchProgress(LaunchStep.Launch, "Jeu lancé.", 1));

        return new LaunchResult(session, game, javaPath, versionId);
    }

    /// <summary>Signale le début d'une étape avant même son premier rapport de progression : une
    /// étape silencieuse au démarrage (synchro déjà à jour, session en cache) reste ainsi visible
    /// dans le journal, et l'UI peut réinitialiser sa barre sur ce changement d'étape.</summary>
    private static void AnnounceStep(IProgress<LaunchProgress>? progress, LaunchStep step) =>
        progress?.Report(new LaunchProgress(step, $"{DescribeStep(step)}..."));

    private static async Task<T> RunStepAsync<T>(LaunchStep step, CancellationToken cancellationToken, Func<Task<T>> action)
    {
        // Vérifié AVANT chaque étape : une annulation demandée pendant l'étape précédente (qui a pu
        // se terminer sans consulter le token) ne doit pas laisser la suivante démarrer.
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new LaunchStepException(step, ex);
        }
    }

    private static async Task RunVoidStepAsync(LaunchStep step, CancellationToken cancellationToken, Func<Task> action)
    {
        await RunStepAsync(step, cancellationToken, async () =>
        {
            await action();
            return true;
        });
    }

    /// <summary>
    /// Relaie un rapport de progression immédiatement, sur le thread de l'appelant. Pas de
    /// Progress&lt;T&gt; ici : le pipeline tourne hors du thread UI (voir MainWindow), où Progress&lt;T&gt;
    /// posterait chaque rapport sur le pool de threads, dans le désordre (un vieux "42 %" pouvait
    /// arriver après "Java 17 prêt"). C'est l'IProgress final, fourni par l'appelant, qui décide
    /// comment et quand rejoindre l'interface (CoalescingProgress dans le launcher).
    /// </summary>
    private sealed class Relay<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    public static string DescribeStep(LaunchStep step) => step switch
    {
        LaunchStep.Java => "Préparation de Java 17",
        LaunchStep.Forge => "Installation de Forge",
        LaunchStep.ModSync => "Synchronisation des mods",
        LaunchStep.Auth => "Connexion Microsoft",
        LaunchStep.ServerList => "Écriture de la liste des serveurs",
        LaunchStep.Launch => "Lancement du jeu",
        _ => step.ToString(),
    };
}
