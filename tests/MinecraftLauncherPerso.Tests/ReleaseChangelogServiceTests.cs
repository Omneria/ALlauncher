using MinecraftLauncherPerso.Services.Changelog;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// CleanNotes doit rester aligné avec la fonction JS équivalente de la landing page
/// (Omneria-landing/index.html, cleanNotes) : même source (corps d'une release GitHub) et mêmes
/// règles de nettoyage, pour que le changelog affiché dans le launcher et sur le site se
/// ressemblent. Cas réels tirés des releases déjà publiées du dépôt.
/// </summary>
public sealed class ReleaseChangelogServiceTests
{
    [Fact]
    public void CleanNotes_ne_garde_que_les_lignes_a_puces()
    {
        var body = """
            ## What's Changed
            * Merge dev dans main : correctif urgent by @dro0id in https://github.com/Omneria/ALlauncher/pull/21

            **Full Changelog**: https://github.com/Omneria/ALlauncher/compare/v1.9.1...v1.9.2
            """;

        var notes = ReleaseChangelogService.CleanNotes(body);

        Assert.Equal(["Merge dev dans main : correctif urgent"], notes);
    }

    [Fact]
    public void CleanNotes_retire_liens_markdown_gras_et_backticks()
    {
        var body = "- Corrige `InvariantGlobalization` et **le crash** au démarrage ([détails](https://github.com/x))";

        var notes = ReleaseChangelogService.CleanNotes(body);

        Assert.Equal(["Corrige InvariantGlobalization et le crash au démarrage (détails)"], notes);
    }

    [Fact]
    public void CleanNotes_accepte_les_puces_etoile_et_tiret()
    {
        var body = """
            - Première ligne
            * Deuxième ligne
            Pas une puce, ignorée
            """;

        var notes = ReleaseChangelogService.CleanNotes(body);

        Assert.Equal(["Première ligne", "Deuxième ligne"], notes);
    }

    [Fact]
    public void CleanNotes_limite_a_quatre_notes()
    {
        var body = string.Join('\n', Enumerable.Range(1, 6).Select(i => $"- Note {i}"));

        var notes = ReleaseChangelogService.CleanNotes(body);

        Assert.Equal(4, notes.Count);
        Assert.Equal("Note 1", notes[0]);
        Assert.Equal("Note 4", notes[^1]);
    }

    [Fact]
    public void CleanNotes_renvoie_une_liste_vide_si_le_corps_est_absent_ou_vide()
    {
        Assert.Empty(ReleaseChangelogService.CleanNotes(null));
        Assert.Empty(ReleaseChangelogService.CleanNotes(""));
    }
}
