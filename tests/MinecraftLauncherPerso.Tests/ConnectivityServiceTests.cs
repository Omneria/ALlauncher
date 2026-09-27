using System.Net;
using System.Net.Http;
using MinecraftLauncherPerso.Services.Network;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// État "pas de connexion internet" (v1.14.0) : hors ligne seulement si Windows ne voit aucun
/// réseau ou si aucune sonde ne répond — un simple 404 prouve que la connexion marche.
/// </summary>
public sealed class ConnectivityServiceTests
{
    private static readonly string[] Probes = ["https://vps.test/modpack/manifest.json", "https://api.github.test/"];

    [Fact]
    public async Task IsOnlineAsync_hors_ligne_si_windows_ne_voit_aucun_reseau()
    {
        var handler = new ProbeHandler(_ => HttpStatusCode.OK);
        var service = new ConnectivityService(new HttpClient(handler), isNetworkAvailable: () => false);

        Assert.False(await service.IsOnlineAsync(Probes));
        Assert.Equal(0, handler.Calls); // aucune requête inutile
    }

    [Fact]
    public async Task IsOnlineAsync_hors_ligne_si_aucune_sonde_ne_repond()
    {
        var service = new ConnectivityService(new HttpClient(new ProbeHandler(_ => null)), isNetworkAvailable: () => true);

        Assert.False(await service.IsOnlineAsync(Probes));
    }

    [Fact]
    public async Task IsOnlineAsync_en_ligne_si_une_seule_sonde_repond_meme_en_erreur()
    {
        // VPS injoignable mais GitHub répond 404 : la connexion du joueur fonctionne.
        var service = new ConnectivityService(
            new HttpClient(new ProbeHandler(uri => uri.Host == "api.github.test" ? HttpStatusCode.NotFound : null)),
            isNetworkAvailable: () => true);

        Assert.True(await service.IsOnlineAsync(Probes));
    }

    [Fact]
    public async Task IsOnlineAsync_en_ligne_sans_adresse_valide_a_sonder()
    {
        var service = new ConnectivityService(new HttpClient(new ProbeHandler(_ => null)), isNetworkAvailable: () => true);

        Assert.True(await service.IsOnlineAsync(["", "pas une url"]));
    }

    /// <summary>Répond le code donné, ou échoue comme un réseau coupé si null.</summary>
    private sealed class ProbeHandler(Func<Uri, HttpStatusCode?> respond) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            var status = respond(request.RequestUri!);
            return status is null
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("Réseau injoignable"))
                : Task.FromResult(new HttpResponseMessage(status.Value));
        }
    }
}
