using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Status;

namespace MinecraftLauncherPerso.Controls;

/// <summary>
/// Page SERVEUR (maquette « SERVEUR — page dédiée ») : état du moment, disponibilité des dernières
/// 24 h, message du jour, joueurs connectés. L'historique vient du VPS (<see cref="ServerHistory"/>) ;
/// sans lui, la page se limite à l'état du moment et aux pseudos annoncés par le serveur.
/// </summary>
public partial class ServerPage : UserControl
{
    /// <summary>Hauteur d'une barre de disponibilité : 4 px (coupure) à 30 px (toujours en ligne).</summary>
    private const double MinBarHeight = 4;
    private const double MaxBarHeight = 30;

    private bool _updating;

    public ServerPage()
    {
        InitializeComponent();
    }

    /// <summary>Case « serveur de nouveau en ligne » cochée ou décochée.</summary>
    public event EventHandler<bool>? NotificationsToggled;

    /// <summary>Case « un ami se connecte » cochée ou décochée.</summary>
    public event EventHandler<bool>? FriendNotificationsToggled;

    /// <summary>Clic sur JOUER : le hub prend le relais pour le lancement.</summary>
    public event EventHandler? PlayRequested;

    private sealed record AvailabilityBar(Brush Brush, double BarHeight, string Tooltip);

    private sealed record PlayerRow(string Name, string Duration);

    public void Update(LauncherSettings settings, ServerStatus? status, ServerHistory? history, DateTimeOffset now)
    {
        _updating = true;
        NotifyCheckBox.IsChecked = settings.DesktopNotificationsEnabled;
        FriendCheckBox.IsChecked = settings.NotifyFriendConnected;
        _updating = false;

        var name = string.IsNullOrWhiteSpace(settings.ServerName) ? "SERVEUR" : settings.ServerName.ToUpperInvariant();
        AddressText.Text = $"{name} · {settings.ServerHost}";

        ShowStatus(settings, status);
        ShowAvailability(history, now);
        ShowPlayers(status, history, now);
    }

    private void ShowStatus(LauncherSettings settings, ServerStatus? status)
    {
        if (status is null)
        {
            StatusPill.Set(StatusKind.Neutral, "INCONNU");
            PlayersCountText.Text = "";
            LatencyText.Inlines.Clear();
            VersionText.Inlines.Clear();
            ProtocolText.Inlines.Clear();
            MotdText.Text = "Pas de message du jour.";
            return;
        }

        var presentation = ServerStatusPresentation.From(status, settings.MinecraftVersion);
        StatusPill.Set(presentation.Kind, status.IsOnline ? "EN LIGNE" : "HORS LIGNE", presentation.Tooltip);

        PlayersCountText.Inlines.Clear();
        if (status.IsOnline)
        {
            PlayersCountText.Inlines.Add(new System.Windows.Documents.Run(status.OnlinePlayers.ToString()));
            PlayersCountText.Inlines.Add(new System.Windows.Documents.Run($"/{status.MaxPlayers}")
            {
                FontSize = 16,
                Foreground = Res("InkDimBrush"),
            });
        }

        Meta(LatencyText, "LATENCE ", status.IsOnline && status.LatencyMs is { } latency ? $"{latency} ms" : "—", null);
        Meta(VersionText, "VERSION ", status.IsOnline ? status.VersionName ?? settings.MinecraftVersion : "—", null);

        var expected = ServerStatusService.ExpectedProtocol(settings.MinecraftVersion);
        var protocolOk = status.ProtocolVersion is { } protocol && expected == protocol;
        Meta(
            ProtocolText,
            "PROTOCOLE ",
            status.IsOnline && status.ProtocolVersion is { } value ? $"{value}{(protocolOk ? " ✓" : "")}" : "—",
            protocolOk ? Res("CyanBrush") : null);

        MotdText.Text = status.IsOnline && !string.IsNullOrWhiteSpace(status.Motd) ? status.Motd : "Pas de message du jour.";
    }

    /// <summary>« LATENCE <b>24 ms</b> » : libellé atténué, valeur en gras (couleur de l'encre ou d'accent).</summary>
    private void Meta(TextBlock target, string label, string value, Brush? accent)
    {
        target.Inlines.Clear();
        target.Inlines.Add(new System.Windows.Documents.Run(label));
        target.Inlines.Add(new System.Windows.Documents.Run(value)
        {
            FontWeight = FontWeights.Bold,
            Foreground = accent ?? Res("InkBrush"),
        });
    }

    private void ShowAvailability(ServerHistory? history, DateTimeOffset now)
    {
        if (history is null || history.Samples.Count == 0)
        {
            AvailabilityBars.ItemsSource = null;
            OutageText.Text = "";
            HistoryUnavailableText.Visibility = Visibility.Visible;
            return;
        }

        HistoryUnavailableText.Visibility = Visibility.Collapsed;

        // 48 barres d'une demi-heure : hauteur proportionnelle à la disponibilité, cyan si complète,
        // ambre si partielle, magenta et plate (4 px) pour une coupure franche.
        AvailabilityBars.ItemsSource = ServerHistoryAnalyzer.Hourly(history, now, hours: 24, bucketsPerHour: 2)
            .Select(bucket => new AvailabilityBar(
                bucket.UptimeRatio switch
                {
                    null => Res("Surface2Brush"),
                    >= 0.99 => Res("CyanBrush"),
                    > 0 => Res("AmberBrush"),
                    _ => Res("MagentaBrush"),
                },
                MinBarHeight + ((MaxBarHeight - MinBarHeight) * (bucket.UptimeRatio ?? 0)),
                bucket.UptimeRatio is { } ratio
                    ? $"{bucket.Start.ToLocalTime():dd/MM HH:mm} · {ratio * 100:0} % disponible"
                    : $"{bucket.Start.ToLocalTime():dd/MM HH:mm} · pas de données"))
            .ToList();

        var outage = ServerHistoryAnalyzer.LastOutage(history, now);
        OutageText.Text = outage switch
        {
            null => "",
            { End: { } end } => $"coupure {outage.Start.ToLocalTime():HH:mm} → {end.ToLocalTime():HH:mm}",
            _ => $"coupure depuis {outage.Start.ToLocalTime():HH:mm}",
        };
    }

    private void ShowPlayers(ServerStatus? status, ServerHistory? history, DateTimeOffset now)
    {
        var rows = new List<PlayerRow>();
        if (history is { Samples.Count: > 0 })
        {
            rows.AddRange(ServerHistoryAnalyzer.Presence(history, now)
                .Where(presence => presence.IsOnline)
                .Select(presence => new PlayerRow(presence.Name, ServerHistoryAnalyzer.FormatDuration(now - presence.SessionStart))));
        }

        if (rows.Count == 0 && status is { IsOnline: true, PlayerNames: { Count: > 0 } names })
        {
            rows.AddRange(names.Select(name => new PlayerRow(name, "")));
        }

        PlayersList.ItemsSource = rows;
        PlayersListCount.Text = status is { IsOnline: true } ? status.OnlinePlayers.ToString() : "";
        NoPlayersText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private void NotifyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_updating)
        {
            NotificationsToggled?.Invoke(this, NotifyCheckBox.IsChecked == true);
        }
    }

    private void FriendCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_updating)
        {
            FriendNotificationsToggled?.Invoke(this, FriendCheckBox.IsChecked == true);
        }
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e) => PlayRequested?.Invoke(this, EventArgs.Empty);
}
