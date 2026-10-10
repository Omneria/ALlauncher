using MinecraftLauncherPerso.Services.Status;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>Lecture de la réponse status du serveur (v1.12.0) : version, message du jour, latence.</summary>
public sealed class ServerStatusParsingTests
{
    [Fact]
    public void ParseStatus_lit_joueurs_version_et_latence()
    {
        var status = ServerStatusService.ParseStatus(
            """{"version":{"name":"1.20.1","protocol":763},"players":{"online":3,"max":20},"description":"Astral Nexus"}""",
            latencyMs: 42);

        Assert.True(status.IsOnline);
        Assert.Equal(3, status.OnlinePlayers);
        Assert.Equal(20, status.MaxPlayers);
        Assert.Equal(763, status.ProtocolVersion);
        Assert.Equal("1.20.1", status.VersionName);
        Assert.Equal(42, status.LatencyMs);
        Assert.Equal("Astral Nexus", status.Motd);
    }

    [Fact]
    public void ParseStatus_lit_les_pseudos_de_lechantillon_de_joueurs()
    {
        var status = ServerStatusService.ParseStatus(
            """{"version":{"name":"1.20.1","protocol":763},"players":{"online":2,"max":12,"sample":[{"name":"Alex","id":"a"},{"name":"Sam","id":"b"},{"id":"c"}]}}""");

        Assert.Equal(["Alex", "Sam"], status.PlayerNames);
    }

    [Fact]
    public void ParseStatus_sans_echantillon_ne_donne_pas_de_pseudos()
    {
        var status = ServerStatusService.ParseStatus("""{"players":{"online":2,"max":12}}""");

        Assert.Null(status.PlayerNames);
    }

    [Fact]
    public void ParseStatus_aplatit_un_motd_en_composant_json_et_retire_les_codes_de_couleur()
    {
        var status = ServerStatusService.ParseStatus(
            """{"players":{"online":0,"max":20},"description":{"text":"§bAstral ","extra":[{"text":"§lNexus"},"  saison 6"]}}""");

        Assert.Equal("Astral Nexus  saison 6", status.Motd);
        Assert.Null(status.ProtocolVersion);
    }

    [Fact]
    public void ParseStatus_sans_motd_renvoie_null()
    {
        var status = ServerStatusService.ParseStatus("""{"players":{"online":0,"max":20},"description":""}""");

        Assert.Null(status.Motd);
    }

    [Theory]
    [InlineData("1.20.1", 763)]
    [InlineData("1.20", 763)]
    [InlineData("1.16.5", null)]
    public void ExpectedProtocol_connait_la_version_du_pack(string minecraftVersion, int? expected)
    {
        Assert.Equal(expected, ServerStatusService.ExpectedProtocol(minecraftVersion));
    }
}
