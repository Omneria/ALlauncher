using System.Net;
using System.Net.Http;
using MinecraftLauncherPerso.Services.Http;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// SchemeFallbackHandler (v1.11.0, transition HTTP → HTTPS du VPS) : repli en http:// pour le seul
/// hôte du VPS quand la connexion HTTPS échoue, mémorisé pour la session ; jamais pour un autre
/// hôte, jamais sur une réponse HTTP (même une erreur).
/// </summary>
public sealed class SchemeFallbackHandlerTests
{
    private const string VpsHost = "astralnexusmc.duckdns.org";

    [Fact]
    public async Task Retombe_en_http_quand_la_connexion_https_echoue_puis_sen_souvient()
    {
        var inner = new RecordingHandler(httpsFails: true);
        using var client = new HttpClient(new SchemeFallbackHandler(inner));

        using var first = await client.GetAsync($"https://{VpsHost}/modpack/manifest.json");
        using var second = await client.GetAsync($"https://{VpsHost}/modpack/news.txt");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(
            [
                $"https://{VpsHost}/modpack/manifest.json", // première tentative HTTPS, échoue
                $"http://{VpsHost}/modpack/manifest.json", // repli
                $"http://{VpsHost}/modpack/news.txt", // repli mémorisé : directement en HTTP
            ],
            inner.Requests);
    }

    [Fact]
    public async Task Reste_en_https_quand_le_vps_repond()
    {
        var inner = new RecordingHandler(httpsFails: false);
        using var client = new HttpClient(new SchemeFallbackHandler(inner));

        using var response = await client.GetAsync($"https://{VpsHost}/modpack/manifest.json");

        Assert.Equal([$"https://{VpsHost}/modpack/manifest.json"], inner.Requests);
    }

    [Fact]
    public async Task Ne_retombe_jamais_en_http_pour_un_autre_hote()
    {
        var inner = new RecordingHandler(httpsFails: true);
        using var client = new HttpClient(new SchemeFallbackHandler(inner));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://api.github.com/repos/x/y/releases"));

        Assert.Equal(["https://api.github.com/repos/x/y/releases"], inner.Requests);
    }

    [Fact]
    public async Task Ne_retombe_pas_en_http_sur_une_reponse_http_en_erreur()
    {
        // Un 404 HTTPS est une vraie réponse du VPS (fichier absent), pas un VPS sans TLS.
        var inner = new RecordingHandler(httpsFails: false, status: HttpStatusCode.NotFound);
        using var client = new HttpClient(new SchemeFallbackHandler(inner));

        using var response = await client.GetAsync($"https://{VpsHost}/modpack/maintenance.txt");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal([$"https://{VpsHost}/modpack/maintenance.txt"], inner.Requests);
    }

    [Fact]
    public async Task Conserve_les_entetes_de_la_requete_repliee()
    {
        var inner = new RecordingHandler(httpsFails: true);
        using var client = new HttpClient(new SchemeFallbackHandler(inner));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{VpsHost}/modpack/Algaron-modded.zip");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(1024, null);

        using var response = await client.SendAsync(request);

        Assert.Equal("bytes=1024-", inner.LastRangeHeader);
    }

    private sealed class RecordingHandler(bool httpsFails, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public string? LastRangeHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            LastRangeHeader = request.Headers.Range?.ToString();

            if (httpsFails && request.RequestUri.Scheme == Uri.UriSchemeHttps)
            {
                // Comme SocketsHttpHandler sur un port 443 fermé : HttpRequestException sans StatusCode.
                throw new HttpRequestException("Connexion refusée (simulée)");
            }

            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("ok") });
        }
    }
}
