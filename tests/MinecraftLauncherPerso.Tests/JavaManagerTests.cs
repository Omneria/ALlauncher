using MinecraftLauncherPerso.Services.Java;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// Forge 1.16.5 exige explicitement Java 8 (JavaManager vérifie la version majeure exacte, pas
/// juste ">= 8") : un bug dans le parsing de "java -version" ferait accepter un Java 11/17 comme
/// étant du Java 8 (ou l'inverse, rejeter un vrai Java 8), avec un échec de lancement du jeu
/// difficile à relier à sa vraie cause. Ce parsing n'était jusqu'ici couvert par aucun test.
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
}
