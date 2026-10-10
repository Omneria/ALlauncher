using System.Net;
using System.Net.Http;
using MinecraftLauncherPerso.Services.Skins;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>Têtes des joueurs de la page SERVEUR : validation des pseudos, cache, secours entre services.</summary>
public sealed class PlayerAvatarServiceTests : IDisposable
{
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "avatar-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, recursive: true);
        }
    }

    [Theory]
    [InlineData("Dro0id", true)]
    [InlineData("DroOid_", true)]
    [InlineData("ab", false)]
    [InlineData("pseudo_beaucoup_trop_long", false)]
    [InlineData("../evil", false)]
    [InlineData("a b c", false)]
    [InlineData(null, false)]
    public void Seuls_les_pseudos_valides_sont_acceptes(string? name, bool expected)
    {
        Assert.Equal(expected, PlayerAvatarService.IsValidName(name));
    }

    [Fact]
    public async Task Un_pseudo_invalide_ne_declenche_aucune_requete()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });
        var service = new PlayerAvatarService(new HttpClient(handler), _cache);

        Assert.Null(await service.GetAvatarAsync("../x"));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task La_tete_est_mise_en_cache_et_relue_sans_reseau()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        var service = new PlayerAvatarService(new HttpClient(handler), _cache);

        var first = await service.GetAvatarAsync("Dro0id");
        var second = await service.GetAvatarAsync("Dro0id");

        Assert.Equal([1, 2, 3], first);
        Assert.Equal([1, 2, 3], second);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Le_second_service_prend_le_relais_quand_le_premier_echoue()
    {
        var handler = new FakeHandler(request => request.RequestUri!.Host.Contains("mc-heads")
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([9]) });
        var service = new PlayerAvatarService(new HttpClient(handler), _cache);

        Assert.Equal([9], await service.GetAvatarAsync("Nyx_"));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Sans_reseau_une_tete_perimee_en_cache_est_conservee()
    {
        Directory.CreateDirectory(_cache);
        var service = new PlayerAvatarService(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))), _cache);
        var path = service.CachePathFor("Kaelith");
        await File.WriteAllBytesAsync(path, [7, 7]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));

        Assert.Equal([7, 7], await service.GetAvatarAsync("Kaelith"));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }
}
