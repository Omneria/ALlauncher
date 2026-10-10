using MinecraftLauncherPerso.ViewModels;
using MinecraftLauncherPerso.Services.Launch;
using MinecraftLauncherPerso.Services.ModSync;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>Contenu des tuiles du hub : changelog du pack, nombre de mods, actu, résumés d'étapes.</summary>
public sealed class HubContentTests
{
    [Fact]
    public void Le_changelog_du_pack_est_range_en_ajouts_retraits_et_mises_a_jour()
    {
        var summary = ModpackChangeSummary.From(
        [
            "Ajouté : Create Steam & Rails",
            "Ajouté : Sophisticated Backpacks",
            "Retiré : Journeymap",
            "Mis à jour : Create 0.5.0 → Create 0.5.1",
            "Mis à jour : Jade",
            "Texte libre sans deux-points",
        ]);

        Assert.Equal(["Create Steam & Rails", "Sophisticated Backpacks"], summary.Added);
        Assert.Equal(["Journeymap"], summary.Removed);
        Assert.Equal(["Create 0.5.1", "Jade"], summary.Updated);
    }

    [Fact]
    public void Le_nombre_de_mods_ne_compte_que_les_jars_de_mods()
    {
        var manifest = new ModpackManifest();
        manifest.Files["mods/create.jar"] = new ModpackManifestFile();
        manifest.Files["mods\\jade.jar"] = new ModpackManifestFile();
        manifest.Files["mods/readme.txt"] = new ModpackManifestFile();
        manifest.Files["config/create-common.toml"] = new ModpackManifestFile();

        Assert.Equal(2, ModSyncService.CountMods(manifest));
    }

    [Fact]
    public void La_premiere_ligne_dune_actu_est_son_titre()
    {
        Assert.Equal(("Event PvE samedi", "RDV au spawn à 21h."), TilesViewModel.SplitNews("Event PvE samedi\n\nRDV au spawn\nà 21h."));
        Assert.Equal(("", "Maintenance ce soir"), TilesViewModel.SplitNews("Maintenance ce soir"));
        Assert.Equal(("", ""), TilesViewModel.SplitNews("  \n "));
    }

    [Fact]
    public void Une_etape_terminee_garde_un_resume_court()
    {
        Assert.Equal("prêt", LaunchViewModel.DoneSummary(LaunchStep.Java, "Java 17 prêt : C:\\java", out var javaLabel));
        Assert.Null(javaLabel);

        Assert.Equal("prêt", LaunchViewModel.DoneSummary(LaunchStep.Forge, "Forge prêt : 1.20.1-forge-47.3.0", out var forgeLabel));
        Assert.Equal("Forge 47.3.0", forgeLabel);

        Assert.Equal("Dro0id", LaunchViewModel.DoneSummary(LaunchStep.Auth, "Connecté en tant que Dro0id.", out _));
        Assert.Equal("terminé", LaunchViewModel.DoneSummary(LaunchStep.ModSync, "Téléchargement du modpack...", out _));
    }
}
