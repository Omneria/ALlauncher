namespace MinecraftLauncherPerso.ViewModels;

/// <summary>Pages de la fenêtre principale.</summary>
public enum AppPage
{
    Hub,
    Actus,
    Server,
    Settings,
    Logs,
    Legal,
}

/// <summary>
/// View-model racine (DataContext de la fenêtre principale) : page affichée, libellé de la barre de
/// titre, état de la mise à jour du launcher, et les view-models du hub (lancement, carte héros,
/// tuiles, bannière de maintenance, profil).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private AppPage _currentPage = AppPage.Hub;
    private bool _updateAvailable;
    private string _updateText = "MISE À JOUR";
    private bool _updateEnabled = true;

    public LaunchViewModel Launch { get; } = new();

    public HeroViewModel Hero { get; } = new();

    public TilesViewModel Tiles { get; } = new();

    public MaintenanceViewModel Maintenance { get; } = new();

    public ProfileViewModel Profile { get; } = new();

    public AppPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsHubPage));
                OnPropertyChanged(nameof(IsNotHubPage));
                OnPropertyChanged(nameof(IsActusPage));
                OnPropertyChanged(nameof(IsServerPage));
                OnPropertyChanged(nameof(IsSettingsPage));
                OnPropertyChanged(nameof(IsLogsPage));
                OnPropertyChanged(nameof(IsLegalPage));
                OnPropertyChanged(nameof(IsSettingsNavActive));
                OnPropertyChanged(nameof(TitleLabel));
            }
        }
    }

    public bool IsHubPage => CurrentPage == AppPage.Hub;

    public bool IsNotHubPage => CurrentPage != AppPage.Hub;

    public bool IsActusPage => CurrentPage == AppPage.Actus;

    public bool IsServerPage => CurrentPage == AppPage.Server;

    public bool IsSettingsPage => CurrentPage == AppPage.Settings;

    public bool IsLogsPage => CurrentPage == AppPage.Logs;

    public bool IsLegalPage => CurrentPage == AppPage.Legal;

    /// <summary>Paramètres, journaux et mentions légales dépendent tous de l'item PARAMÈTRES.</summary>
    public bool IsSettingsNavActive => CurrentPage is AppPage.Settings or AppPage.Logs or AppPage.Legal;

    public string TitleLabel => "OMNÉRIA GAMES LAUNCHER · " + CurrentPage switch
    {
        AppPage.Actus => "ACTUS",
        AppPage.Server => "SERVEUR",
        AppPage.Settings => "PARAMÈTRES",
        AppPage.Logs => "JOURNAUX",
        AppPage.Legal => "MENTIONS LÉGALES",
        _ => "HUB",
    };

    /// <summary>Pastille « MISE À JOUR x.y.z » de la barre de titre.</summary>
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set => SetProperty(ref _updateAvailable, value);
    }

    public string UpdateText
    {
        get => _updateText;
        private set => SetProperty(ref _updateText, value);
    }

    public bool UpdateEnabled
    {
        get => _updateEnabled;
        set => SetProperty(ref _updateEnabled, value);
    }

    public void ShowUpdate(string version)
    {
        UpdateText = $"MISE À JOUR {version}";
        UpdateAvailable = true;
    }
}
