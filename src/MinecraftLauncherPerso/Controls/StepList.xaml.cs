using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;
using MinecraftLauncherPerso.Services.Launch;

namespace MinecraftLauncherPerso.Controls;

/// <summary>Une ligne de <see cref="StepList"/>, observable pour que la liste se mette à jour seule.</summary>
public sealed class StepItem(LaunchStep step, string label) : INotifyPropertyChanged
{
    private StepState _state = StepState.Pending;
    private string _detail = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public LaunchStep Step { get; } = step;

    public string Label { get; } = label;

    public StepState State
    {
        get => _state;
        set => Set(ref _state, value, nameof(State));
    }

    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value, nameof(Detail));
    }

    private void Set<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Liste des étapes du lancement (v2.0.0, remplace le spinner + une ligne de texte). Pilotée par
/// <see cref="Reset"/> puis <see cref="Update"/> ; le calcul des états est dans
/// <see cref="LaunchStepStates"/> (testé sans fenêtre).
/// </summary>
public partial class StepList : UserControl
{
    private readonly ObservableCollection<StepItem> _items = [];

    public StepList()
    {
        InitializeComponent();
        StepsControl.ItemsSource = _items;
    }

    /// <summary>Remet la liste à zéro : toutes les étapes à venir.</summary>
    public void Reset(IEnumerable<LaunchStep> steps)
    {
        _items.Clear();
        foreach (var step in steps)
        {
            _items.Add(new StepItem(step, LaunchPipeline.DescribeStep(step)));
        }
    }

    /// <summary>
    /// Applique l'étape en cours (ou en échec) à toutes les lignes. <paramref name="detail"/> est
    /// affiché sur la ligne en cours ou en échec, effacé sur les autres.
    /// </summary>
    public void Update(LaunchStep? current, LaunchStep? failed, string? detail)
    {
        foreach (var item in _items)
        {
            item.State = LaunchStepStates.StateOf(item.Step, current, failed);
            item.Detail = item.State is StepState.Active or StepState.Failed ? detail ?? "" : "";
        }
    }
}
