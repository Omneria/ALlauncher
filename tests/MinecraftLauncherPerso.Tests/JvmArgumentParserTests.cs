using MinecraftLauncherPerso.Services.Launch;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>Arguments JVM saisis dans Paramètres > Jeu : découpage et refus de ce qui n'en est pas un.</summary>
public sealed class JvmArgumentParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Une_saisie_vide_ne_donne_aucun_argument(string? text)
    {
        var result = JvmArgumentParser.Parse(text);

        Assert.True(result.IsValid);
        Assert.Empty(result.Arguments);
    }

    [Fact]
    public void Les_arguments_sont_separes_par_des_espaces()
    {
        var result = JvmArgumentParser.Parse("-XX:+UseG1GC   -Dfoo=bar\t-XX:MaxGCPauseMillis=50");

        Assert.True(result.IsValid);
        Assert.Equal(["-XX:+UseG1GC", "-Dfoo=bar", "-XX:MaxGCPauseMillis=50"], result.Arguments);
    }

    [Fact]
    public void Un_chemin_entre_guillemets_reste_un_seul_argument()
    {
        var result = JvmArgumentParser.Parse("-XX:ErrorFile=\"D:\\Mes jeux\\hs_err.log\" -Dx=1");

        Assert.True(result.IsValid);
        Assert.Equal([@"-XX:ErrorFile=D:\Mes jeux\hs_err.log", "-Dx=1"], result.Arguments);
    }

    [Fact]
    public void Le_contournement_du_JIT_du_crash_mezz_config_est_accepte()
    {
        var result = JvmArgumentParser.Parse("-XX:CompileCommand=exclude,net.mezzdev.config.schema.ConfigSchema::loadIfNeededWithoutNotifying");

        Assert.True(result.IsValid);
        Assert.Single(result.Arguments);
    }

    [Theory]
    [InlineData("UseG1GC")]
    [InlineData("-Dok=1 nimporte")]
    public void Un_mot_qui_ne_commence_pas_par_un_tiret_est_refuse(string text)
    {
        var result = JvmArgumentParser.Parse(text);

        Assert.False(result.IsValid);
        Assert.Contains("commencer par", result.Error);
    }

    [Theory]
    [InlineData("-Xmx8G")]
    [InlineData("-xms2g")]
    public void La_RAM_reste_reglee_par_le_curseur(string text)
    {
        var result = JvmArgumentParser.Parse(text);

        Assert.False(result.IsValid);
        Assert.Contains("curseur de RAM", result.Error);
    }

    [Fact]
    public void Un_guillemet_non_referme_est_refuse()
    {
        Assert.Contains("guillemet", JvmArgumentParser.Parse("-Dx=\"abc").Error);
    }

    [Fact]
    public void Une_saisie_sur_plusieurs_lignes_ou_trop_longue_est_refusee()
    {
        Assert.False(JvmArgumentParser.Parse("-Da=1\n-Db=2").IsValid);
        Assert.False(JvmArgumentParser.Parse("-D" + new string('a', JvmArgumentParser.MaxLength)).IsValid);
    }
}
