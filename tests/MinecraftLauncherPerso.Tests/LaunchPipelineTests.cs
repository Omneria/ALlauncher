using System.Diagnostics;
using CmlLib.Core;
using CmlLib.Core.ProcessBuilder;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Auth;
using MinecraftLauncherPerso.Services.Forge;
using MinecraftLauncherPerso.Services.Java;
using MinecraftLauncherPerso.Services.Launch;
using MinecraftLauncherPerso.Services.ModSync;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// LaunchPipeline (v1.11.0) : l'orchestration Java → Forge → synchro → auth → servers.dat →
/// lancement, jusqu'ici enfouie dans MainWindow.PlayButton_Click (intestable : une Window WPF ne
/// s'instancie pas sous xUnit). Faux services enregistrant les appels, aucun réseau ni CmlLib réel
/// (fabrique de MinecraftLauncher court-circuitée, les faux ignorent cet argument).
/// </summary>
public sealed class LaunchPipelineTests : IDisposable
{
    private readonly string _gameDirectory = Path.Combine(Path.GetTempPath(), $"al-launcher-pipeline-tests-{Guid.NewGuid():N}");
    private readonly List<string> _calls = [];

    public void Dispose()
    {
        if (Directory.Exists(_gameDirectory))
        {
            Directory.Delete(_gameDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_enchaine_toutes_les_etapes_dans_lordre_et_retourne_le_resultat()
    {
        var pipeline = CreatePipeline();
        var settings = CreateSettings();
        MinecraftSession? acquired = null;
        var steps = new List<LaunchStep>();

        var result = await pipeline.RunAsync(
            settings,
            new SynchronousProgress<LaunchProgress>(p => steps.Add(p.Step)),
            session => acquired = session);

        Assert.Equal(["java", "forge", "sync", "auth", "launch"], _calls);
        Assert.Equal("C:\\java\\bin\\java.exe", result.JavaPath);
        Assert.Equal("1.20.1-forge-47.3.0", result.VersionId);
        Assert.Equal("Steve", result.Session.Username);
        Assert.NotNull(acquired);
        Assert.True(File.Exists(Path.Combine(_gameDirectory, "servers.dat"))); // ServerHost renseigné → liste écrite
        Assert.Equal([LaunchStep.Java, LaunchStep.Forge, LaunchStep.ModSync, LaunchStep.Auth, LaunchStep.ServerList, LaunchStep.Launch], steps.Distinct());
    }

    [Fact]
    public async Task RunAsync_necrit_pas_servers_dat_sans_ServerHost()
    {
        var pipeline = CreatePipeline();
        var settings = CreateSettings();
        settings.ServerHost = "";

        await pipeline.RunAsync(settings);

        Assert.False(File.Exists(Path.Combine(_gameDirectory, "servers.dat")));
    }

    [Fact]
    public async Task RunAsync_enveloppe_lechec_dune_etape_avec_son_nom_et_narrete_les_suivantes()
    {
        var pipeline = CreatePipeline(forge: new FakeForgeManager(_calls, fail: new InvalidOperationException("403 Forbidden")));

        var ex = await Assert.ThrowsAsync<LaunchStepException>(() => pipeline.RunAsync(CreateSettings()));

        Assert.Equal(LaunchStep.Forge, ex.Step);
        Assert.Contains("Installation de Forge", ex.Message);
        Assert.Contains("403 Forbidden", ex.Message);
        Assert.Equal(["java", "forge"], _calls); // ni synchro, ni auth, ni lancement après l'échec
    }

    [Fact]
    public async Task RunAsync_annule_entre_deux_etapes_sans_demarrer_la_suivante()
    {
        using var cts = new CancellationTokenSource();
        // Le faux Java annule le token en se terminant : la synchro/Forge ne doivent jamais démarrer.
        var pipeline = CreatePipeline(java: new FakeJavaManager(_calls, onDone: cts.Cancel));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.RunAsync(CreateSettings(), cancellationToken: cts.Token));

        Assert.Equal(["java"], _calls);
    }

    [Fact]
    public async Task RunAsync_laisse_remonter_une_annulation_sans_lenvelopper()
    {
        using var cts = new CancellationTokenSource();
        var pipeline = CreatePipeline(sync: new FakeModSyncService(_calls, cancelDuringSync: cts));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.RunAsync(CreateSettings(), cancellationToken: cts.Token));

        Assert.IsNotType<LaunchStepException>(ex);
        Assert.Equal(["java", "forge", "sync"], _calls);
    }

    [Theory]
    [InlineData(double.NaN)] // 0/0 : ByteProgress.ToRatio() de CmlLib quand la taille totale est inconnue
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void LaunchProgress_transforme_une_fraction_non_finie_en_progression_inconnue(double fraction)
    {
        // ProgressBar.Value lève ArgumentException sur NaN : vécu en v1.11.0 pendant l'installation
        // de Forge, rattrapé par le filet global mais visible dans launcher.log.
        Assert.Null(new LaunchProgress(LaunchStep.Forge, "x", fraction).Fraction);
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(0.42, 0.42)]
    [InlineData(1.7, 1)]
    public void LaunchProgress_borne_la_fraction_entre_0_et_1(double fraction, double expected)
    {
        Assert.Equal(expected, new LaunchProgress(LaunchStep.ModSync, "x", fraction).Fraction);
    }

    [Fact]
    public void LaunchProgress_sans_fraction_reste_null()
    {
        Assert.Null(new LaunchProgress(LaunchStep.Auth, "x").Fraction);
    }

    private LaunchPipeline CreatePipeline(
        FakeJavaManager? java = null,
        FakeForgeManager? forge = null,
        FakeModSyncService? sync = null) =>
        new(
            java ?? new FakeJavaManager(_calls),
            forge ?? new FakeForgeManager(_calls),
            sync ?? new FakeModSyncService(_calls),
            new FakeAuthService(_calls),
            new FakeGameLauncher(_calls),
            launcherFactory: _ => null!);

    private LauncherSettings CreateSettings() => new()
    {
        GameDirectory = _gameDirectory,
        ServerHost = "astralnexusmc.duckdns.org",
        ServerName = "Astral Nexus",
    };

    /// <summary>Progress<T> standard poste sur le SynchronizationContext (asynchrone) : une version
    /// synchrone évite d'attendre que les rapports arrivent avant d'inspecter la liste.</summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private sealed class FakeJavaManager(List<string> calls, Action? onDone = null) : IJavaManager
    {
        public Task<string> EnsureJavaAsync(IProgress<JavaSetupProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            calls.Add("java");
            progress?.Report(new JavaSetupProgress(JavaSetupStage.Ready, 100, "Java 17 prêt"));
            onDone?.Invoke();
            return Task.FromResult("C:\\java\\bin\\java.exe");
        }
    }

    private sealed class FakeForgeManager(List<string> calls, Exception? fail = null) : IForgeManager
    {
        public Task<string> EnsureForgeInstalledAsync(MinecraftLauncher launcher, string minecraftVersion, string forgeVersion, IProgress<string>? progress = null, IProgress<double>? downloadProgress = null, CancellationToken cancellationToken = default)
        {
            calls.Add("forge");
            if (fail is not null)
            {
                throw fail;
            }

            return Task.FromResult($"{minecraftVersion}-forge-{forgeVersion}");
        }
    }

    private sealed class FakeModSyncService(List<string> calls, CancellationTokenSource? cancelDuringSync = null) : IModSyncService
    {
        public Task SyncAsync(string modpackZipUrl, string? manifestUrl, string gameDirectory, IProgress<string>? progress = null, IProgress<double>? downloadProgress = null, CancellationToken cancellationToken = default)
        {
            calls.Add("sync");
            if (cancelDuringSync is not null)
            {
                cancelDuringSync.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.CompletedTask;
        }

        public DateTimeOffset? GetLastSyncedAt(string gameDirectory) => null;

        public Task RepairAsync(string modpackZipUrl, string? manifestUrl, string gameDirectory, IProgress<string>? progress = null, IProgress<double>? downloadProgress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PrefetchAsync(string modpackZipUrl, string? manifestUrl, string gameDirectory, IProgress<string>? progress = null, IProgress<double>? downloadProgress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public bool HasRollbackAvailable(string gameDirectory) => false;

        public Task RollbackAsync(string gameDirectory, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAuthService(List<string> calls) : IAuthService
    {
        public Task<MinecraftSession> GetActiveSessionAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            calls.Add("auth");
            return Task.FromResult(new MinecraftSession("Steve", "00000000-0000-0000-0000-000000000000", "token"));
        }

        public Task<MinecraftSession?> TryGetCachedSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult<MinecraftSession?>(null);
    }

    private sealed class FakeGameLauncher(List<string> calls) : IGameLauncher
    {
        public event EventHandler<int>? GameExited;

        public Task<ProcessWrapper> LaunchAsync(MinecraftLauncher launcher, string versionId, MinecraftSession session, string javaExecutablePath, LauncherSettings settings, IProgress<string>? gameOutput = null, CancellationToken cancellationToken = default)
        {
            calls.Add("launch");
            GameExited?.Invoke(this, 0); // évite l'avertissement "événement jamais utilisé"
            return Task.FromResult(new ProcessWrapper(new Process()));
        }
    }
}
