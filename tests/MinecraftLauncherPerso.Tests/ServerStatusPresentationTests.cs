using MinecraftLauncherPerso.Services.Status;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>Libellé, gravité et infobulle de la pastille d'état du serveur.</summary>
public sealed class ServerStatusPresentationTests
{
    [Fact]
    public void Un_serveur_en_ligne_affiche_sa_latence()
    {
        var status = new ServerStatus(true, 2, 12, LatencyMs: 42, ProtocolVersion: 763);

        var result = ServerStatusPresentation.From(status, "1.20.1");

        Assert.Equal(StatusKind.Online, result.Kind);
        Assert.Equal("EN LIGNE · 42 ms", result.Text);
        Assert.Null(result.Tooltip);
    }

    [Fact]
    public void Sans_latence_le_libelle_reste_simple()
    {
        Assert.Equal("EN LIGNE", ServerStatusPresentation.From(new ServerStatus(true, 0, 12), "1.20.1").Text);
    }

    [Fact]
    public void Un_serveur_hors_ligne_donne_la_cause_en_infobulle()
    {
        var result = ServerStatusPresentation.From(new ServerStatus(false, 0, 0, "Timeout après 8s"), "1.20.1");

        Assert.Equal(StatusKind.Offline, result.Kind);
        Assert.Equal("HORS LIGNE", result.Text);
        Assert.Equal("Timeout après 8s", result.Tooltip);
    }

    [Fact]
    public void Une_autre_version_de_minecraft_est_signalee()
    {
        var status = new ServerStatus(true, 1, 12, ProtocolVersion: 767, VersionName: "1.21");

        var result = ServerStatusPresentation.From(status, "1.20.1");

        Assert.Equal(StatusKind.Warning, result.Kind);
        Assert.Equal("EN LIGNE · VERSION DIFFÉRENTE", result.Text);
        Assert.Contains("1.21", result.Tooltip);
    }

    [Fact]
    public void Une_version_de_launcher_inconnue_ne_declenche_pas_dalerte()
    {
        var status = new ServerStatus(true, 1, 12, ProtocolVersion: 767);

        Assert.Equal(StatusKind.Online, ServerStatusPresentation.From(status, "1.21").Kind);
    }
}
