using MinecraftLauncherPerso.Services.Formatting;
using MinecraftLauncherPerso.Services.Status;

namespace MinecraftLauncherPerso.ViewModels;

/// <summary>
/// Carte héros du hub : nom du serveur, pastille d'état, joueurs, latence ou cause de la panne,
/// compatibilité de version et message du jour. Tout le calcul d'affichage est ici (testé sans fenêtre) ;
/// la fenêtre n'a plus qu'à appeler <see cref="Apply"/> avec le dernier ping.
/// </summary>
public sealed class HeroViewModel : ObservableObject
{
    private string _serverName = "";
    private StatusKind _statusKind = StatusKind.Neutral;
    private string _statusText = "Vérification...";
    private string? _statusTooltip;
    private bool _showPlayers;
    private string _playersOnline = "";
    private string _playersMax = "";
    private string _contactText = "";
    private bool _showContact;
    private string _failureText = "";
    private bool _showFailure;
    private string _compatText = "";
    private bool _showCompat;
    private bool _compatIsWarning;
    private string _motd = "";

    public string ServerName
    {
        get => _serverName;
        set => SetProperty(ref _serverName, value);
    }

    public StatusKind StatusKind
    {
        get => _statusKind;
        private set => SetProperty(ref _statusKind, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? StatusTooltip
    {
        get => _statusTooltip;
        private set => SetProperty(ref _statusTooltip, value);
    }

    public bool ShowPlayers
    {
        get => _showPlayers;
        private set => SetProperty(ref _showPlayers, value);
    }

    /// <summary>« 3 » (en gras) puis <see cref="PlayersMax"/> (« /12 joueurs »).</summary>
    public string PlayersOnline
    {
        get => _playersOnline;
        private set => SetProperty(ref _playersOnline, value);
    }

    public string PlayersMax
    {
        get => _playersMax;
        private set => SetProperty(ref _playersMax, value);
    }

    /// <summary>Hors ligne : « dernier contact il y a 12 min ».</summary>
    public string ContactText
    {
        get => _contactText;
        private set => SetProperty(ref _contactText, value);
    }

    public bool ShowContact
    {
        get => _showContact;
        private set => SetProperty(ref _showContact, value);
    }

    /// <summary>Hors ligne : « timeout 8 s » ou « injoignable ». (En ligne, la latence est dans la pastille.)</summary>
    public string FailureText
    {
        get => _failureText;
        private set => SetProperty(ref _failureText, value);
    }

    public bool ShowFailure
    {
        get => _showFailure;
        private set => SetProperty(ref _showFailure, value);
    }

    /// <summary>« 1.20.1 · compatible » ou « 1.21 · version différente ».</summary>
    public string CompatText
    {
        get => _compatText;
        private set => SetProperty(ref _compatText, value);
    }

    public bool ShowCompat
    {
        get => _showCompat;
        private set => SetProperty(ref _showCompat, value);
    }

    public bool CompatIsWarning
    {
        get => _compatIsWarning;
        private set => SetProperty(ref _compatIsWarning, value);
    }

    /// <summary>« Bienvenue sur le serveur » (avec guillemets), vide si le serveur n'en annonce pas.</summary>
    public string Motd
    {
        get => _motd;
        private set
        {
            if (SetProperty(ref _motd, value))
            {
                OnPropertyChanged(nameof(HasMotd));
            }
        }
    }

    public bool HasMotd => Motd.Length > 0;

    /// <summary>Aucun serveur configuré.</summary>
    public void NotConfigured()
    {
        StatusKind = StatusKind.Neutral;
        StatusText = "Non configuré";
        StatusTooltip = null;
        ShowPlayers = false;
        ShowContact = false;
        ShowFailure = false;
        ShowCompat = false;
        Motd = "";
    }

    /// <summary>
    /// Applique le dernier ping. <paramref name="lastContact"/> et <paramref name="lastMotd"/> servent
    /// hors ligne (dernier état connu).
    /// </summary>
    public void Apply(ServerStatus status, string minecraftVersion, DateTimeOffset? lastContact, string? lastMotd, DateTimeOffset now)
    {
        var presentation = ServerStatusPresentation.From(status, minecraftVersion);
        StatusKind = presentation.Kind;
        StatusText = presentation.Text;
        StatusTooltip = presentation.Tooltip;

        if (status.IsOnline)
        {
            ShowPlayers = true;
            PlayersOnline = status.OnlinePlayers.ToString();
            PlayersMax = $"/{status.MaxPlayers} joueurs";
            ShowContact = false;
            ShowFailure = false;
            ShowCompat = presentation.Compatibility is not null;
            CompatText = presentation.Compatibility ?? "";
            CompatIsWarning = presentation.Kind == StatusKind.Warning;
        }
        else
        {
            ShowPlayers = false;
            ShowCompat = false;
            ShowContact = true;
            ContactText = lastContact is { } at ? $"dernier contact {RelativeTime.Format(at, now)}" : "aucun contact récent";
            ShowFailure = true;
            FailureText = status.ErrorDetail?.StartsWith("Timeout", StringComparison.OrdinalIgnoreCase) == true ? "timeout 8 s" : "injoignable";
        }

        var motd = status.IsOnline ? status.Motd : lastMotd;
        Motd = string.IsNullOrWhiteSpace(motd) ? "" : $"« {motd} »";
    }
}
