using MinecraftLauncherPerso.Services.Java;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// Forge 1.20.1 exige explicitement Java 17 (JavaManager vérifie la version majeure exacte, pas
/// juste ">= 17") : un bug dans le parsing de "java -version" ferait accepter un Java 11/21 comme
/// étant du Java 17 (ou l'inverse, rejeter un vrai Java 17), avec un échec de lancement du jeu
/// difficile à relier à sa vraie cause. Ce parsing n'était jusqu'ici couvert par aucun test.
/// Les cas Java 8 (ancien schéma de version) restent testés : ParseMajorVersion doit rester
/// correct pour toutes les versions majeures, pas seulement 17.
/// </summary>
public sealed class JavaManagerTests
{
    [Theory]
    [InlineData("1.8.0_392", 8)] // ancien schéma ("1.8.0_xxx") : la version majeure réelle est le 2e segment
    [InlineData("1.8.0", 8)]
    [InlineData("8.0.392", 8)] // certaines distributions Temurin utilisent déjà le nouveau schéma pour Java 8
    [InlineData("17.0.9", 17)] // nouveau schéma (9+) : le 1er segment est la version majeure
    [InlineData("11.0.2", 11)]
    [InlineData("21", 21)]
    public void ParseMajorVersion_extrait_la_bonne_version_majeure(string versionString, int expectedMajor)
    {
        Assert.Equal(expectedMajor, JavaManager.ParseMajorVersion(versionString));
    }

    [Theory]
    [InlineData("")]
    [InlineData("n/a")]
    public void ParseMajorVersion_retourne_zero_pour_une_chaine_non_reconnue(string versionString)
    {
        Assert.Equal(0, JavaManager.ParseMajorVersion(versionString));
    }

    [Fact]
    public void ParseVersionOutput_extrait_la_version_de_la_sortie_reelle_de_java_version()
    {
        // Sortie typique de "java -version" pour un Temurin 8 (écrite sur stderr par la JVM elle-même).
        const string output = """
            openjdk version "1.8.0_392"
            OpenJDK Runtime Environment (Temurin)(build 1.8.0_392-b08)
            OpenJDK 64-Bit Server VM (Temurin)(build 25.392-b08, mixed mode)
            """;

        var info = JavaManager.ParseVersionOutput(@"C:\java\bin\java.exe", output);

        Assert.NotNull(info);
        Assert.Equal(8, info!.MajorVersion);
        Assert.Equal("1.8.0_392", info.VersionString);
        Assert.Equal(@"C:\java\bin\java.exe", info.ExecutablePath);
    }

    [Fact]
    public void ParseVersionOutput_retourne_null_si_aucune_ligne_version_reconnaissable()
    {
        var info = JavaManager.ParseVersionOutput(@"C:\notjava.exe", "command not found");

        Assert.Null(info);
    }

    [Theory]
    [InlineData("17.0.12", 12)]
    [InlineData("17.0.14+7", 14)]
    [InlineData("17.0.9", 9)]
    [InlineData("17", 0)]
    [InlineData("17.0", 0)]
    [InlineData("1.8.0_392", 0)] // ancien schéma : le "0" est le 3e segment, aucune importance pour Java 17
    public void ParseUpdate_extrait_le_correctif(string versionString, int expectedUpdate)
    {
        Assert.Equal(expectedUpdate, JavaManager.ParseUpdate(versionString));
    }

    [Theory]
    [InlineData("17.0.12", false)] // Oracle de juillet 2024 : a fait planter le jeu en code JIT (v2.2.0)
    [InlineData("17.0.13", false)]
    [InlineData("17.0.14", true)]
    [InlineData("17.0.16", true)]
    public void IsRecentEnough_exige_un_correctif_minimal(string versionString, bool expected)
    {
        var info = new Models.JavaVersionInfo(@"C:\Javain\java.exe", versionString, JavaManager.ParseMajorVersion(versionString));

        Assert.Equal(expected, JavaManager.IsRecentEnough(info));
    }

    [Fact]
    public void Une_autre_version_majeure_n_est_jamais_assez_recente()
    {
        Assert.False(JavaManager.IsRecentEnough(new Models.JavaVersionInfo(@"C:\Javain\java.exe", "21.0.5", 21)));
        Assert.False(JavaManager.IsRecentEnough(null));
    }

    [Fact]
    public void PickNewest_prefere_le_correctif_le_plus_recent_et_ignore_les_autres_versions_majeures()
    {
        var old = new Models.JavaVersionInfo("old", "17.0.12", 17);
        var recent = new Models.JavaVersionInfo("recent", "17.0.16", 17);
        var java21 = new Models.JavaVersionInfo("21", "21.0.9", 21);

        Assert.Equal("recent", JavaManager.PickNewest(old, null, java21, recent)!.ExecutablePath);
        Assert.Null(JavaManager.PickNewest(null, java21));
    }
}
