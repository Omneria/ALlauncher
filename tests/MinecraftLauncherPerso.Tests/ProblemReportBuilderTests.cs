using MinecraftLauncherPerso.Services.Diagnostics;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// Rapport "SIGNALER UN PROBLÈME" (v1.15.0) : RAM du PC, liste des mods, fin du log du jeu, sur
/// un dossier de jeu temporaire.
/// </summary>
public sealed class ProblemReportBuilderTests : IDisposable
{
    private readonly string _gameDirectory = Path.Combine(Path.GetTempPath(), $"al-launcher-report-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_gameDirectory))
        {
            Directory.Delete(_gameDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_liste_les_mods_la_ram_et_la_fin_du_log_du_jeu()
    {
        Directory.CreateDirectory(Path.Combine(_gameDirectory, "mods"));
        File.WriteAllText(Path.Combine(_gameDirectory, "mods", "create-1.20.1-6.0.8.jar"), "");
        File.WriteAllText(Path.Combine(_gameDirectory, "mods", "Quark-4.0-462.jar"), "");
        File.WriteAllText(Path.Combine(_gameDirectory, "mods", "notes.txt"), ""); // pas un mod
        Directory.CreateDirectory(Path.Combine(_gameDirectory, "logs"));
        var gameLog = Enumerable.Range(1, 80).Select(i => $"ligne {i}");
        File.WriteAllLines(Path.Combine(_gameDirectory, "logs", "latest.log"), gameLog);

        var report = await ProblemReportBuilder.BuildAsync(Info(physicalMemoryMb: 16384));

        Assert.Contains("RAM du PC : 16 Go", report);
        Assert.Contains("Mods installés (2)", report);
        Assert.Contains("create-1.20.1-6.0.8.jar", report);
        Assert.Contains("Quark-4.0-462.jar", report);
        Assert.DoesNotContain("notes.txt", report);
        Assert.Contains("ligne 80", report);
        Assert.Contains($"ligne {80 - ProblemReportBuilder.GameLogLineCount + 1}", report);
        Assert.DoesNotContain($"ligne {80 - ProblemReportBuilder.GameLogLineCount}{Environment.NewLine}", report);
    }

    [Fact]
    public async Task BuildAsync_reste_utile_sans_mods_ni_logs()
    {
        var report = await ProblemReportBuilder.BuildAsync(Info(physicalMemoryMb: null));

        Assert.Contains("RAM du PC : inconnue", report);
        Assert.Contains("dossier mods/ absent", report);
        Assert.Contains("(aucun logs/latest.log pour l'instant)", report);
        Assert.Contains("(aucun launcher.log pour l'instant)", report);
    }

    [Fact]
    public async Task ReadLastLinesAsync_lit_un_fichier_ouvert_en_ecriture_par_le_jeu()
    {
        Directory.CreateDirectory(_gameDirectory);
        var path = Path.Combine(_gameDirectory, "latest.log");
        await using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await writer.WriteAsync("en cours\n"u8.ToArray());
        await writer.FlushAsync();

        var lines = await ProblemReportBuilder.ReadLastLinesAsync(path, 10, "latest.log");

        Assert.Equal(["en cours"], lines);
    }

    [Fact]
    public void HideUserProfilePath_masque_le_nom_de_session_windows()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrEmpty(profile));

        var text = ProblemReportBuilder.HideUserProfilePath(Path.Combine(profile, "AppData", "x.log"));

        Assert.DoesNotContain(profile, text);
        Assert.StartsWith("%USERPROFILE%", text);
    }

    private ProblemReportInfo Info(int? physicalMemoryMb) => new(
        "1.15.0",
        physicalMemoryMb,
        2048,
        6144,
        "astralnexusmc.duckdns.org:25566",
        null,
        _gameDirectory,
        Path.Combine(_gameDirectory, "launcher.log"));
}
