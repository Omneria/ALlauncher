using MinecraftLauncherPerso.Services.Launch;
using MinecraftLauncherPerso.ViewModels;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>Bloc de lancement du hub : progression, états des étapes, disponibilité de JOUER.</summary>
public sealed class LaunchViewModelTests
{
    private static readonly IReadOnlyList<LaunchStep> Order = LaunchStepStates.VisibleSteps();

    private static LaunchViewModel Started()
    {
        var vm = new LaunchViewModel();
        vm.BeginLaunch(Order);
        return vm;
    }

    [Fact]
    public void Au_depart_le_bloc_est_visible_avec_toutes_les_etapes_a_venir()
    {
        var vm = Started();

        Assert.True(vm.IsPanelVisible);
        Assert.True(vm.IsLaunching);
        Assert.False(vm.PlayEnabled);
        Assert.Equal(4, vm.Steps.Count);
        Assert.All(vm.Steps, step => Assert.Equal(StepState.Pending, step.State));
        Assert.Equal("0 / 4 · 0 %", vm.CountText);
    }

    [Fact]
    public void La_progression_globale_mele_etapes_terminees_et_fraction_en_cours()
    {
        var vm = Started();

        vm.Report(LaunchStep.ModSync, 0.5, "Téléchargement du modpack...");

        // Java et Forge terminés (2) + moitié de la synchro : 2,5 / 4.
        Assert.Equal(62.5, vm.Progress);
        Assert.StartsWith("3 / 4", vm.CountText);
        Assert.Equal(StepState.Done, vm.Steps[0].State);
        Assert.Equal(StepState.Done, vm.Steps[1].State);
        Assert.Equal(StepState.Active, vm.Steps[2].State);
        Assert.Equal("", vm.Steps[2].Detail);
    }

    [Fact]
    public void Une_etape_terminee_garde_un_resume_court()
    {
        var vm = Started();

        vm.Report(LaunchStep.Java, 1, "Java 17 prêt : C:\\java");
        vm.Report(LaunchStep.Forge, null, "Forge prêt : 1.20.1-forge-47.3.0");
        vm.Report(LaunchStep.ModSync, null, "Synchronisation...");

        Assert.Equal("prêt", vm.Steps[0].Detail);
        Assert.Equal("Forge 47.3.0", vm.Steps[1].Label);
        Assert.Equal("prêt", vm.Steps[1].Detail);
    }

    [Fact]
    public void Les_etapes_non_listees_comptent_apres_la_derniere_etape_visible()
    {
        var vm = Started();

        vm.Report(LaunchStep.Launch, null, "Jeu lancé.");

        Assert.Equal(100, vm.Progress);
        Assert.All(vm.Steps, step => Assert.Equal(StepState.Done, step.State));
    }

    [Fact]
    public void Un_echec_marque_la_ligne_et_rattache_une_etape_cachee_a_la_derniere_ligne()
    {
        var vm = Started();
        vm.Report(LaunchStep.Forge, null, "Forge...");

        vm.Fail(LaunchStep.Forge, "Erreur : réseau");
        Assert.Equal(StepState.Failed, vm.Steps[1].State);
        Assert.True(vm.StatusIsError);
        Assert.Equal("Erreur : réseau", vm.StatusMessage);

        var hidden = Started();
        hidden.Fail(LaunchStep.ServerList, "Erreur : servers.dat");
        Assert.Equal(StepState.Failed, hidden.Steps[^1].State);
    }

    [Fact]
    public void Apres_un_echec_FERMER_replie_le_bloc_et_JOUER_revient()
    {
        var vm = Started();
        vm.Fail(LaunchStep.Auth, "Erreur");

        vm.FailedLaunch();

        Assert.Equal("FERMER", vm.CancelLabel);
        Assert.True(vm.IsClosingFailure);
        Assert.True(vm.CancelVisible);
        Assert.True(vm.PlayEnabled);

        vm.ClosePanel();
        Assert.False(vm.IsPanelVisible);
    }

    [Fact]
    public void Un_serveur_hors_ligne_grise_JOUER_et_propose_LANCER_QUAND_MEME()
    {
        var vm = new LaunchViewModel { ServerOffline = true };

        Assert.False(vm.PlayEnabled);
        Assert.True(vm.LaunchAnywayVisible);
        Assert.True(vm.LaunchAnywayEnabled);
        Assert.False(vm.ReadyMetaVisible);

        vm.BeginLaunch(Order);
        Assert.False(vm.LaunchAnywayEnabled);
    }

    [Fact]
    public void Le_jeu_lance_garde_JOUER_grise_jusqua_sa_fermeture()
    {
        var vm = Started();

        vm.Succeeded();
        Assert.False(vm.IsPanelVisible);
        Assert.False(vm.PlayEnabled);

        vm.GameEnded();
        Assert.True(vm.PlayEnabled);
    }

    [Fact]
    public void Lannulation_replie_le_bloc_et_redonne_la_main()
    {
        var vm = Started();
        vm.EnableCancel();

        vm.RequestCancel();
        Assert.False(vm.CancelEnabled);
        Assert.Equal("Annulation en cours...", vm.StatusMessage);

        vm.Cancelled();
        Assert.False(vm.IsPanelVisible);
        Assert.True(vm.PlayEnabled);
        Assert.Equal(0, vm.Progress);
    }

    [Fact]
    public void La_mise_a_jour_du_launcher_reutilise_le_bloc_sans_liste_detapes()
    {
        var vm = new LaunchViewModel();

        vm.BeginUpdate();
        vm.Log("Téléchargement...");

        Assert.True(vm.IsPanelVisible);
        Assert.False(vm.ShowSteps);
        Assert.True(vm.ShowSpinner);
        Assert.Equal("MISE À JOUR", vm.Title);
    }

    [Fact]
    public void Les_changements_de_propriete_sont_notifies()
    {
        var vm = new LaunchViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ServerOffline = true;

        Assert.Contains(nameof(LaunchViewModel.PlayEnabled), changed);
        Assert.Contains(nameof(LaunchViewModel.LaunchAnywayVisible), changed);
    }
}
