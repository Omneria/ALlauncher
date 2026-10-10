using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Status;

namespace MinecraftLauncherPerso.ViewModels;

/// <summary>
/// Champs de la page Paramètres (hors slider de RAM, qui reste une affaire de la vue) : saisie de la
/// résolution, dossier de jeu, cases à cocher, amis. Valide la saisie et l'applique aux
/// <see cref="LauncherSettings"/> ; testé sans fenêtre.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private string _screenWidthText = "0";
    private string _screenHeightText = "0";
    private string _gameDirectory = "";
    private bool _desktopNotifications;
    private bool _minimizeWhileGame;
    private bool _sounds;
    private string _friendsText = "";
    private bool _widthInvalid;
    private bool _heightInvalid;
    private string _validationMessage = "";

    public string ScreenWidthText
    {
        get => _screenWidthText;
        set => SetProperty(ref _screenWidthText, value);
    }

    public string ScreenHeightText
    {
        get => _screenHeightText;
        set => SetProperty(ref _screenHeightText, value);
    }

    public string GameDirectory
    {
        get => _gameDirectory;
        set => SetProperty(ref _gameDirectory, value);
    }

    public bool DesktopNotifications
    {
        get => _desktopNotifications;
        set => SetProperty(ref _desktopNotifications, value);
    }

    public bool MinimizeWhileGame
    {
        get => _minimizeWhileGame;
        set => SetProperty(ref _minimizeWhileGame, value);
    }

    public bool Sounds
    {
        get => _sounds;
        set => SetProperty(ref _sounds, value);
    }

    /// <summary>Pseudos des amis, séparés par des virgules.</summary>
    public string FriendsText
    {
        get => _friendsText;
        set => SetProperty(ref _friendsText, value);
    }

    public bool WidthInvalid
    {
        get => _widthInvalid;
        private set => SetProperty(ref _widthInvalid, value);
    }

    public bool HeightInvalid
    {
        get => _heightInvalid;
        private set => SetProperty(ref _heightInvalid, value);
    }

    /// <summary>Pourquoi rien n'est enregistré (vide si la saisie est valide).</summary>
    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationError));
            }
        }
    }

    public bool HasValidationError => ValidationMessage.Length > 0;

    /// <summary>Charge les valeurs courantes (et efface les erreurs d'une ouverture précédente).</summary>
    public void Load(LauncherSettings settings)
    {
        ScreenWidthText = settings.ScreenWidth.ToString();
        ScreenHeightText = settings.ScreenHeight.ToString();
        GameDirectory = settings.GameDirectory;
        DesktopNotifications = settings.DesktopNotificationsEnabled;
        MinimizeWhileGame = settings.MinimizeWhileGameRuns;
        Sounds = settings.SoundsEnabled;
        FriendsText = string.Join(", ", settings.FriendNames);
        WidthInvalid = false;
        HeightInvalid = false;
        ValidationMessage = "";
    }

    /// <summary>
    /// Valide la saisie puis l'applique à <paramref name="settings"/> avec la plage de RAM choisie au
    /// slider. Renvoie faux (et renseigne <see cref="ValidationMessage"/>) sans rien modifier si une
    /// valeur est invalide.
    /// </summary>
    public bool TryApply(LauncherSettings settings, int minRamMb, int maxRamMb)
    {
        var widthValid = int.TryParse(ScreenWidthText, out var width) && width >= 0;
        var heightValid = int.TryParse(ScreenHeightText, out var height) && height >= 0;
        WidthInvalid = !widthValid;
        HeightInvalid = !heightValid;

        var errors = new List<string>();
        if (!widthValid)
        {
            errors.Add("Largeur : entier positif ou 0 (automatique) attendu.");
        }

        if (!heightValid)
        {
            errors.Add("Hauteur : entier positif ou 0 (automatique) attendu.");
        }

        ValidationMessage = string.Join(" ", errors);
        if (errors.Count > 0)
        {
            return false;
        }

        settings.MinRamMb = minRamMb;
        settings.MaxRamMb = maxRamMb;
        settings.ScreenWidth = width;
        settings.ScreenHeight = height;

        if (!string.IsNullOrWhiteSpace(GameDirectory))
        {
            settings.GameDirectory = GameDirectory;
        }

        settings.DesktopNotificationsEnabled = DesktopNotifications;
        settings.MinimizeWhileGameRuns = MinimizeWhileGame;
        settings.SoundsEnabled = Sounds;
        settings.FriendNames = FriendWatcher.ParseNames(FriendsText);
        return true;
    }
}
