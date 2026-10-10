using MinecraftLauncherPerso.Services.Launch;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>Liste d'étapes du lancement (v2.0.0) : états fait / en cours / à venir / échec.</summary>
public sealed class LaunchStepStatesTests
{
    [Fact]
    public void Sans_adresse_de_serveur_la_liste_saute_lecriture_de_la_liste_des_serveurs()
    {
        var steps = LaunchStepStates.StepsFor(includeServerList: false);

        Assert.Equal(
            [LaunchStep.Java, LaunchStep.Forge, LaunchStep.ModSync, LaunchStep.Auth, LaunchStep.Launch],
            steps);
        Assert.Equal(6, LaunchStepStates.StepsFor(includeServerList: true).Count);
    }

    [Fact]
    public void Avant_la_premiere_etape_rien_nest_commence()
    {
        foreach (var step in LaunchStepStates.StepsFor(true))
        {
            Assert.Equal(StepState.Pending, LaunchStepStates.StateOf(step, current: null, failed: null));
        }
    }

    [Fact]
    public void Letape_en_cours_est_active_les_precedentes_sont_faites_les_suivantes_a_venir()
    {
        Assert.Equal(StepState.Done, LaunchStepStates.StateOf(LaunchStep.Java, LaunchStep.ModSync, null));
        Assert.Equal(StepState.Done, LaunchStepStates.StateOf(LaunchStep.Forge, LaunchStep.ModSync, null));
        Assert.Equal(StepState.Active, LaunchStepStates.StateOf(LaunchStep.ModSync, LaunchStep.ModSync, null));
        Assert.Equal(StepState.Pending, LaunchStepStates.StateOf(LaunchStep.Auth, LaunchStep.ModSync, null));
        Assert.Equal(StepState.Pending, LaunchStepStates.StateOf(LaunchStep.Launch, LaunchStep.ModSync, null));
    }

    [Fact]
    public void Apres_un_echec_letape_est_en_echec_et_la_suite_reste_a_venir()
    {
        Assert.Equal(StepState.Done, LaunchStepStates.StateOf(LaunchStep.Forge, LaunchStep.Auth, LaunchStep.Auth));
        Assert.Equal(StepState.Failed, LaunchStepStates.StateOf(LaunchStep.Auth, LaunchStep.Auth, LaunchStep.Auth));
        Assert.Equal(StepState.Pending, LaunchStepStates.StateOf(LaunchStep.Launch, LaunchStep.Auth, LaunchStep.Auth));
    }

    [Fact]
    public void Un_echec_prime_sur_letape_en_cours()
    {
        // L'échec peut être rapporté sans étape en cours (current null) : il décide seul.
        Assert.Equal(StepState.Failed, LaunchStepStates.StateOf(LaunchStep.Java, current: null, failed: LaunchStep.Java));
        Assert.Equal(StepState.Pending, LaunchStepStates.StateOf(LaunchStep.Forge, current: null, failed: LaunchStep.Java));
    }
}
