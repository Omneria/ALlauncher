using MinecraftLauncherPerso.Services.Status;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>Notification « un ami se connecte » (page SERVEUR).</summary>
public sealed class FriendWatcherTests
{
    [Fact]
    public void Un_ami_qui_arrive_entre_deux_sondes_est_signale()
    {
        var result = FriendWatcher.NewlyConnected(["Dro0id"], ["Dro0id", "Nyx_", "Inconnu"], ["nyx_", "Kaelith"]);

        Assert.Equal(["Nyx_"], result);
    }

    [Fact]
    public void Au_premier_passage_ou_sans_echantillon_personne_nest_signale()
    {
        Assert.Empty(FriendWatcher.NewlyConnected(null, ["Nyx_"], ["Nyx_"]));
        Assert.Empty(FriendWatcher.NewlyConnected(["Nyx_"], null, ["Nyx_"]));
    }

    [Fact]
    public void Un_ami_deja_present_nest_pas_signale_de_nouveau()
    {
        Assert.Empty(FriendWatcher.NewlyConnected(["Nyx_"], ["Nyx_"], ["Nyx_"]));
    }

    [Fact]
    public void La_saisie_des_amis_est_nettoyee()
    {
        Assert.Equal(["Alex", "sam"], FriendWatcher.ParseNames(" Alex, sam ,, ALEX;"));
        Assert.Empty(FriendWatcher.ParseNames(null));
    }
}
