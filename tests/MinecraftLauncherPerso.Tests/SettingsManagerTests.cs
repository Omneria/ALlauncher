using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Configuration;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// SettingsManager.Load() plantait auparavant tout le launcher au démarrage si settings.json était
/// corrompu (JSON tronqué par une coupure en pleine écriture) : aucun test ne couvrait ni ce cas,
/// ni le filet de sécurité contre des valeurs absurdes (RAM négative, min &gt; max...) qu'un
/// settings.json édité à la main pouvait contenir sans qu'aucune exception ne soit levée.
/// </summary>
public sealed class SettingsManagerTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"al-launcher-tests-{Guid.NewGuid():N}", "settings.json");

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_sans_fichier_existant_retourne_les_valeurs_par_defaut()
    {
        var manager = new SettingsManager(_settingsPath);

        var settings = manager.Load();

        Assert.Equal(new LauncherSettings().MinRamMb, settings.MinRamMb);
        Assert.False(File.Exists(_settingsPath)); // ne crée pas le fichier tant que rien n'a jamais été sauvegardé
    }

    [Fact]
    public void SettingsFileExists_distingue_premier_lancement_et_lancements_suivants()
    {
        // Utilisé par MainWindow (WelcomeWindow, v1.8.0) pour décider d'afficher l'assistant de
        // premier lancement : doit rester false tant que rien n'a jamais été sauvegardé, y compris
        // après un simple Load() (qui ne crée le fichier que s'il a dû corriger quelque chose).
        var manager = new SettingsManager(_settingsPath);
        Assert.False(manager.SettingsFileExists());

        manager.Save(new LauncherSettings());

        Assert.True(manager.SettingsFileExists());
    }

    [Fact]
    public void Save_puis_Load_redonne_les_memes_valeurs()
    {
        var manager = new SettingsManager(_settingsPath);
        var original = new LauncherSettings { MinRamMb = 2048, MaxRamMb = 4096, ServerPort = 25570 };

        manager.Save(original);
        var reloaded = manager.Load();

        Assert.Equal(2048, reloaded.MinRamMb);
        Assert.Equal(4096, reloaded.MaxRamMb);
        Assert.Equal(25570, reloaded.ServerPort);
    }

    [Fact]
    public void Load_recupere_dun_settingsjson_corrompu_au_lieu_de_planter()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, "{ \"MinRamMb\": 2048, \"MaxRamMb\": "); // JSON tronqué
        var manager = new SettingsManager(_settingsPath);

        var settings = manager.Load(); // ne doit lever aucune exception

        Assert.Equal(new LauncherSettings().MinRamMb, settings.MinRamMb);
        // Le fichier fautif est conservé à côté (renommé) plutôt qu'écrasé silencieusement, pour
        // pouvoir diagnostiquer si ça se reproduit.
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Assert.Contains(Directory.GetFiles(directory), f => f.Contains(".corrupt-"));
    }

    [Theory]
    [InlineData(-1, 4096)] // RAM min négative
    [InlineData(0, 4096)] // RAM min nulle
    [InlineData(8192, 4096)] // min > max : doit être remis dans l'ordre plutôt que planter au lancement du jeu
    public void Load_corrige_une_configuration_RAM_invalide(int minRamMb, int maxRamMb)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, $$"""{ "MinRamMb": {{minRamMb}}, "MaxRamMb": {{maxRamMb}} }""");
        var manager = new SettingsManager(_settingsPath);

        var settings = manager.Load();

        Assert.True(settings.MinRamMb > 0);
        Assert.True(settings.MaxRamMb > 0);
        Assert.True(settings.MinRamMb <= settings.MaxRamMb);
    }

    [Fact]
    public void Load_corrige_un_port_serveur_hors_plage()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, """{ "ServerPort": 999999 }""");
        var manager = new SettingsManager(_settingsPath);

        var settings = manager.Load();

        Assert.InRange(settings.ServerPort, 1, 65535);
    }

    [Fact]
    public void Load_applique_la_migration_de_lancien_ServerHost_fautif()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, """{ "ServerHost": "astranexusmc.duckdns.org" }""");
        var manager = new SettingsManager(_settingsPath);

        var settings = manager.Load();

        Assert.Equal("astralnexusmc.duckdns.org", settings.ServerHost);
    }

    [Fact]
    public void Load_migre_1_16_5_vers_1_20_1_si_valeurs_par_defaut_intactes()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, """{ "MinecraftVersion": "1.16.5", "ForgeVersion": "36.2.34" }""");
        var manager = new SettingsManager(_settingsPath);

        var settings = manager.Load();

        Assert.Equal(new LauncherSettings().MinecraftVersion, settings.MinecraftVersion);
        Assert.Equal(new LauncherSettings().ForgeVersion, settings.ForgeVersion);
    }

    [Fact]
    public void Load_ne_migre_pas_une_version_personnalisee_par_le_joueur()
    {
        // Seul MinecraftVersion a été changé à la main (ForgeVersion reste l'ancien défaut) : pas
        // une combinaison "jamais touchée", donc pas de migration automatique qui écraserait un
        // choix délibéré.
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, """{ "MinecraftVersion": "1.19.2", "ForgeVersion": "36.2.34" }""");
        var manager = new SettingsManager(_settingsPath);

        var settings = manager.Load();

        Assert.Equal("1.19.2", settings.MinecraftVersion);
    }

    [Fact]
    public void Load_active_le_manifest_par_defaut_si_le_champ_est_encore_vide()
    {
        // "" était la seule valeur possible avant l'introduction du manifest 1.20.1 : un
        // settings.json qui l'a encore doit basculer vers la nouvelle URL par défaut.
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, """{ "ModpackManifestUrl": "" }""");
        var manager = new SettingsManager(_settingsPath);

        var settings = manager.Load();

        Assert.Equal(new LauncherSettings().ModpackManifestUrl, settings.ModpackManifestUrl);
        Assert.NotEmpty(settings.ModpackManifestUrl);
    }

    [Fact]
    public void Load_ne_touche_pas_un_ModpackManifestUrl_deja_personnalise()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, """{ "ModpackManifestUrl": "https://autre-vps/manifest.json" }""");
        var manager = new SettingsManager(_settingsPath);

        var settings = manager.Load();

        Assert.Equal("https://autre-vps/manifest.json", settings.ModpackManifestUrl);
    }

    [Fact]
    public void Load_active_la_banniere_de_maintenance_par_defaut_si_le_champ_est_encore_vide()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, """{ "MaintenanceMessageUrl": "" }""");
        var manager = new SettingsManager(_settingsPath);

        var settings = manager.Load();

        Assert.Equal(new LauncherSettings().MaintenanceMessageUrl, settings.MaintenanceMessageUrl);
        Assert.NotEmpty(settings.MaintenanceMessageUrl);
    }

    [Fact]
    public void Load_bascule_les_anciennes_URL_http_du_VPS_vers_les_nouvelles_https()
    {
        // Un settings.json écrit jusqu'en v1.10.0 porte les anciennes URL par défaut (http:// +
        // IP brute) : elles doivent basculer sur les nouvelles (https:// + domaine), sans quoi le
        // passage en HTTPS ne s'appliquerait jamais aux joueurs existants.
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var legacy = LauncherSettings.LegacyVpsUrlDefaults.Keys.ToList();
        Assert.Equal(3, legacy.Count);
        var legacyZip = legacy.Single(u => u.EndsWith(".zip"));
        var legacyManifest = legacy.Single(u => u.EndsWith("manifest.json"));
        var legacyMaintenance = legacy.Single(u => u.EndsWith("maintenance.txt"));
        File.WriteAllText(_settingsPath, $$"""{ "ModpackZipUrl": "{{legacyZip}}", "ModpackManifestUrl": "{{legacyManifest}}", "MaintenanceMessageUrl": "{{legacyMaintenance}}" }""");
        var manager = new SettingsManager(_settingsPath);

        var settings = manager.Load();

        var defaults = new LauncherSettings();
        Assert.Equal(defaults.ModpackZipUrl, settings.ModpackZipUrl);
        Assert.Equal(defaults.ModpackManifestUrl, settings.ModpackManifestUrl);
        Assert.Equal(defaults.MaintenanceMessageUrl, settings.MaintenanceMessageUrl);
        Assert.StartsWith("https://", settings.ModpackManifestUrl);
    }

    [Fact]
    public void Load_migre_lancien_port_25565_vers_25566_pour_le_serveur_par_defaut()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, """{ "ServerHost": "astralnexusmc.duckdns.org", "ServerPort": 25565 }""");
        var manager = new SettingsManager(_settingsPath);

        var settings = manager.Load();

        Assert.Equal(25566, settings.ServerPort);
    }

    [Fact]
    public void Load_ne_change_pas_le_port_dun_autre_serveur()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, """{ "ServerHost": "autre-serveur.example", "ServerPort": 25565 }""");
        var manager = new SettingsManager(_settingsPath);

        var settings = manager.Load();

        Assert.Equal(25565, settings.ServerPort);
    }

    [Fact]
    public void Load_ne_touche_pas_un_MaintenanceMessageUrl_deja_personnalise()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, """{ "MaintenanceMessageUrl": "https://autre-vps/maintenance.txt" }""");
        var manager = new SettingsManager(_settingsPath);

        var settings = manager.Load();

        Assert.Equal("https://autre-vps/maintenance.txt", settings.MaintenanceMessageUrl);
    }
}
