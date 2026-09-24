using MinecraftLauncherPerso.Services.Launch;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// CoalescingProgress (v1.11.1) : corrige le gel du launcher pendant les téléchargements, où
/// chaque rapport de progression de CmlLib (des milliers par seconde) devenait un message pour le
/// thread UI. Ne doit appliquer que la dernière valeur, uniquement au Flush, et rester sûr quand
/// plusieurs threads rapportent en même temps.
/// </summary>
public sealed class CoalescingProgressTests
{
    [Fact]
    public void Report_nappelle_rien_avant_Flush()
    {
        var applied = new List<string>();
        var progress = new CoalescingProgress<string>(applied.Add);

        progress.Report("a");
        progress.Report("b");

        Assert.Empty(applied);
    }

    [Fact]
    public void Flush_applique_seulement_la_derniere_valeur()
    {
        var applied = new List<string>();
        var progress = new CoalescingProgress<string>(applied.Add);

        progress.Report("10 %");
        progress.Report("20 %");
        progress.Report("30 %");
        progress.Flush();

        Assert.Equal(["30 %"], applied);
    }

    [Fact]
    public void Flush_sans_nouveau_rapport_ne_fait_rien()
    {
        var applied = new List<string>();
        var progress = new CoalescingProgress<string>(applied.Add);

        progress.Report("a");
        progress.Flush();
        progress.Flush();

        Assert.Equal(["a"], applied);
    }

    [Fact]
    public async Task Rapports_concurrents_depuis_plusieurs_threads_sans_perte_de_la_derniere_valeur()
    {
        var applied = new List<string>();
        var progress = new CoalescingProgress<string>(applied.Add);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 10_000; i++)
            {
                progress.Report($"{t}-{i}");
            }
        })));
        progress.Report("final");
        progress.Flush();

        Assert.Equal(["final"], applied);
    }
}
