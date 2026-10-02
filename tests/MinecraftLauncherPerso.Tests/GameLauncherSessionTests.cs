using System.Text;
using MinecraftLauncherPerso.Services.Auth;
using MinecraftLauncherPerso.Services.Launch;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// Session passée au jeu : avec "--userType Mojang" (valeur par défaut de CmlLib), Minecraft ne
/// demande pas sa clé de signature de chat et le serveur désactive le tchat.
/// </summary>
public sealed class GameLauncherSessionTests
{
    [Fact]
    public void ToMSession_declare_un_compte_Microsoft_avec_le_xuid_du_jeton()
    {
        var token = Jwt("""{"xuid":"2535412345678901","auth":"XBOX"}""");

        var session = GameLauncher.ToMSession(new MinecraftSession("DroOid_", "d4a7fb40-d97f-4d46-8e3f-84aba0c301ae", token));

        Assert.Equal("msa", session.UserType);
        Assert.Equal("2535412345678901", session.Xuid);
        Assert.Equal("DroOid_", session.Username);
        Assert.Equal(token, session.AccessToken);
    }

    [Theory]
    [InlineData("pas-un-jwt")]
    [InlineData("a.%%%.c")]
    [InlineData("")]
    public void ToMSession_reste_msa_quand_le_xuid_est_illisible(string token)
    {
        var session = GameLauncher.ToMSession(new MinecraftSession("DroOid_", "uuid", token));

        Assert.Equal("msa", session.UserType);
        Assert.Equal("0", session.Xuid);
    }

    [Fact]
    public void ReadXuid_ignore_un_jeton_sans_xuid()
    {
        Assert.Null(GameLauncher.ReadXuid(Jwt("""{"sub":"abc"}""")));
    }

    private static string Jwt(string payloadJson)
    {
        static string Base64Url(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{Base64Url("""{"alg":"HS256"}""")}.{Base64Url(payloadJson)}.signature";
    }
}
