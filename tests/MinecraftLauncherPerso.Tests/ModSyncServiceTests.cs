using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MinecraftLauncherPerso.Services.ModSync;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// Mode manifest de ModSyncService (le mode par défaut depuis le modpack 1.20.1) : jusqu'ici
/// entièrement non testé alors que c'est lui qui écrit dans le dossier de jeu de chaque joueur.
/// Couvre les trois garde-fous ajoutés en v1.11.0 — chemin du manifest confiné au dossier de jeu,
/// empreinte SHA-256 vérifiée avant d'écrire, mods retirés du pack supprimés localement — via un
/// HttpClient factice (aucun accès réseau), sur un dossier temporaire jetable.
/// </summary>
public sealed class ModSyncServiceTests : IDisposable
{
    private const string ManifestUrl = "http://stub/manifest.json";

    private readonly string _gameDirectory = Path.Combine(Path.GetTempPath(), $"al-launcher-modsync-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_gameDirectory))
        {
            Directory.Delete(_gameDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("mods/create.jar")]
    [InlineData("config/create.toml")]
    [InlineData("mods/sous/dossier/x.jar")]
    [InlineData("mods\\backslash.jar")] // séparateur Windows dans la clé : toléré, reste sous le dossier de jeu
    public void ResolveSafeLocalPath_accepte_un_chemin_relatif_sous_le_dossier_de_jeu(string relativePath)
    {
        var resolved = ModSyncService.ResolveSafeLocalPath(_gameDirectory, relativePath);

        Assert.NotNull(resolved);
        Assert.StartsWith(Path.GetFullPath(_gameDirectory) + Path.DirectorySeparatorChar, resolved!);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("mods/../../evil.txt")]
    [InlineData("mods/..")] // remonte exactement à la racine : pas un fichier "sous" le dossier de jeu non plus
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveSafeLocalPath_rejette_un_chemin_qui_sort_du_dossier_de_jeu(string relativePath)
    {
        Assert.Null(ModSyncService.ResolveSafeLocalPath(_gameDirectory, relativePath));
    }

    [Fact]
    public void ResolveSafeLocalPath_rejette_un_chemin_absolu()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "evil.txt");

        Assert.Null(ModSyncService.ResolveSafeLocalPath(_gameDirectory, absolute));
    }

    [Fact]
    public async Task SyncAsync_telecharge_les_fichiers_manquants_et_supprime_les_mods_retires_du_pack()
    {
        var newJar = Encoding.UTF8.GetBytes("nouveau mod");
        Directory.CreateDirectory(Path.Combine(_gameDirectory, "mods"));
        Directory.CreateDirectory(Path.Combine(_gameDirectory, "config"));
        File.WriteAllText(Path.Combine(_gameDirectory, "mods", "ancien.jar"), "retiré du pack côté VPS");
        File.WriteAllText(Path.Combine(_gameDirectory, "config", "perso.toml"), "réglage local du joueur");

        var service = CreateService(new Dictionary<string, byte[]>
        {
            [ManifestUrl] = ManifestJson(("mods/nouveau.jar", "http://stub/files/mods/nouveau.jar", Sha256Hex(newJar))),
            ["http://stub/files/mods/nouveau.jar"] = newJar,
        });

        await service.SyncAsync("", ManifestUrl, _gameDirectory);

        Assert.Equal(newJar, File.ReadAllBytes(Path.Combine(_gameDirectory, "mods", "nouveau.jar")));
        Assert.False(File.Exists(Path.Combine(_gameDirectory, "mods", "ancien.jar"))); // mod absent du manifest : supprimé
        Assert.True(File.Exists(Path.Combine(_gameDirectory, "config", "perso.toml"))); // config/ jamais élagué
        Assert.False(File.Exists(Path.Combine(_gameDirectory, "mods", "nouveau.jar.part"))); // écriture atomique terminée
        Assert.NotNull(service.GetLastSyncedAt(_gameDirectory));
    }

    [Fact]
    public async Task SyncAsync_ne_supprime_rien_dans_mods_si_le_manifest_ne_couvre_pas_mods()
    {
        // Manifest partiel (ex. erreur de génération côté VPS) : ne doit pas faire disparaître tout
        // le pack d'un coup.
        var config = Encoding.UTF8.GetBytes("valeur=1");
        Directory.CreateDirectory(Path.Combine(_gameDirectory, "mods"));
        File.WriteAllText(Path.Combine(_gameDirectory, "mods", "existant.jar"), "mod en place");

        var service = CreateService(new Dictionary<string, byte[]>
        {
            [ManifestUrl] = ManifestJson(("config/x.toml", "http://stub/files/config/x.toml", Sha256Hex(config))),
            ["http://stub/files/config/x.toml"] = config,
        });

        await service.SyncAsync("", ManifestUrl, _gameDirectory);

        Assert.True(File.Exists(Path.Combine(_gameDirectory, "mods", "existant.jar")));
        Assert.True(File.Exists(Path.Combine(_gameDirectory, "config", "x.toml")));
    }

    [Fact]
    public async Task SyncAsync_refuse_un_fichier_dont_lempreinte_ne_correspond_pas_au_manifest()
    {
        var served = Encoding.UTF8.GetBytes("contenu corrompu ou tronqué");
        var service = CreateService(new Dictionary<string, byte[]>
        {
            [ManifestUrl] = ManifestJson(("mods/mod.jar", "http://stub/files/mods/mod.jar", new string('0', 64))),
            ["http://stub/files/mods/mod.jar"] = served,
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SyncAsync("", ManifestUrl, _gameDirectory));

        Assert.Contains("mods/mod.jar", ex.Message);
        Assert.False(File.Exists(Path.Combine(_gameDirectory, "mods", "mod.jar"))); // rien d'écrit sous le nom définitif
        Assert.False(File.Exists(Path.Combine(_gameDirectory, "mods", "mod.jar.part")));
    }

    [Fact]
    public async Task SyncAsync_refuse_un_manifest_dont_un_chemin_sort_du_dossier_de_jeu()
    {
        var payload = Encoding.UTF8.GetBytes("ne doit jamais être écrit");
        var escapedName = $"al-launcher-evil-{Guid.NewGuid():N}.txt";
        var service = CreateService(new Dictionary<string, byte[]>
        {
            [ManifestUrl] = ManifestJson(($"../{escapedName}", "http://stub/files/evil.txt", Sha256Hex(payload))),
            ["http://stub/files/evil.txt"] = payload,
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SyncAsync("", ManifestUrl, _gameDirectory));

        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), escapedName)));
    }

    [Fact]
    public async Task SyncAsync_ne_retelecharge_pas_un_fichier_deja_a_jour()
    {
        var jar = Encoding.UTF8.GetBytes("déjà en place");
        Directory.CreateDirectory(Path.Combine(_gameDirectory, "mods"));
        File.WriteAllBytes(Path.Combine(_gameDirectory, "mods", "mod.jar"), jar);

        // Le fichier n'est volontairement PAS servi par le stub : s'il était retéléchargé, le 404
        // ferait échouer la synchro.
        var service = CreateService(new Dictionary<string, byte[]>
        {
            [ManifestUrl] = ManifestJson(("mods/mod.jar", "http://stub/files/mods/mod.jar", Sha256Hex(jar))),
        });

        await service.SyncAsync("", ManifestUrl, _gameDirectory);

        Assert.Equal(jar, File.ReadAllBytes(Path.Combine(_gameDirectory, "mods", "mod.jar")));
    }

    [Fact]
    public async Task SyncAsync_ne_reecrit_pas_une_config_par_defaut_deja_modifiee_par_le_joueur()
    {
        var serverDefault = Encoding.UTF8.GetBytes("minimap=off");
        Directory.CreateDirectory(Path.Combine(_gameDirectory, "config"));
        var localPath = Path.Combine(_gameDirectory, "config", "journeymap.toml");
        File.WriteAllText(localPath, "minimap=on");

        var service = CreateService(new Dictionary<string, byte[]>
        {
            [ManifestUrl] = ManifestJson(("config/journeymap.toml", "http://stub/files/config/journeymap.toml", Sha256Hex(serverDefault), "default")),
            ["http://stub/files/config/journeymap.toml"] = serverDefault,
        });

        await service.SyncAsync("", ManifestUrl, _gameDirectory);

        Assert.Equal("minimap=on", File.ReadAllText(localPath));
    }

    [Fact]
    public async Task SyncAsync_installe_une_config_par_defaut_absente()
    {
        var serverDefault = Encoding.UTF8.GetBytes("minimap=off");
        var service = CreateService(new Dictionary<string, byte[]>
        {
            [ManifestUrl] = ManifestJson(("config/journeymap.toml", "http://stub/files/config/journeymap.toml", Sha256Hex(serverDefault), "default")),
            ["http://stub/files/config/journeymap.toml"] = serverDefault,
        });

        await service.SyncAsync("", ManifestUrl, _gameDirectory);

        Assert.Equal(serverDefault, File.ReadAllBytes(Path.Combine(_gameDirectory, "config", "journeymap.toml")));
    }

    [Fact]
    public async Task SyncAsync_reimpose_une_config_imposee_modifiee_localement()
    {
        var enforced = Encoding.UTF8.GetBytes("background=astral");
        Directory.CreateDirectory(Path.Combine(_gameDirectory, "config"));
        var localPath = Path.Combine(_gameDirectory, "config", "early.json");
        File.WriteAllText(localPath, "background=forge");

        var service = CreateService(new Dictionary<string, byte[]>
        {
            [ManifestUrl] = ManifestJson(("config/early.json", "http://stub/files/config/early.json", Sha256Hex(enforced), null)),
            ["http://stub/files/config/early.json"] = enforced,
        });

        await service.SyncAsync("", ManifestUrl, _gameDirectory);

        Assert.Equal(enforced, File.ReadAllBytes(localPath));
    }

    [Fact]
    public async Task FetchPackSettingsAsync_lit_la_section_pack_du_manifest()
    {
        var service = CreateService(new Dictionary<string, byte[]>
        {
            [ManifestUrl] = Encoding.UTF8.GetBytes("""{"files":{},"pack":{"forgeVersion":"47.4.23","recommendedRamMb":6144,"minRamMb":4096}}"""),
        });

        var pack = await service.FetchPackSettingsAsync(ManifestUrl);

        Assert.NotNull(pack);
        Assert.Equal("47.4.23", pack!.ForgeVersion);
        Assert.Equal(6144, pack.RecommendedRamMb);
        Assert.Equal(4096, pack.MinRamMb);
    }

    [Fact]
    public async Task FetchPackSettingsAsync_renvoie_null_si_le_vps_ne_repond_pas()
    {
        var service = CreateService([]);

        Assert.Null(await service.FetchPackSettingsAsync(ManifestUrl));
        Assert.Null(await service.FetchPackSettingsAsync(null));
    }

    [Fact]
    public async Task FetchChangelogAsync_lit_changelog_json_a_cote_du_manifest()
    {
        var service = CreateService(new Dictionary<string, byte[]>
        {
            ["http://stub/changelog.json"] = Encoding.UTF8.GetBytes(
                """[{"date":"2026-09-27T15:00:00+00:00","lines":["Ajouté : Quark-4.0-462"]},{"date":"2026-09-20T10:00:00+00:00","lines":[]}]"""),
        });

        var entries = await service.FetchChangelogAsync(ManifestUrl);

        var entry = Assert.Single(entries); // entrée sans ligne ignorée
        Assert.Equal(["Ajouté : Quark-4.0-462"], entry.Lines);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 15, 0, 0, TimeSpan.Zero), entry.Date);
    }

    [Fact]
    public async Task FetchChangelogAsync_renvoie_une_liste_vide_si_absent_ou_illisible()
    {
        var service = CreateService(new Dictionary<string, byte[]>
        {
            ["http://stub/changelog.json"] = Encoding.UTF8.GetBytes("pas du json"),
        });

        Assert.Empty(await service.FetchChangelogAsync(ManifestUrl));
        Assert.Empty(await CreateService([]).FetchChangelogAsync(ManifestUrl));
    }

    private static ModSyncService CreateService(Dictionary<string, byte[]> responses) =>
        new(new HttpClient(new StubHandler(responses)));

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] ManifestJson(params (string RelativePath, string Url, string Sha256)[] files) =>
        ManifestJson(files.Select(f => (f.RelativePath, f.Url, f.Sha256, (string?)null)).ToArray());

    private static byte[] ManifestJson(params (string RelativePath, string Url, string Sha256, string? Mode)[] files)
    {
        var manifest = new ModpackManifest();
        foreach (var (relativePath, url, sha256, mode) in files)
        {
            manifest.Files[relativePath] = new ModpackManifestFile { Url = url, Sha256 = sha256, Mode = mode };
        }

        return JsonSerializer.SerializeToUtf8Bytes(manifest);
    }

    /// <summary>Répond aux URL connues avec les octets fournis, 404 sinon (changelog.txt, etc.).</summary>
    private sealed class StubHandler(Dictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            var response = responses.TryGetValue(url, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);

            return Task.FromResult(response);
        }
    }
}
