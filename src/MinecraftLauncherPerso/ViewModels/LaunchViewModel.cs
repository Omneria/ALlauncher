using System.Collections.ObjectModel;
using MinecraftLauncherPerso.Services.Launch;

namespace MinecraftLauncherPerso.ViewModels;

/// <summary>
/// État et logique d'affichage du bloc de droite de la carte héros : bouton JOUER, liste des étapes du
/// lancement, progression, annulation, mise à jour du launcher. Aucune dépendance à WPF : testé sans
/// fenêtre (LaunchViewModelTests). La fenêtre pilote ce view-model (Begin, Report, Fail...) et le XAML
/// s'y lie.
/// </summary>
public sealed class LaunchViewModel : ObservableObject
{
    private bool _isPanelVisible;
    private bool _isLaunching;
    private bool _serverOffline;
    private bool _showSteps;
    private bool _showSpinner;
    private bool _cancelVisible;
    private bool _cancelEnabled = true;
    private bool _crashLogsVisible;
    private string _title = "LANCEMENT";
    private string _countText = "";
    private double _progress;
    private string _statusMessage = "";
    private bool _statusIsError;
    private string _cancelLabel = "ANNULER";
    private string _readyMeta = "";
    private IReadOnlyList<LaunchStep> _order = [];
    private LaunchStep? _lastStep;
    private double _stepFraction;

    /// <summary>Lignes de la liste d'étapes.</summary>
    public ObservableCollection<StepItem> Steps { get; } = [];

    /// <summary>Le bloc « lancement » remplace JOUER tant qu'il est visible.</summary>
    public bool IsPanelVisible
    {
        get => _isPanelVisible;
        private set => SetProperty(ref _isPanelVisible, value);
    }

    /// <summary>Vrai du clic sur JOUER jusqu'à la fin de la partie (ou de l'échec).</summary>
    public bool IsLaunching
    {
        get => _isLaunching;
        private set
        {
            if (SetProperty(ref _isLaunching, value))
            {
                OnPropertyChanged(nameof(PlayEnabled));
                OnPropertyChanged(nameof(LaunchAnywayEnabled));
            }
        }
    }

    /// <summary>Serveur injoignable : JOUER est grisé et LANCER QUAND MÊME prend le relais.</summary>
    public bool ServerOffline
    {
        get => _serverOffline;
        set
        {
            if (SetProperty(ref _serverOffline, value))
            {
                OnPropertyChanged(nameof(PlayEnabled));
                OnPropertyChanged(nameof(LaunchAnywayVisible));
                OnPropertyChanged(nameof(ReadyMetaVisible));
            }
        }
    }

    public bool PlayEnabled => !IsLaunching && !ServerOffline;

    public bool LaunchAnywayVisible => ServerOffline;

    public bool LaunchAnywayEnabled => !IsLaunching;

    public bool ReadyMetaVisible => !ServerOffline;

    /// <summary>« MODPACK À JOUR · JAVA 17 PRÊT » sous JOUER.</summary>
    public string ReadyMeta
    {
        get => _readyMeta;
        set => SetProperty(ref _readyMeta, value);
    }

    /// <summary>« LANCEMENT » ou « MISE À JOUR ».</summary>
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>« 3 / 4 · 62 % ».</summary>
    public string CountText
    {
        get => _countText;
        private set => SetProperty(ref _countText, value);
    }

    /// <summary>Progression globale, de 0 à 100.</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public bool ShowSteps
    {
        get => _showSteps;
        private set => SetProperty(ref _showSteps, value);
    }

    public bool ShowSpinner
    {
        get => _showSpinner;
        private set => SetProperty(ref _showSpinner, value);
    }

    /// <summary>Message libre sous la liste (erreur, annulation en cours...).</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public bool CancelVisible
    {
        get => _cancelVisible;
        private set => SetProperty(ref _cancelVisible, value);
    }

    public bool CancelEnabled
    {
        get => _cancelEnabled;
        set => SetProperty(ref _cancelEnabled, value);
    }

    /// <summary>« ANNULER » pendant le lancement, « FERMER » après un échec.</summary>
    public string CancelLabel
    {
        get => _cancelLabel;
        private set
        {
            if (SetProperty(ref _cancelLabel, value))
            {
                OnPropertyChanged(nameof(IsClosingFailure));
            }
        }
    }

    /// <summary>Vrai quand le bouton ferme un lancement en échec au lieu de l'annuler.</summary>
    public bool IsClosingFailure => CancelLabel == "FERMER";

    /// <summary>VOIR LES LOGS, proposé après une sortie anormale du jeu.</summary>
    public bool CrashLogsVisible
    {
        get => _crashLogsVisible;
        set => SetProperty(ref _crashLogsVisible, value);
    }

    /// <summary>Prépare un nouveau lancement : liste d'étapes remise à zéro, progression à 0.</summary>
    public void BeginLaunch(IReadOnlyList<LaunchStep> order)
    {
        _order = order;
        _lastStep = null;
        _stepFraction = 0;
        IsLaunching = true;
        CrashLogsVisible = false;
        Title = "LANCEMENT";
        CancelLabel = "ANNULER";
        CancelEnabled = true;
        CancelVisible = false;
        ShowSpinner = false;
        StatusIsError = false;
        StatusMessage = "";

        Steps.Clear();
        foreach (var step in order)
        {
            Steps.Add(new StepItem(step, LaunchPipeline.StepLabel(step)));
        }

        ShowSteps = true;
        Progress = 0;
        CountText = $"0 / {order.Count} · 0 %";
        IsPanelVisible = true;
    }

    /// <summary>Le pipeline est en route (token d'annulation posé) : ANNULER devient disponible.</summary>
    public void EnableCancel()
    {
        CancelEnabled = true;
        CancelVisible = true;
    }

    /// <summary>Rapport du pipeline : étape en cours, fraction éventuelle de cette étape, message.</summary>
    public void Report(LaunchStep step, double? fraction, string message)
    {
        if (step != _lastStep)
        {
            _lastStep = step;
            _stepFraction = 0;
        }

        if (fraction is { } value)
        {
            _stepFraction = value;
        }

        ShowProgress(step, _stepFraction);
        ApplyStates(step, failed: null, message);
    }

    /// <summary>Étape non listée (liste des serveurs, démarrage du jeu) : comptée après la dernière listée.</summary>
    private void ShowProgress(LaunchStep step, double stepFraction)
    {
        var total = Math.Max(1, _order.Count);
        var listed = _order.ToList().IndexOf(step);
        var index = listed >= 0 ? listed : total - 1;
        var fraction = listed >= 0 ? stepFraction : 1;
        Progress = Math.Clamp((index + fraction) / total * 100, 0, 100);
        CountText = $"{index + 1} / {total} · {Progress:0} %";
    }

    private void ApplyStates(LaunchStep? current, LaunchStep? failed, string? detail)
    {
        foreach (var item in Steps)
        {
            var previous = item.State;
            item.State = LaunchStepStates.StateOf(item.Step, current, failed);
            switch (item.State)
            {
                case StepState.Active:
                    // Pas de détail sur l'étape en cours (noms de fichiers, compteurs) : l'icône et la
                    // barre suffisent. Le dernier message sert au résumé de l'étape terminée.
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

    /// <summary>
    /// Échec d'une étape : la ligne passe en erreur (une étape non listée est rattachée à la dernière
    /// ligne, pour garder RÉESSAYER accessible) et le message s'affiche sous la liste.
    /// </summary>
    public void Fail(LaunchStep? step, string message)
    {
        if (step is { } failedStep && Steps.Count > 0)
        {
            var visible = Steps.Any(item => item.Step == failedStep) ? failedStep : Steps[^1].Step;
            ApplyStates(current: null, failed: visible, detail: message);
        }

        ShowError(message);
    }

    /// <summary>Message d'erreur sous la liste (aussi utilisé hors lancement, ex. échec de mise à jour).</summary>
    public void ShowError(string message)
    {
        IsPanelVisible = true;
        ShowSpinner = false;
        StatusIsError = true;
        StatusMessage = message;
    }

    /// <summary>Message d'information sous la liste ; le spinner sert quand la liste d'étapes n'est pas affichée.</summary>
    public void Log(string message)
    {
        ShowSpinner = !ShowSteps;
        StatusIsError = false;
        StatusMessage = message;
    }

    /// <summary>Annulation demandée : le bouton se grise le temps que le pipeline s'arrête.</summary>
    public void RequestCancel()
    {
        CancelEnabled = false;
        Log("Annulation en cours...");
    }

    /// <summary>Lancement terminé avec succès : le bloc se replie, le jeu tourne (JOUER reste grisé jusqu'à sa fermeture).</summary>
    public void Succeeded()
    {
        Progress = 100;
        CancelVisible = false;
        IsPanelVisible = false;
    }

    /// <summary>Lancement annulé par le joueur.</summary>
    public void Cancelled()
    {
        Progress = 0;
        CancelVisible = false;
        IsPanelVisible = false;
        IsLaunching = false;
    }

    /// <summary>Lancement en échec : la liste reste affichée avec RÉESSAYER, FERMER replie le bloc.</summary>
    public void FailedLaunch()
    {
        CancelLabel = "FERMER";
        CancelEnabled = true;
        CancelVisible = true;
        IsLaunching = false;
    }

    /// <summary>Fin de la partie (ou sécurité du chien de garde) : JOUER redevient disponible.</summary>
    public void GameEnded()
    {
        IsLaunching = false;
        IsPanelVisible = false;
    }

    /// <summary>Replie le bloc (bouton FERMER après un échec).</summary>
    public void ClosePanel()
    {
        CancelVisible = false;
        IsPanelVisible = false;
    }

    /// <summary>Mise à jour du launcher : même bloc, sans liste d'étapes.</summary>
    public void BeginUpdate()
    {
        ShowSteps = false;
        Title = "MISE À JOUR";
        CountText = "";
        CancelVisible = false;
        Progress = 0;
        IsPanelVisible = true;
    }

    /// <summary>Résumé court d'une étape terminée (« prêt », le pseudo...) et, pour Forge, le libellé avec la version.</summary>
    public static string DoneSummary(LaunchStep step, string lastDetail, out string? label)
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
