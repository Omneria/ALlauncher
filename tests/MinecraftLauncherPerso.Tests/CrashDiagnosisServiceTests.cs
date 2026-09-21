using MinecraftLauncherPerso.Services.Diagnostics;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// CrashDiagnosisService (v1.8.0) : diagnostic best-effort à partir de la sortie console du jeu.
/// Couvre les motifs reconnus (pour vérifier qu'ils déclenchent bien le bon message/la bonne
/// suggestion de réparation) et le cas "rien de reconnu" (ne doit jamais inventer une explication).
/// </summary>
public sealed class CrashDiagnosisServiceTests
{
    [Fact]
    public void Diagnose_retourne_null_si_aucun_motif_connu()
    {
        var diagnosis = CrashDiagnosisService.Diagnose(["[INFO] Setting user...", "[INFO] LWJGL Version: 3.2.2"]);

        Assert.Null(diagnosis);
    }

    [Fact]
    public void Diagnose_detecte_OutOfMemoryError_sans_suggerer_de_reparation()
    {
        var diagnosis = CrashDiagnosisService.Diagnose(["Exception in thread \"main\" java.lang.OutOfMemoryError: Java heap space"]);

        Assert.NotNull(diagnosis);
        Assert.False(diagnosis.SuggestsRepair);
    }

    [Fact]
    public void Diagnose_detecte_un_mod_corrompu_et_suggere_une_reparation()
    {
        var diagnosis = CrashDiagnosisService.Diagnose(["net.minecraftforge.fml.common.MissingModsException: missing dependency"]);

        Assert.NotNull(diagnosis);
        Assert.True(diagnosis.SuggestsRepair);
    }

    [Fact]
    public void Diagnose_detecte_un_jar_corrompu_zip_et_suggere_une_reparation()
    {
        var diagnosis = CrashDiagnosisService.Diagnose(["java.util.zip.ZipException: zip END header not found"]);

        Assert.NotNull(diagnosis);
        Assert.True(diagnosis.SuggestsRepair);
    }
}
