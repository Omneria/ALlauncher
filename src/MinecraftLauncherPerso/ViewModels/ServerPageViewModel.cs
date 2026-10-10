using System.Collections.ObjectModel;
using System.Windows.Media;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Status;

namespace MinecraftLauncherPerso.ViewModels;

/// <summary>Allure d'une barre de disponibilité : la couleur se déduit de ce type dans le XAML.</summary>
public enum BarKind
{
    NoData,
    Up,
    Partial,
    Down,
}

public enum NotificationKind
{
    ServerBack,
    Friend,
}

/// <summary>Changement d'une case de notification de la page SERVEUR.</summary>
public sealed record NotificationChange(NotificationKind Kind, bool Enabled);

/// <summary>Une barre de la courbe 24 h : hauteur proportionnelle à la disponibilité (4 px mini).</summary>
public sealed record AvailabilityBar(BarKind Kind, double BarHeight, string Tooltip);

/// <summary>Une ligne de la liste des joueurs connectés ; la tête arrive après coup.</summary>
public sealed class PlayerRowViewModel(string name, string duration) : ObservableObject
{
    private ImageSource? _avatar;

    public string Name { get; } = name;

    public string Duration { get; } = duration;

    public ImageSource? Avatar
    {
        get => _avatar;
        set => SetProperty(ref _avatar, value);
    }
}

/// <summary>
/// Page SERVEUR : état du moment, disponibilité des dernières 24 h, message du jour, joueurs connectés.
/// L'historique vient du VPS ; sans lui, la page se limite à l'état du moment et aux pseudos annoncés
/// par le serveur. Calculs testés sans fenêtre (ServerPageViewModelTests).
/// </summary>
public sealed class ServerPageViewModel : ObservableObject
{
    /// <summary>Hauteur d'une barre : 4 px (coupure) à 30 px (toujours en ligne).</summary>
    public const double MinBarHeight = 4;
    public const double MaxBarHeight = 30;

    private string _addressText = "";
    private StatusKind _statusKind = StatusKind.Neutral;
    private string _statusText = "INCONNU";
    private string? _statusHint;
    private string _playersOnline = "";
    private string _playersMax = "";
    private string _latency = "—";
    private string _version = "—";
    private string _protocol = "—";
    private bool _protocolOk;
    private string _outageText = "";
    private bool _historyUnavailable = true;
    private string _motd = "Pas de message du jour.";
    private string _playersListCount = "";
    private bool _notifyServerBack;
    private bool _notifyFriend;

    public string AddressText
    {
        get => _addressText;
        private set => SetProperty(ref _addressText, value);
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

    public string? StatusHint
    {
        get => _statusHint;
        private set => SetProperty(ref _statusHint, value);
    }

    /// <summary>« 3 » (grand, cyan) puis <see cref="PlayersMax"/> (« /12 »).</summary>
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

    public string Latency
    {
        get => _latency;
        private set => SetProperty(ref _latency, value);
    }

    public string Version
    {
        get => _version;
        private set => SetProperty(ref _version, value);
    }

    /// <summary>« 763 ✓ » quand le protocole est celui que le launcher installe.</summary>
    public string Protocol
    {
        get => _protocol;
        private set => SetProperty(ref _protocol, value);
    }

    public bool ProtocolOk
    {
        get => _protocolOk;
        private set => SetProperty(ref _protocolOk, value);
    }

    public ObservableCollection<AvailabilityBar> Bars { get; } = [];

    /// <summary>« coupure 04:10 → 04:22 », vide s'il n'y en a pas eu.</summary>
    public string OutageText
    {
        get => _outageText;
        private set => SetProperty(ref _outageText, value);
    }

    public bool HistoryUnavailable
    {
        get => _historyUnavailable;
        private set => SetProperty(ref _historyUnavailable, value);
    }

    public string Motd
    {
        get => _motd;
        private set => SetProperty(ref _motd, value);
    }

    public ObservableCollection<PlayerRowViewModel> Players { get; } = [];

    public string PlayersListCount
    {
        get => _playersListCount;
        private set => SetProperty(ref _playersListCount, value);
    }

    public bool NoPlayers => Players.Count == 0;

    /// <summary>Case « prévenir quand le serveur revient en ligne » (liaison dans les deux sens).</summary>
    public bool NotifyServerBack
    {
        get => _notifyServerBack;
        set
        {
            if (SetProperty(ref _notifyServerBack, value) && !_loading)
            {
                NotificationChanged?.Invoke(this, new NotificationChange(NotificationKind.ServerBack, value));
            }
        }
    }

    /// <summary>Case « prévenir quand un ami se connecte » (liaison dans les deux sens).</summary>
    public bool NotifyFriend
    {
        get => _notifyFriend;
        set
        {
            if (SetProperty(ref _notifyFriend, value) && !_loading)
            {
                NotificationChanged?.Invoke(this, new NotificationChange(NotificationKind.Friend, value));
            }
        }
    }

    /// <summary>Le joueur a coché ou décoché une notification (pas déclenché par <see cref="Update"/>).</summary>
    public event EventHandler<NotificationChange>? NotificationChanged;

    private bool _loading;

    public void Update(LauncherSettings settings, ServerStatus? status, ServerHistory? history, DateTimeOffset now)
    {
        _loading = true;
        NotifyServerBack = settings.DesktopNotificationsEnabled;
        NotifyFriend = settings.NotifyFriendConnected;
        _loading = false;

        var name = string.IsNullOrWhiteSpace(settings.ServerName) ? "SERVEUR" : settings.ServerName.ToUpperInvariant();
        AddressText = $"{name} · {settings.ServerHost}";

        ShowStatus(settings, status);
        ShowAvailability(history, now);
        ShowPlayers(status, history, now);
    }

    private void ShowStatus(LauncherSettings settings, ServerStatus? status)
    {
        if (status is null)
        {
            StatusKind = StatusKind.Neutral;
            StatusText = "INCONNU";
            StatusHint = null;
            PlayersOnline = "";
            PlayersMax = "";
            Latency = Version = Protocol = "—";
            ProtocolOk = false;
            Motd = "Pas de message du jour.";
            return;
        }

        var presentation = ServerStatusPresentation.From(status, settings.MinecraftVersion);
        StatusKind = presentation.Kind;
        StatusText = status.IsOnline ? "EN LIGNE" : "HORS LIGNE";
        StatusHint = presentation.Tooltip;

        PlayersOnline = status.IsOnline ? status.OnlinePlayers.ToString() : "";
        PlayersMax = status.IsOnline ? $"/{status.MaxPlayers}" : "";
        Latency = status.IsOnline && status.LatencyMs is { } latency ? $"{latency} ms" : "—";
        Version = status.IsOnline ? status.VersionName ?? settings.MinecraftVersion : "—";

        var expected = ServerStatusService.ExpectedProtocol(settings.MinecraftVersion);
        ProtocolOk = status.IsOnline && status.ProtocolVersion is { } protocol && expected == protocol;
        Protocol = status.IsOnline && status.ProtocolVersion is { } value ? $"{value}{(ProtocolOk ? " ✓" : "")}" : "—";

        Motd = status.IsOnline && !string.IsNullOrWhiteSpace(status.Motd) ? status.Motd : "Pas de message du jour.";
    }

    private void ShowAvailability(ServerHistory? history, DateTimeOffset now)
    {
        Bars.Clear();
        if (history is null || history.Samples.Count == 0)
        {
            OutageText = "";
            HistoryUnavailable = true;
            return;
        }

        HistoryUnavailable = false;

        // 48 barres d'une demi-heure.
        foreach (var bucket in ServerHistoryAnalyzer.Hourly(history, now, hours: 24, bucketsPerHour: 2))
        {
            var kind = bucket.UptimeRatio switch
            {
                null => BarKind.NoData,
                >= 0.99 => BarKind.Up,
                > 0 => BarKind.Partial,
                _ => BarKind.Down,
            };
            var tooltip = bucket.UptimeRatio is { } ratio
                ? $"{bucket.Start.ToLocalTime():dd/MM HH:mm} · {ratio * 100:0} % disponible"
                : $"{bucket.Start.ToLocalTime():dd/MM HH:mm} · pas de données";
            Bars.Add(new AvailabilityBar(kind, MinBarHeight + ((MaxBarHeight - MinBarHeight) * (bucket.UptimeRatio ?? 0)), tooltip));
        }

        var outage = ServerHistoryAnalyzer.LastOutage(history, now);
        OutageText = outage switch
        {
            null => "",
            { End: { } end } => $"coupure {outage.Start.ToLocalTime():HH:mm} → {end.ToLocalTime():HH:mm}",
            _ => $"coupure depuis {outage.Start.ToLocalTime():HH:mm}",
        };
    }

    private void ShowPlayers(ServerStatus? status, ServerHistory? history, DateTimeOffset now)
    {
        Players.Clear();
        if (history is { Samples.Count: > 0 })
        {
            foreach (var presence in ServerHistoryAnalyzer.Presence(history, now).Where(presence => presence.IsOnline))
            {
                Players.Add(new PlayerRowViewModel(presence.Name, ServerHistoryAnalyzer.FormatDuration(now - presence.SessionStart)));
            }
        }

        if (Players.Count == 0 && status is { IsOnline: true, PlayerNames: { Count: > 0 } names })
        {
            foreach (var name in names)
            {
                Players.Add(new PlayerRowViewModel(name, ""));
            }
        }

        PlayersListCount = status is { IsOnline: true } ? status.OnlinePlayers.ToString() : "";
        OnPropertyChanged(nameof(NoPlayers));
    }
}
