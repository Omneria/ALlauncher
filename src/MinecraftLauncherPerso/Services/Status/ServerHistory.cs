using System.Text.Json.Serialization;

namespace MinecraftLauncherPerso.Services.Status;

/// <summary>
/// Historique du serveur publié par le VPS (v2.0.0, server-history.json, écrit par
/// record-server-history.py) : un échantillon par minute sur les dernières 24 h. Le launcher ne
/// peut pas le reconstituer seul (il ne tourne pas en permanence), d'où ce fichier côté VPS.
/// </summary>
public sealed class ServerHistory
{
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("samples")]
    public List<ServerHistorySample> Samples { get; set; } = [];
}

public sealed class ServerHistorySample
{
    [JsonPropertyName("t")]
    public DateTimeOffset Time { get; set; }

    [JsonPropertyName("online")]
    public bool Online { get; set; }

    [JsonPropertyName("players")]
    public int Players { get; set; }

    [JsonPropertyName("names")]
    public List<string> Names { get; set; } = [];
}

/// <summary>Disponibilité sur une heure ; <see cref="UptimeRatio"/> est null s'il n'y a aucun échantillon.</summary>
public sealed record AvailabilityBucket(DateTimeOffset Start, double? UptimeRatio);

/// <summary>Un joueur vu dans l'historique : en ligne depuis <see cref="SessionStart"/>, ou vu pour la dernière fois à <see cref="LastSeen"/>.</summary>
public sealed record PlayerPresence(string Name, bool IsOnline, DateTimeOffset SessionStart, DateTimeOffset LastSeen);

/// <summary>Calculs purs sur <see cref="ServerHistory"/>, séparés de l'interface pour être testés.</summary>
public static class ServerHistoryAnalyzer
{
    /// <summary>Écart maximal entre deux échantillons pour qu'une présence soit considérée continue.</summary>
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(3);

    /// <summary>Au-delà, l'historique est périmé (le script du VPS ne tourne plus) : personne n'est « en ligne ».</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    /// <summary>Une case par heure, de la plus ancienne à la plus récente (la dernière se termine à <paramref name="now"/>).</summary>
    public static IReadOnlyList<AvailabilityBucket> Hourly(ServerHistory history, DateTimeOffset now, int hours = 24)
    {
        var buckets = new List<AvailabilityBucket>(hours);
        for (var i = hours - 1; i >= 0; i--)
        {
            var end = now - TimeSpan.FromHours(i);
            var start = end - TimeSpan.FromHours(1);
            var inside = history.Samples.Where(s => s.Time > start && s.Time <= end).ToList();
            buckets.Add(new AvailabilityBucket(start, inside.Count == 0 ? null : inside.Count(s => s.Online) / (double)inside.Count));
        }

        return buckets;
    }

    /// <summary>Part des échantillons en ligne sur la période, ou null sans aucun échantillon.</summary>
    public static double? UptimeRatio(ServerHistory history, DateTimeOffset now, int hours = 24)
    {
        var since = now - TimeSpan.FromHours(hours);
        var inside = history.Samples.Where(s => s.Time > since && s.Time <= now).ToList();
        return inside.Count == 0 ? null : inside.Count(s => s.Online) / (double)inside.Count;
    }

    /// <summary>Joueurs en ligne d'abord (session la plus longue en tête), puis les autres du plus récent au plus ancien.</summary>
    public static IReadOnlyList<PlayerPresence> Presence(ServerHistory history, DateTimeOffset now)
    {
        var ordered = history.Samples.OrderBy(s => s.Time).ToList();
        if (ordered.Count == 0)
        {
            return [];
        }

        var last = ordered[^1];
        var fresh = now - last.Time <= StaleAfter && last.Online;
        var sessions = new Dictionary<string, (DateTimeOffset Start, DateTimeOffset Seen)>(StringComparer.OrdinalIgnoreCase);
        ServerHistorySample? previous = null;

        foreach (var sample in ordered)
        {
            var continuous = previous is not null && sample.Time - previous.Time <= MaxGap && sample.Online && previous.Online;
            foreach (var name in sample.Names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var wasThere = continuous && previous!.Names.Contains(name, StringComparer.OrdinalIgnoreCase);
                var start = wasThere && sessions.TryGetValue(name, out var known) ? known.Start : sample.Time;
                sessions[name] = (start, sample.Time);
            }

            previous = sample;
        }

        return sessions
            .Select(pair => new PlayerPresence(
                pair.Key,
                fresh && pair.Value.Seen == last.Time,
                pair.Value.Start,
                pair.Value.Seen))
            .OrderByDescending(p => p.IsOnline)
            .ThenBy(p => p.IsOnline ? p.SessionStart : DateTimeOffset.MaxValue)
            .ThenByDescending(p => p.LastSeen)
            .ToList();
    }

    /// <summary>Durée lisible et courte : « 12 min », « 1 h 05 », « 3 h », « 2 j ».</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1))
        {
            return "moins d'1 min";
        }

        if (duration < TimeSpan.FromHours(1))
        {
            return $"{(int)duration.TotalMinutes} min";
        }

        if (duration < TimeSpan.FromHours(24))
        {
            return duration.Minutes == 0 ? $"{(int)duration.TotalHours} h" : $"{(int)duration.TotalHours} h {duration.Minutes:00}";
        }

        return $"{(int)duration.TotalDays} j";
    }
}
