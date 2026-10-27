using System.IO;
using MinecraftLauncherPerso.Services.Diagnostics;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// Rapport natif de la JVM (hs_err_pid*.log) : le launcher en tire la cause probable pour le joueur.
/// Les en-têtes ci-dessous sont ceux de vrais rapports (crash du 27/10/2026 dans le code JIT de
/// mezz_config), allégés des chemins personnels.
/// </summary>
public sealed class NativeCrashReportTests : IDisposable
{
    private static readonly string[] JitHeader =
    [
        "#",
        "# A fatal error has been detected by the Java Runtime Environment:",
        "#",
        "#  EXCEPTION_ACCESS_VIOLATION (0xc0000005) at pc=0x000001d513171f51, pid=18380, tid=2316",
        "#",
        "# JRE version: Java(TM) SE Runtime Environment (17.0.12+8) (build 17.0.12+8-LTS-286)",
        "# Java VM: Java HotSpot(TM) 64-Bit Server VM (17.0.12+8-LTS-286, mixed mode, sharing, tiered, compressed oops, compressed class ptrs, g1 gc, windows-amd64)",
        "# Problematic frame:",
        "# J 29020 c2 net.mezzdev.config.schema.ConfigSchema.loadIfNeededWithoutNotifying()Lnet/mezzdev/config/schema/ConfigSchema$LoadResult; mezz_config@0.6.3 (614 bytes) @ 0x000001d513171f51 [0x000001d51316dc80+0x0000000000004",
        "#",
    ];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "native-crash-" + Guid.NewGuid().ToString("N"));

    public NativeCrashReportTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void L_en_tete_donne_la_cause_le_cadre_fautif_et_la_version_de_java()
    {
        var report = NativeCrashReport.ParseHeader("hs_err_pid18380.log", DateTime.Now, JitHeader);

        Assert.Equal("EXCEPTION_ACCESS_VIOLATION (0xc0000005)", report.ExceptionLine);
        Assert.StartsWith("J 29020 c2 net.mezzdev.config.schema.ConfigSchema.loadIfNeededWithoutNotifying()", report.ProblematicFrame);
        Assert.StartsWith("Java(TM) SE Runtime Environment (17.0.12+8)", report.JavaVersion);
    }

    [Fact]
    public void Un_cadre_JIT_est_decrit_comme_un_bug_probable_de_la_machine_virtuelle()
    {
        var description = NativeCrashReport.ParseHeader("hs_err.log", DateTime.Now, JitHeader).Describe();

        Assert.Contains("EXCEPTION_ACCESS_VIOLATION", description);
        Assert.Contains("net.mezzdev.config.schema.ConfigSchema.loadIfNeededWithoutNotifying", description);
        Assert.Contains("mettre Java à jour", description);
        Assert.DoesNotContain("pilote", description);
    }

    [Theory]
    [InlineData("atio6axx.dll")]
    [InlineData("nvoglv64.dll")]
    [InlineData("igdrcl64.dll")]
    public void Une_bibliotheque_de_pilote_graphique_est_nommee_comme_telle(string library)
    {
        string[] header =
        [
            "#  EXCEPTION_ACCESS_VIOLATION (0xc0000005) at pc=0x00007ffa1b2c3d4e, pid=1, tid=2",
            "# Problematic frame:",
            $"# C  [{library}+0x1234]",
        ];

        var description = NativeCrashReport.ParseHeader("hs_err.log", DateTime.Now, header).Describe();

        Assert.Contains(library, description);
        Assert.Contains("pilote graphique", description);
    }

    [Fact]
    public void Une_bibliotheque_non_graphique_n_est_pas_prise_pour_un_pilote()
    {
        string[] header = ["#  SIGSEGV (0xb) at pc=0x1, pid=1, tid=2", "# Problematic frame:", "# C  [libfoo.so+0x10]"];

        var description = NativeCrashReport.ParseHeader("hs_err.log", DateTime.Now, header).Describe();

        Assert.Contains("libfoo.so", description);
        Assert.DoesNotContain("pilote graphique", description);
    }

    [Fact]
    public void Sans_cadre_fautif_le_rapport_est_decrit_sans_inventer_de_cause()
    {
        var description = NativeCrashReport.ParseHeader("hs_err_pid7.log", DateTime.Now, ["# A fatal error has been detected"]).Describe();

        Assert.Contains("ne nomme pas de zone fautive", description);
    }

    [Fact]
    public void FindLatest_retient_le_rapport_le_plus_recent_apres_le_debut_de_la_partie()
    {
        var launchedAt = DateTime.Now.AddMinutes(-10);
        WriteReport("hs_err_pid1.log", DateTime.Now.AddMinutes(-60)); // d'une partie précédente
        WriteReport("hs_err_pid2.log", DateTime.Now.AddMinutes(-5));
        WriteReport("hs_err_pid3.log", DateTime.Now.AddMinutes(-1));
        File.WriteAllText(Path.Combine(_directory, "crash-2026.txt"), "pas un rapport natif");

        var report = NativeCrashReport.FindLatest([_directory], launchedAt);

        Assert.NotNull(report);
        Assert.Equal("hs_err_pid3.log", Path.GetFileName(report!.Path));
    }

    [Fact]
    public void FindLatest_ignore_les_rapports_anterieurs_au_lancement_et_les_dossiers_absents()
    {
        WriteReport("hs_err_pid1.log", DateTime.Now.AddHours(-2));

        var report = NativeCrashReport.FindLatest([_directory, Path.Combine(_directory, "inexistant"), ""], DateTime.Now.AddMinutes(-10));

        Assert.Null(report);
    }

    [Fact]
    public void L_argument_JVM_range_les_rapports_dans_crash_reports()
    {
        var argument = NativeCrashReport.ErrorFileArgument(@"D:\Games\Omnéria Games");

        Assert.StartsWith("-XX:ErrorFile=", argument);
        Assert.EndsWith("hs_err_pid%p.log", argument);
        Assert.Contains("crash-reports", argument);
    }

    private void WriteReport(string name, DateTime writtenAt)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllLines(path, JitHeader);
        File.SetLastWriteTime(path, writtenAt);
    }
}
