namespace MinecraftLauncherPerso.Services.Launch;

/// <summary>État d'une étape dans la liste du lancement (v2.0.0 : fait / en cours / à venir / échec).</summary>
public enum StepState
{
    Pending,
    Active,
    Done,
    Failed,
}

/// <summary>
/// Calcule l'état de chaque étape de <see cref="LaunchPipeline"/> à partir de l'étape en cours ou
/// de celle qui a échoué. Séparé de l'interface pour pouvoir être testé sans fenêtre.
/// </summary>
public static class LaunchStepStates
{
    /// <summary>Les étapes affichées, dans l'ordre du pipeline. L'écriture de la liste des serveurs
    /// n'a lieu que si une adresse de serveur est configurée (voir LaunchPipeline).</summary>
    public static IReadOnlyList<LaunchStep> StepsFor(bool includeServerList) =>
        Enum.GetValues<LaunchStep>().Where(step => includeServerList || step != LaunchStep.ServerList).ToList();

    /// <summary>
    /// Après un échec, tout ce qui précède l'étape en échec est fait et tout ce qui suit reste à
    /// venir. Sans échec, tout ce qui précède l'étape en cours est fait ; sans étape en cours,
    /// rien n'a commencé.
    /// </summary>
    public static StepState StateOf(LaunchStep step, LaunchStep? current, LaunchStep? failed)
    {
        if (failed is { } failedStep)
        {
            return Compare(step, failedStep, StepState.Failed);
        }

        return current is { } currentStep ? Compare(step, currentStep, StepState.Active) : StepState.Pending;
    }

    private static StepState Compare(LaunchStep step, LaunchStep reference, StepState atReference)
    {
        if (step < reference)
        {
            return StepState.Done;
        }

        return step == reference ? atReference : StepState.Pending;
    }
}
