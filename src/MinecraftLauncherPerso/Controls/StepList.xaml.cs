using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MinecraftLauncherPerso.Services.Launch;

namespace MinecraftLauncherPerso.Controls;

/// <summary>Une ligne de <see cref="StepList"/>, observable pour que la liste se mette à jour seule.</summary>
public sealed class StepItem(LaunchStep step, string label) : INotifyPropertyChanged
{
    private StepState _state = StepState.Pending;
    private string _detail = "";
    private string _label = label;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LaunchStep Step { get; } = step;

    /// <summary>Dernier détail affiché pendant que l'étape était en cours (sert au résumé final).</summary>
    public string LastActiveDetail { get; set; } = "";

    public string Label
    {
        get => _label;
        set => Set(ref _label, value, nameof(Label));
    }

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
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => RetryRequested?.Invoke(this, EventArgs.Empty)));
    }

    /// <summary>Clic sur RÉESSAYER, affiché sur l'étape en échec.</summary>
    public event EventHandler? RetryRequested;

    /// <summary>Remet la liste à zéro : toutes les étapes à venir.</summary>
    public void Reset(IEnumerable<LaunchStep> steps)
    {
        _items.Clear();
        foreach (var step in steps)
        {
            _items.Add(new StepItem(step, LaunchPipeline.StepLabel(step)));
        }
    }

    /// <summary>
    /// Applique l'étape en cours (ou en échec) à toutes les lignes. <paramref name="detail"/> est
    /// mémorisé pour le résumé de l'étape terminée (rien n'est affiché sur la ligne en cours) ; <paramref name="fraction"/> (0 à 1) est conservé pour
    /// un affichage futur. Une étape qui vient de se terminer garde un résumé court (voir
    /// <see cref="DoneSummary"/>).
    /// </summary>
    public void Update(LaunchStep? current, LaunchStep? failed, string? detail, double? fraction = null)
    {
        foreach (var item in _items)
        {
            var previous = item.State;
            item.State = LaunchStepStates.StateOf(item.Step, current, failed);
            switch (item.State)
            {
                case StepState.Active:
                    // Pas de détail sur l'étape en cours (noms de fichiers, compteurs) : l'icône et
                    // la barre de progression suffisent. On garde le dernier message pour le résumé.
                    item.Detail = "";
                    item.LastActiveDetail = !string.IsNullOrWhiteSpace(detail) ? detail : item.LastActiveDetail;
                    break;
                case StepState.Done when previous == StepState.Active:
                    item.Detail = DoneSummary(item.Step, item.LastActiveDetail, out var label);
                    if (label is not null)
                    {
                        item.Label = label;
                    }

                    break;
                case StepState.Done:
                    break;
                default:
                    item.Detail = "";
                    break;
            }
        }
    }

    /// <summary>Résumé court d'une étape terminée (« prêt », « déjà installé »...) et, pour Forge,
    /// le libellé avec la version (« Forge 47.3.0 »), d'après le dernier message du pipeline.</summary>
    internal static string DoneSummary(LaunchStep step, string lastDetail, out string? label)
    {
        label = null;
        switch (step)
        {
            case LaunchStep.Java:
                return "prêt";
            case LaunchStep.Forge:
                var marker = lastDetail.LastIndexOf("forge-", StringComparison.OrdinalIgnoreCase);
                if (marker >= 0)
                {
                    label = "Forge " + lastDetail[(marker + "forge-".Length)..].Trim();
                }

                return "prêt";
            case LaunchStep.Auth:
                const string prefix = "Connecté en tant que ";
                return lastDetail.StartsWith(prefix, StringComparison.Ordinal) ? lastDetail[prefix.Length..].TrimEnd('.') : "connecté";
            default:
                return "terminé";
        }
    }
}
