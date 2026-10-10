using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Status;

namespace MinecraftLauncherPerso.Controls;

/// <summary>
/// Page SERVEUR (v2.0.0) : état du moment, disponibilité des dernières 24 h et joueurs vus
/// récemment. L'historique vient du VPS (<see cref="ServerHistory"/>) ; sans lui, la page se limite
/// à l'état du moment et aux pseudos annoncés par le serveur.
/// </summary>
public partial class ServerPage : UserControl
{
    private bool _updatingCheckBox;

    public ServerPage()
    {
        InitializeComponent();
    }

    /// <summary>L'utilisateur a coché ou décoché la notification « serveur de nouveau en ligne ».</summary>
    public event EventHandler<bool>? NotificationsToggled;

    private sealed record AvailabilityBar(Brush Brush, string Tooltip);

    private sealed record PlayerRow(string Name, string Detail, Brush DotBrush);

    public void Update(LauncherSettings settings, ServerStatus? status, ServerHistory? history, DateTimeOffset now)
    {
        TitleText.Text = string.IsNullOrWhiteSpace(settings.ServerName) ? "SERVEUR" : $"SERVEUR · {settings.ServerName.ToUpperInvariant()}";

        _updatingCheckBox = true;
        NotifyCheckBox.IsChecked = settings.DesktopNotificationsEnabled;
        _updatingCheckBox = false;

        ShowStatus(settings, status);
        ShowAvailability(history, now);
        ShowPlayers(status, history, now);
    }

    private void ShowStatus(LauncherSettings settings, ServerStatus? status)
    {
        var details = new List<string> { $"{settings.ServerHost}:{settings.ServerPort}" };

        if (status is null)
        {
            StatusDot.Fill = Res("InkDimBrush");
            StatusText.Text = "Vérification...";
            MotdText.Visibility = Visibility.Collapsed;
            PlayersCountText.Text = "—";
            DetailsText.Text = string.Join("  ·  ", details);
            return;
        }

        StatusDot.Fill = Res(status.IsOnline ? "CyanBrush" : "MagentaBrush");
        StatusText.Text = !status.IsOnline ? "HORS LIGNE" : status.LatencyMs is { } latency ? $"EN LIGNE · {latency} ms" : "EN LIGNE";
        MotdText.Text = status.IsOnline ? status.Motd ?? "" : "";
        MotdText.Visibility = string.IsNullOrEmpty(MotdText.Text) ? Visibility.Collapsed : Visibility.Visible;
        PlayersCountText.Text = status.IsOnline ? $"{status.OnlinePlayers}/{status.MaxPlayers}" : "—";

        if (status.IsOnline)
        {
            if (status.VersionName is { Length: > 0 } version)
            {
                details.Add(version);
            }

            if (status.ProtocolVersion is { } protocol)
            {
                details.Add($"protocole {protocol}");
            }
        }
        else if (!string.IsNullOrEmpty(status.ErrorDetail))
        {
            details.Add(status.ErrorDetail);
        }

        DetailsText.Text = string.Join("  ·  ", details);
    }

    private void ShowAvailability(ServerHistory? history, DateTimeOffset now)
    {
        if (history is null || history.Samples.Count == 0)
        {
            AvailabilityBars.ItemsSource = null;
            UptimeText.Text = "—";
            HistoryUnavailableText.Visibility = Visibility.Visible;
            return;
        }

        HistoryUnavailableText.Visibility = Visibility.Collapsed;
        var uptime = ServerHistoryAnalyzer.UptimeRatio(history, now);
        UptimeText.Text = uptime is { } ratio ? $"{ratio * 100:0.#} %" : "—";

        AvailabilityBars.ItemsSource = ServerHistoryAnalyzer.Hourly(history, now)
            .Select(bucket => new AvailabilityBar(
                bucket.UptimeRatio switch
                {
                    null => Res("Surface2Brush"),
                    >= 0.99 => Res("CyanBrush"),
                    > 0 => Res("AmberBrush"),
                    _ => Res("MagentaBrush"),
                },
                bucket.UptimeRatio is { } r
                    ? $"{bucket.Start.ToLocalTime():dd/MM HH:mm} · {r * 100:0} % disponible"
                    : $"{bucket.Start.ToLocalTime():dd/MM HH:mm} · pas de données"))
            .ToList();
    }

    private void ShowPlayers(ServerStatus? status, ServerHistory? history, DateTimeOffset now)
    {
        var rows = new List<PlayerRow>();
        if (history is { Samples.Count: > 0 })
        {
            foreach (var presence in ServerHistoryAnalyzer.Presence(history, now))
            {
                rows.Add(presence.IsOnline
                    ? new PlayerRow(presence.Name, $"connecté depuis {ServerHistoryAnalyzer.FormatDuration(now - presence.SessionStart)}", Res("CyanBrush"))
                    : new PlayerRow(presence.Name, $"vu il y a {ServerHistoryAnalyzer.FormatDuration(now - presence.LastSeen)}", Res("InkDimBrush")));
            }
        }
        else if (status is { IsOnline: true, PlayerNames: { Count: > 0 } names })
        {
            rows.AddRange(names.Select(name => new PlayerRow(name, "connecté", Res("CyanBrush"))));
        }

        PlayersList.ItemsSource = rows;
        NoPlayersText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private void NotifyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_updatingCheckBox)
        {
            NotificationsToggled?.Invoke(this, NotifyCheckBox.IsChecked == true);
        }
    }
}
