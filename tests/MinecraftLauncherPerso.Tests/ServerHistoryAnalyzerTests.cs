using MinecraftLauncherPerso.Services.Status;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>Disponibilité sur 24 h et présence des joueurs, calculées depuis server-history.json.</summary>
public sealed class ServerHistoryAnalyzerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static ServerHistorySample Sample(int minutesAgo, bool online = true, params string[] names) =>
        new() { Time = Now.AddMinutes(-minutesAgo), Online = online, Players = names.Length, Names = [.. names] };

    private static ServerHistory History(params ServerHistorySample[] samples) => new() { Samples = [.. samples] };

    [Fact]
    public void Sans_echantillon_la_disponibilite_est_inconnue()
    {
        Assert.Null(ServerHistoryAnalyzer.UptimeRatio(History(), Now));
        Assert.All(ServerHistoryAnalyzer.Hourly(History(), Now), bucket => Assert.Null(bucket.UptimeRatio));
    }

    [Fact]
    public void La_disponibilite_est_la_part_des_echantillons_en_ligne()
    {
        var history = History(Sample(1), Sample(2), Sample(3, online: false), Sample(4));

        Assert.Equal(0.75, ServerHistoryAnalyzer.UptimeRatio(history, Now));
    }

    [Fact]
    public void Les_echantillons_de_plus_de_24_heures_sont_ignores()
    {
        var history = History(Sample(1), Sample(24 * 60 + 30, online: false));

        Assert.Equal(1.0, ServerHistoryAnalyzer.UptimeRatio(history, Now));
    }

    [Fact]
    public void Les_cases_horaires_vont_de_la_plus_ancienne_a_la_plus_recente()
    {
        var history = History(Sample(10), Sample(70, online: false));

        var buckets = ServerHistoryAnalyzer.Hourly(history, Now);

        Assert.Equal(24, buckets.Count);
        Assert.Equal(1.0, buckets[^1].UptimeRatio);
        Assert.Equal(0.0, buckets[^2].UptimeRatio);
        Assert.Null(buckets[0].UptimeRatio);
    }

    [Fact]
    public void Un_joueur_present_sans_interruption_est_en_ligne_depuis_son_premier_echantillon()
    {
        var history = History(Sample(10, true, "Alex"), Sample(9, true, "Alex"), Sample(8, true, "Alex"), Sample(1, true, "Alex"));

        var alex = Assert.Single(ServerHistoryAnalyzer.Presence(history, Now));

        Assert.True(alex.IsOnline);
        // Écart de 7 minutes entre deux échantillons : nouvelle session.
        Assert.Equal(Now.AddMinutes(-1), alex.SessionStart);
    }

    [Fact]
    public void Une_session_continue_garde_son_debut()
    {
        var history = History(Sample(3, true, "Alex"), Sample(2, true, "Alex"), Sample(1, true, "Alex"));

        var alex = Assert.Single(ServerHistoryAnalyzer.Presence(history, Now));

        Assert.Equal(Now.AddMinutes(-3), alex.SessionStart);
        Assert.Equal(Now.AddMinutes(-1), alex.LastSeen);
    }

    [Fact]
    public void Un_joueur_parti_nest_plus_en_ligne_et_les_presents_passent_en_premier()
    {
        var history = History(Sample(3, true, "Alex", "Sam"), Sample(2, true, "Alex", "Sam"), Sample(1, true, "Sam"));

        var presence = ServerHistoryAnalyzer.Presence(history, Now);

        Assert.Equal(["Sam", "Alex"], presence.Select(p => p.Name));
        Assert.True(presence[0].IsOnline);
        Assert.False(presence[1].IsOnline);
        Assert.Equal(Now.AddMinutes(-2), presence[1].LastSeen);
    }

    [Fact]
    public void Un_historique_perime_ne_montre_personne_en_ligne()
    {
        var history = History(Sample(30, true, "Alex"));

        Assert.False(Assert.Single(ServerHistoryAnalyzer.Presence(history, Now)).IsOnline);
    }

    [Fact]
    public void Un_serveur_hors_ligne_au_dernier_echantillon_ne_montre_personne_en_ligne()
    {
        var history = History(Sample(2, true, "Alex"), Sample(1, online: false));

        Assert.False(Assert.Single(ServerHistoryAnalyzer.Presence(history, Now)).IsOnline);
    }

    [Theory]
    [InlineData(20, "moins d'1 min")]
    [InlineData(12 * 60, "12 min")]
    [InlineData(65 * 60, "1 h 05")]
    [InlineData(3 * 3600, "3 h")]
    [InlineData(50 * 3600, "2 j")]
    public void Les_durees_sont_courtes_et_lisibles(int seconds, string expected)
    {
        Assert.Equal(expected, ServerHistoryAnalyzer.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }
}
