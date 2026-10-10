using MinecraftLauncherPerso.Services.Changelog;
using MinecraftLauncherPerso.Services.Formatting;
using MinecraftLauncherPerso.Services.ModSync;
using MinecraftLauncherPerso.Services.News;
using MinecraftLauncherPerso.Services.Status;
using MinecraftLauncherPerso.ViewModels;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>View-models du hub : carte héros, tuiles, bannière de maintenance, profil, navigation.</summary>
public sealed class ViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(20, "à l'instant")]
    [InlineData(12 * 60, "il y a 12 min")]
    [InlineData(2 * 3600, "il y a 2 h")]
    [InlineData(3 * 86400, "il y a 3 j")]
    public void Le_temps_ecoule_est_court_et_en_francais(int seconds, string expected)
    {
        Assert.Equal(expected, RelativeTime.Format(Now.AddSeconds(-seconds), Now));
    }

    [Fact]
    public void La_carte_heros_en_ligne_montre_joueurs_et_compatibilite_sans_latence_en_double()
    {
        var hero = new HeroViewModel();

        hero.Apply(new ServerStatus(true, 3, 12, LatencyMs: 17, ProtocolVersion: 763, Motd: "Bienvenue"), "1.20.1", null, null, Now);

        Assert.Equal(StatusKind.Online, hero.StatusKind);
        Assert.Equal("EN LIGNE · 17 ms", hero.StatusText);
        Assert.True(hero.ShowPlayers);
        Assert.Equal("3", hero.PlayersOnline);
        Assert.Equal("/12 joueurs", hero.PlayersMax);
        Assert.False(hero.ShowFailure);
        Assert.False(hero.ShowContact);
        Assert.True(hero.ShowCompat);
        Assert.False(hero.CompatIsWarning);
        Assert.Equal("1.20.1 · compatible", hero.CompatText);
        Assert.Equal("« Bienvenue »", hero.Motd);
    }

    [Fact]
    public void La_carte_heros_hors_ligne_montre_le_dernier_contact_et_la_cause_et_garde_le_dernier_motd()
    {
        var hero = new HeroViewModel();

        hero.Apply(new ServerStatus(false, 0, 0, "Timeout après 8s en contactant x"), "1.20.1", Now.AddMinutes(-12), "Dernier message", Now);

        Assert.Equal(StatusKind.Offline, hero.StatusKind);
        Assert.False(hero.ShowPlayers);
        Assert.True(hero.ShowContact);
        Assert.Equal("dernier contact il y a 12 min", hero.ContactText);
        Assert.Equal("timeout 8 s", hero.FailureText);
        Assert.Equal("« Dernier message »", hero.Motd);
        Assert.True(hero.HasMotd);
    }

    [Fact]
    public void Une_autre_version_de_minecraft_passe_la_compatibilite_en_avertissement()
    {
        var hero = new HeroViewModel();

        hero.Apply(new ServerStatus(true, 1, 12, ProtocolVersion: 767, VersionName: "1.21"), "1.20.1", null, null, Now);

        Assert.True(hero.CompatIsWarning);
        Assert.Equal(StatusKind.Warning, hero.StatusKind);
    }

    [Fact]
    public void Sans_serveur_configure_la_pastille_est_neutre()
    {
        var hero = new HeroViewModel();

        hero.NotConfigured();

        Assert.Equal("Non configuré", hero.StatusText);
        Assert.False(hero.ShowPlayers);
    }

    [Fact]
    public void La_maintenance_decoupe_titre_fin_estimee_et_sous_titre()
    {
        var (title, subtitle, end) = MaintenanceViewModel.Parse("Mise à jour du modpack\r\nFIN: 23:00\nLe serveur redémarre.\nMerci.");

        Assert.Equal("Mise à jour du modpack", title);
        Assert.Equal("23:00", end);
        Assert.Equal("Le serveur redémarre.\nMerci.", subtitle);
    }

    [Fact]
    public void La_banniere_se_masque_sans_message_et_sans_fin_estimee_le_bloc_disparait()
    {
        var vm = new MaintenanceViewModel();

        vm.Apply("Maintenance ce soir");
        Assert.True(vm.IsVisible);
        Assert.False(vm.HasEndTime);
        Assert.False(vm.HasSubtitle);

        vm.Apply(null);
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public void La_tuile_modpack_resume_ajouts_retraits_et_mises_a_jour()
    {
        var tiles = new TilesViewModel();
        var changelog = new[]
        {
            new ModpackChangelogEntry { Date = Now.AddHours(-3), Lines = ["Ajouté : Create", "Ajouté : Jade", "Retiré : Journeymap", "Mis à jour : Waystones"] },
            new ModpackChangelogEntry { Date = Now.AddDays(-9), Lines = ["Ajouté : Ancien"] },
        };

        tiles.UpdateModpack(changelog, 42, "1.20.1", Now);

        Assert.Equal("42 mods · 1.20.1", tiles.ModpackTitle);
        Assert.Equal("MIS À JOUR IL Y A 3 H", tiles.ModpackAge);
        Assert.Equal(["+ 2 ajoutés", "− 1 retiré", "↻ 1 mis à jour"], tiles.ModpackLines.Select(line => line.Prefix));
        Assert.Equal([TileLineKind.Added, TileLineKind.Removed, TileLineKind.Updated], tiles.ModpackLines.Select(line => line.Kind));
        Assert.True(tiles.HasModpackLines);
    }

    [Fact]
    public void La_tuile_modpack_sans_changelog_reste_vide_et_sans_nombre_de_mods_garde_un_titre()
    {
        var tiles = new TilesViewModel();

        tiles.UpdateModpack([], null, "1.20.1", Now);

        Assert.Equal("Modpack · 1.20.1", tiles.ModpackTitle);
        Assert.False(tiles.HasModpackLines);
        Assert.Equal("", tiles.ModpackAge);
    }

    [Fact]
    public void La_tuile_actu_prend_la_plus_recente_et_decoupe_titre_et_texte()
    {
        var tiles = new TilesViewModel();

        tiles.UpdateNews(
        [
            new NewsHistoryEntry(Now.AddDays(-2), "Ancienne actu"),
            new NewsHistoryEntry(Now.AddHours(-1), "Event PvE samedi\nRDV au spawn à 21h."),
        ]);

        Assert.Equal("Event PvE samedi", tiles.NewsTitle);
        Assert.Equal("RDV au spawn à 21h.", tiles.NewsBody);
        Assert.True(tiles.HasNewsTitle);

        tiles.UpdateNews([]);
        Assert.Equal("Aucune actualité pour le moment.", tiles.NewsBody);
        Assert.False(tiles.HasNewsTitle);
    }

    [Fact]
    public void La_tuile_launcher_montre_les_notes_de_la_version_installee_et_la_pastille_derniere()
    {
        var tiles = new TilesViewModel();
        var releases = new[]
        {
            new ReleaseChangelogEntry("v2.0.1", "https://example/v2.0.1", Now, true, ["Titre mis à jour"]),
            new ReleaseChangelogEntry("v2.0.0", "https://example/v2.0.0", Now.AddDays(-1), false, ["Nouvelle interface"]),
        };

        tiles.UpdateLauncher(releases, "2.0.1");
        Assert.Equal("v2.0.1 · DERNIÈRE", tiles.LauncherPillText);
        Assert.Equal(StatusKind.Online, tiles.LauncherPillKind);
        Assert.Equal("— Titre mis à jour", tiles.LauncherBody);
        Assert.Equal("https://example/v2.0.1", tiles.LauncherUrl);

        tiles.UpdateLauncher(releases, "2.0.0");
        Assert.Equal("v2.0.0", tiles.LauncherPillText);
        Assert.Equal(StatusKind.Neutral, tiles.LauncherPillKind);
    }

    [Fact]
    public void Le_profil_affiche_version_et_synchro_et_la_connexion()
    {
        var profile = new ProfileViewModel();
        Assert.True(profile.IsDisconnected);

        profile.SetConnected("DroOid_");
        profile.UpdateSync(Now.AddHours(-2), "2.0.1", Now);

        Assert.True(profile.IsConnected);
        Assert.Equal("DroOid_", profile.PlayerName);
        Assert.Equal("v2.0.1 · SYNCHRO IL Y A 2 H", profile.VersionLine);

        profile.UpdateSync(null, "2.0.1", Now);
        Assert.Equal("v2.0.1 · JAMAIS SYNCHRONISÉ", profile.VersionLine);

        profile.SetAvatarError("réseau");
        Assert.True(profile.HasAvatarError);
    }

    [Fact]
    public void La_navigation_deduit_visibilites_item_actif_et_libelle_de_la_page_courante()
    {
        var main = new MainViewModel();
        var changed = new List<string?>();
        main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.True(main.IsHubPage);
        Assert.Equal("OMNÉRIA GAMES LAUNCHER · HUB", main.TitleLabel);

        main.CurrentPage = AppPage.Logs;

        Assert.False(main.IsHubPage);
        Assert.True(main.IsNotHubPage);
        Assert.True(main.IsLogsPage);
        Assert.True(main.IsSettingsNavActive);
        Assert.Equal("OMNÉRIA GAMES LAUNCHER · JOURNAUX", main.TitleLabel);
        Assert.Contains(nameof(MainViewModel.TitleLabel), changed);
    }

    [Fact]
    public void La_pastille_de_mise_a_jour_apparait_avec_la_version()
    {
        var main = new MainViewModel();
        Assert.False(main.UpdateAvailable);

        main.ShowUpdate("2.0.2");

        Assert.True(main.UpdateAvailable);
        Assert.Equal("MISE À JOUR 2.0.2", main.UpdateText);
    }
}
