using MinecraftLauncherPerso.Services.Launch;

namespace MinecraftLauncherPerso.ViewModels;

/// <summary>Une ligne de la liste des étapes du lancement, observable pour que la liste se mette à jour seule.</summary>
public sealed class StepItem(LaunchStep step, string label) : ObservableObject
{
    private StepState _state = StepState.Pending;
    private string _detail = "";
    private string _label = label;

    public LaunchStep Step { get; } = step;

    /// <summary>Dernier message du pipeline reçu pendant que l'étape était en cours (sert au résumé final).</summary>
    public string LastActiveDetail { get; set; } = "";

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public StepState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }
}
