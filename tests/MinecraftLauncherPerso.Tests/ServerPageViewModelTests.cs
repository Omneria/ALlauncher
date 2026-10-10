using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Status;
using MinecraftLauncherPerso.ViewModels;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>Page SERVEUR : état du moment, courbe 24 h, joueurs, cases de notification.</summary>
public sealed class ServerPageViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static LauncherSettings Settings() => new() { ServerName = "Astral Nexus", ServerHost = "astralnexusmc.duckdns.org" };

    private static ServerHistorySample Sample(int minutesAgo, bool online, params string[] names) =>
        new() { Time = Now.AddMinutes(-minutesAgo), Online = online, Players = names.Length, Names = [.. names] };

    [Fact]
    public void Sans_ping_la_page_montre_un_etat_inconnu_et_pas_de_courbe()
    {
        var vm = new ServerPageViewModel();

        vm.Update(Settings(), null, null, Now);

        Assert.Equal("INCONNU", vm.StatusText);
        Assert.Equal(StatusKind.Neutral, vm.StatusKind);
        Assert.Equal("ASTRAL NEXUS · astralnexusmc.duckdns.org", vm.AddressText);
        Assert.True(vm.HistoryUnavailable);
        Assert.Empty(vm.Bars);
        Assert.True(vm.NoPlayers);
    }

    [Fact]
    public void Un_serveur_en_ligne_montre_latence_version_et_protocole_valide()
    {
        var vm = new ServerPageViewModel();

        vm.Update(Settings(), new ServerStatus(true, 3, 12, LatencyMs: 24, ProtocolVersion: 763, VersionName: "Forge 1.20.1", Motd: "Saison 3"), null, Now);

        Assert.Equal("EN LIGNE", vm.StatusText);
        Assert.Equal("3", vm.PlayersOnline);
        Assert.Equal("/12", vm.PlayersMax);
        Assert.Equal("24 ms", vm.Latency);
        Assert.Equal("Forge 1.20.1", vm.Version);
        Assert.Equal("763 ✓", vm.Protocol);
        Assert.True(vm.ProtocolOk);
        Assert.Equal("Saison 3", vm.Motd);
    }

    [Fact]
    public void Un_serveur_hors_ligne_efface_les_valeurs()
    {
        var vm = new ServerPageViewModel();

        vm.Update(Settings(), new ServerStatus(false, 0, 0, "Timeout"), null, Now);

        Assert.Equal("HORS LIGNE", vm.StatusText);
        Assert.Equal("—", vm.Latency);
        Assert.Equal("", vm.PlayersOnline);
        Assert.False(vm.ProtocolOk);
    }

    [Fact]
    public void La_courbe_a_48_barres_et_signale_la_derniere_coupure()
    {
        var vm = new ServerPageViewModel();
        var history = new ServerHistory { Samples = [Sample(100, true), Sample(61, false), Sample(60, false), Sample(59, true), Sample(1, true)] };

        vm.Update(Settings(), null, history, Now);

        Assert.False(vm.HistoryUnavailable);
        Assert.Equal(48, vm.Bars.Count);
        Assert.Contains(vm.Bars, bar => bar.Kind == BarKind.Down && bar.BarHeight == ServerPageViewModel.MinBarHeight);
        Assert.Contains(vm.Bars, bar => bar.Kind == BarKind.Up && bar.BarHeight == ServerPageViewModel.MaxBarHeight);
        Assert.StartsWith("coupure ", vm.OutageText);
    }

    [Fact]
    public void Les_joueurs_connectes_viennent_de_lhistorique_sinon_de_lechantillon_du_ping()
    {
        var vm = new ServerPageViewModel();
        var history = new ServerHistory { Samples = [Sample(3, true, "Dro0id", "Nyx_"), Sample(2, true, "Dro0id", "Nyx_"), Sample(1, true, "Dro0id")] };

        vm.Update(Settings(), new ServerStatus(true, 1, 12), history, Now);
        Assert.Equal(["Dro0id"], vm.Players.Select(p => p.Name));
        Assert.Equal("3 min", vm.Players[0].Duration);
        Assert.Equal("1", vm.PlayersListCount);

        vm.Update(Settings(), new ServerStatus(true, 2, 12, PlayerNames: ["Alex", "Sam"]), null, Now);
        Assert.Equal(["Alex", "Sam"], vm.Players.Select(p => p.Name));
        Assert.False(vm.NoPlayers);
    }

    [Fact]
    public void Les_cases_de_notification_refletent_les_reglages_sans_declencher_levenement()
    {
        var vm = new ServerPageViewModel();
        var changes = new List<NotificationChange>();
        vm.NotificationChanged += (_, change) => changes.Add(change);
        var settings = Settings();
        settings.DesktopNotificationsEnabled = true;
        settings.NotifyFriendConnected = false;

        vm.Update(settings, null, null, Now);

        Assert.True(vm.NotifyServerBack);
        Assert.False(vm.NotifyFriend);
        Assert.Empty(changes);

        vm.NotifyFriend = true;
        Assert.Equal([new NotificationChange(NotificationKind.Friend, true)], changes);
    }
}
