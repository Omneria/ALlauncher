using System.Net;
using System.Net.Http;
using System.Text;
using MinecraftLauncherPerso.Services.Changelog;
using MinecraftLauncherPerso.Services.GitHub;
using MinecraftLauncherPerso.Services.Update;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// Client partagé des releases GitHub (v1.12.0) : un seul appel pour la mise à jour et le
/// changelog, sans épuiser le quota de 60 requêtes/heure de l'API non authentifiée.
/// </summary>
public sealed class GitHubReleasesClientTests
{
    private const string ReleasesJson = """
        [
          {"tag_name":"v1.13.0","draft":true,"prerelease":false,"html_url":"https://x/1.13.0","published_at":"2026-10-02T10:00:00Z","body":"- Brouillon","assets":[]},
          {"tag_name":"v1.12.0","draft":false,"prerelease":false,"html_url":"https://x/1.12.0","published_at":"2026-10-01T10:00:00Z","body":"- Passage à .NET 10","assets":[]},
          {"tag_name":"v1.11.2","draft":false,"prerelease":false,"html_url":"https://x/1.11.2","published_at":"2026-09-26T10:00:00Z","body":"- Forge 47.4.23","assets":[]}
        ]
        """;

    [Fact]
    public async Task Deux_appels_rapproches_ne_font_quune_requete()
    {
        var handler = new CountingHandler(_ => Ok(ReleasesJson));
        var now = DateTimeOffset.UtcNow;
        var client = new GitHubReleasesClient(new HttpClient(handler), () => now);

        await client.GetReleasesAsync();
        await client.GetReleasesAsync();

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Apres_expiration_renvoie_une_requete_conditionnelle_et_garde_la_reponse_sur_304()
    {
        var handler = new CountingHandler(request => request.Headers.IfNoneMatch.Count > 0
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : Ok(ReleasesJson, etag: "\"abc\""));
        var now = DateTimeOffset.UtcNow;
        var client = new GitHubReleasesClient(new HttpClient(handler), () => now);

        await client.GetReleasesAsync();
        now += TimeSpan.FromMinutes(1);
        var second = await client.GetReleasesAsync();

        Assert.Equal(2, handler.Calls);
        Assert.Equal("\"abc\"", handler.LastIfNoneMatch);
        Assert.Equal(3, second!.Value.GetArrayLength());
    }

    [Fact]
    public async Task Un_refus_de_GitHub_garde_la_derniere_reponse_connue()
    {
        var calls = 0;
        var handler = new CountingHandler(_ => ++calls == 1 ? Ok(ReleasesJson) : new HttpResponseMessage(HttpStatusCode.Forbidden));
        var now = DateTimeOffset.UtcNow;
        var client = new GitHubReleasesClient(new HttpClient(handler), () => now);

        await client.GetReleasesAsync();
        now += TimeSpan.FromMinutes(1);
        var afterRateLimit = await client.GetReleasesAsync();

        Assert.NotNull(afterRateLimit);
    }

    [Fact]
    public async Task Le_changelog_ignore_les_brouillons()
    {
        var client = new GitHubReleasesClient(new HttpClient(new CountingHandler(_ => Ok(ReleasesJson))));
        var service = new ReleaseChangelogService(client);

        var entries = await service.GetRecentReleasesAsync(4);

        Assert.Equal(["v1.12.0", "v1.11.2"], entries.Select(e => e.Tag));
        Assert.True(entries[0].IsLatest);
    }

    private const string ReleasesFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom" xmlns:media="http://search.yahoo.com/mrss/" xml:lang="en-US">
          <title>Release notes from ALlauncher</title>
          <entry>
            <id>tag:github.com,2008:Repository/1/v1.15.1</id>
            <updated>2026-10-02T13:31:23Z</updated>
            <link rel="alternate" type="text/html" href="https://github.com/Omneria/ALlauncher/releases/tag/v1.15.1"/>
            <title>v1.15.1</title>
            <content type="html">&lt;ul&gt;
        &lt;li&gt;Le tchat du serveur fonctionne. Le jeu se lan&#231;ait sans d&#233;clarer ton compte, &lt;strong&gt;&#171; Tchat d&#233;sactiv&#233; &#187;&lt;/strong&gt; &amp;amp; l&#39;absence de cl&#233;.&lt;/li&gt;
        &lt;li&gt;Seconde
        ligne avec &lt;a href=&quot;https://x&quot;&gt;un lien&lt;/a&gt;.&lt;/li&gt;
        &lt;/ul&gt;</content>
          </entry>
          <entry>
            <id>tag:github.com,2008:Repository/1/v1.15.0</id>
            <updated>2026-09-28T19:07:55Z</updated>
            <link rel="alternate" type="text/html" href="https://github.com/Omneria/ALlauncher/releases/tag/v1.15.0"/>
            <title>v1.15.0</title>
            <content type="html">&lt;p&gt;Sans liste&lt;/p&gt;</content>
          </entry>
        </feed>
        """;

    [Fact]
    public async Task Le_changelog_se_rabat_sur_le_flux_quand_lAPI_refuse()
    {
        var handler = new CountingHandler(request => request.RequestUri!.Host == "api.github.com"
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : Ok(ReleasesFeed));
        var http = new HttpClient(handler);
        var service = new ReleaseChangelogService(new GitHubReleasesClient(http), http);

        var entries = await service.GetRecentReleasesAsync(4);

        Assert.Equal(["v1.15.1", "v1.15.0"], entries.Select(e => e.Tag));
        Assert.True(entries[0].IsLatest);
        Assert.Equal("https://github.com/Omneria/ALlauncher/releases/tag/v1.15.1", entries[0].Url);
        Assert.Equal<string>(
            [
                "Le tchat du serveur fonctionne. Le jeu se lançait sans déclarer ton compte, « Tchat désactivé » & l'absence de clé.",
                "Seconde ligne avec un lien.",
            ],
            entries[0].Notes);
        Assert.Equal<string>([ReleaseChangelogService.MissingNotesPlaceholder], entries[1].Notes);
    }

    [Fact]
    public async Task Le_changelog_ne_touche_pas_au_flux_quand_lAPI_repond()
    {
        var handler = new CountingHandler(request => request.RequestUri!.Host == "api.github.com"
            ? Ok(ReleasesJson)
            : throw new InvalidOperationException("Le flux ne devrait pas être appelé."));
        var http = new HttpClient(handler);
        var service = new ReleaseChangelogService(new GitHubReleasesClient(http), http);

        var entries = await service.GetRecentReleasesAsync(4);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(["v1.12.0", "v1.11.2"], entries.Select(e => e.Tag));
    }

    [Fact]
    public async Task Le_changelog_reste_vide_sans_flux_de_secours_et_sans_API()
    {
        var http = new HttpClient(new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var service = new ReleaseChangelogService(new GitHubReleasesClient(http));

        Assert.Empty(await service.GetRecentReleasesAsync(4));
    }

    [Theory]
    [InlineData("1.12.0+abc123", false, "1.12.0")]
    [InlineData("1.12.0-dev.153+abc123", true, "1.12.0-dev.153")]
    [InlineData("1.12.0-dev.153", true, "1.12.0-dev.153")]
    [InlineData(null, false, "1.12.0")]
    public void AppVersion_reconnait_un_build_de_test(string? informationalVersion, bool isDev, string display)
    {
        Assert.Equal(isDev, AppVersion.IsDevLabel(informationalVersion));
        Assert.Equal(display, AppVersion.DisplayLabel(informationalVersion, new Version(1, 12, 0)));
    }

    [Theory]
    [InlineData("1.12.0", "1.12.0", false, false)] // même version, release : rien à proposer
    [InlineData("1.12.0", "1.12.0", true, true)]   // build de test 1.12.0-dev : la release le remplace
    [InlineData("1.11.2", "1.12.0", true, false)]  // release plus ancienne : jamais proposée
    [InlineData("1.12.1", "1.12.0", false, true)]
    public void AppVersion_IsUpdate_traite_un_build_de_test_comme_anterieur(string remote, string current, bool currentIsDev, bool expected)
    {
        Assert.Equal(expected, AppVersion.IsUpdate(Version.Parse(remote), Version.Parse(current), currentIsDev));
    }

    private static HttpResponseMessage Ok(string json, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (etag is not null)
        {
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        }

        return response;
    }

    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string? LastIfNoneMatch { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastIfNoneMatch = request.Headers.IfNoneMatch.FirstOrDefault()?.ToString();
            return Task.FromResult(respond(request));
        }
    }
}
