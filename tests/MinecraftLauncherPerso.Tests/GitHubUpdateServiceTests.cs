using MinecraftLauncherPerso.Services.Update;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// Le README documente un incident passé (v1.2.5) où une dérive entre &lt;Version&gt; du csproj et
/// les tags réellement publiés a cassé l'auto-update en silence. TryParseVersion (comparaison
/// tag_name GitHub -> version installée) est le cœur de cette logique et n'était jusqu'ici couvert
/// par aucun test.
/// </summary>
public sealed class GitHubUpdateServiceTests
{
    [Theory]
    [InlineData("v1.5.2", true, "1.5.2")]
    [InlineData("V1.5.2", true, "1.5.2")] // casse : GitHub Actions ne déclenche que sur "v*" minuscule, mais le parsing lui-même doit rester tolérant
    [InlineData("1.5.2", true, "1.5.2")]
    [InlineData("v2.0.0", true, "2.0.0")]
    [InlineData("v1.0", true, "1.0")]
    public void TryParseVersion_accepte_les_formats_de_tag_valides(string tagName, bool expectedSuccess, string expectedVersion)
    {
        var success = GitHubUpdateService.TryParseVersion(tagName, out var version);

        Assert.Equal(expectedSuccess, success);
        Assert.Equal(Version.Parse(expectedVersion), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("release-1.5.2")]
    public void TryParseVersion_rejette_les_formats_invalides(string tagName)
    {
        var success = GitHubUpdateService.TryParseVersion(tagName, out var version);

        Assert.False(success);
        Assert.Equal(new Version(0, 0, 0), version);
    }

    [Fact]
    public void Une_version_plus_recente_est_bien_superieure()
    {
        // Régression directe de l'incident v1.2.5 : GetCurrentVersion() (assembly) comparé à la
        // version du tag distant via l'opérateur > standard de System.Version.
        GitHubUpdateService.TryParseVersion("v1.5.2", out var remote);
        GitHubUpdateService.TryParseVersion("v1.5.10", out var newerRemote);

        Assert.True(newerRemote > remote); // .10 doit être supérieur à .2 (pas une comparaison de chaînes)
    }
}
