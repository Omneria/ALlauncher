using System.Collections.ObjectModel;
using MinecraftLauncherPerso.Services.Changelog;
using MinecraftLauncherPerso.Services.Formatting;
using MinecraftLauncherPerso.Services.ModSync;
using MinecraftLauncherPerso.Services.News;
using MinecraftLauncherPerso.Services.Status;

namespace MinecraftLauncherPerso.ViewModels;

/// <summary>Une ligne de la tuile MODPACK : « + 2 ajoutés » (accent) suivi des noms.</summary>
public sealed record TileLine(string Prefix, string Names, TileLineKind Kind);

public enum TileLineKind
{
    Added,
    Removed,
    Updated,
    Plain,
}

/// <summary>
/// Les trois tuiles du hub (dernière actu, modpack, launcher). Chaque tuile se calcule à partir de ses
/// données brutes (news.txt, changelog.json, releases GitHub) ; testé sans fenêtre.
/// </summary>
public sealed class TilesViewModel : ObservableObject
{
    private string _newsDate = "";
    private string _newsTitle = "";
    private string _newsBody = "Aucune actualité pour le moment.";
    private string _modpackAge = "";
    private string _modpackTitle = "Modpack";
    private string _launcherPillText = "";
    private StatusKind _launcherPillKind = StatusKind.Neutral;
    private string _launcherBody = "Notes de version indisponibles pour le moment.";
    private string? _launcherUrl;

    public string NewsDate
    {
        get => _newsDate;
        private set => SetProperty(ref _newsDate, value);
    }

    public string NewsTitle
    {
        get => _newsTitle;
        private set
        {
            if (SetProperty(ref _newsTitle, value))
            {
                OnPropertyChanged(nameof(HasNewsTitle));
            }
        }
    }

    public bool HasNewsTitle => NewsTitle.Length > 0;

    public string NewsBody
    {
        get => _newsBody;
        private set => SetProperty(ref _newsBody, value);
    }

    public string ModpackAge
    {
        get => _modpackAge;
        private set => SetProperty(ref _modpackAge, value);
    }

    public string ModpackTitle
    {
        get => _modpackTitle;
        private set => SetProperty(ref _modpackTitle, value);
    }

    /// <summary>Lignes + / − / ↻ du dernier changement du pack ; vide s'il n'y a rien à montrer.</summary>
    public ObservableCollection<TileLine> ModpackLines { get; } = [];

    public bool HasModpackLines => ModpackLines.Count > 0;

    public string LauncherPillText
    {
        get => _launcherPillText;
        private set => SetProperty(ref _launcherPillText, value);
    }

    public StatusKind LauncherPillKind
    {
        get => _launcherPillKind;
        private set => SetProperty(ref _launcherPillKind, value);
    }

    public string LauncherBody
    {
        get => _launcherBody;
        private set => SetProperty(ref _launcherBody, value);
    }

    /// <summary>Page GitHub de la release montrée (lien « VOIR SUR GITHUB »), null tant qu'inconnue.</summary>
    public string? LauncherUrl
    {
        get => _launcherUrl;
        private set => SetProperty(ref _launcherUrl, value);
    }

    /// <summary>Dernière actu : la première ligne est le titre, le reste le texte.</summary>
    public void UpdateNews(IEnumerable<NewsHistoryEntry> history)
    {
        var latest = history.OrderByDescending(entry => entry.FetchedAt).FirstOrDefault();
        if (latest is null)
        {
            NewsDate = "";
            NewsTitle = "";
            NewsBody = "Aucune actualité pour le moment.";
            return;
        }

        var (title, body) = SplitNews(latest.Content);
        NewsDate = FormatDate(latest.FetchedAt);
        NewsTitle = title;
        NewsBody = body;
    }

    /// <summary>La première ligne d'une actu fait office de titre, le reste du texte. Une actu d'une
    /// seule ligne n'a pas de titre : tout est le texte.</summary>
    public static (string Title, string Body) SplitNews(string content)
    {
        var lines = content.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return lines.Length switch
        {
            0 => ("", ""),
            1 => ("", lines[0]),
            _ => (lines[0], string.Join(" ", lines.Skip(1))),
        };
    }

    public static string FormatDate(DateTimeOffset date) => date.ToLocalTime().ToString("dd/MM · HH:mm", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));

    /// <summary>Tuile MODPACK : « 48 mods · 1.20.1 », âge de la dernière mise à jour et son résumé.</summary>
    public void UpdateModpack(IEnumerable<ModpackChangelogEntry> changelog, int? modCount, string minecraftVersion, DateTimeOffset now)
    {
        var latest = changelog.OrderByDescending(entry => entry.Date).FirstOrDefault();
        ModpackTitle = modCount is { } count ? $"{count} mods · {minecraftVersion}" : $"Modpack · {minecraftVersion}";
        ModpackAge = latest is null ? "" : $"MIS À JOUR {RelativeTime.Format(latest.Date, now).ToUpperInvariant()}";

        ModpackLines.Clear();
        if (latest is not null)
        {
            var summary = ModpackChangeSummary.From(latest.Lines);
            if (summary.Added.Count > 0)
            {
                ModpackLines.Add(new TileLine($"+ {summary.Added.Count} ajouté{Plural(summary.Added.Count)}", string.Join(", ", summary.Added), TileLineKind.Added));
            }

            if (summary.Removed.Count > 0)
            {
                ModpackLines.Add(new TileLine($"− {summary.Removed.Count} retiré{Plural(summary.Removed.Count)}", string.Join(", ", summary.Removed), TileLineKind.Removed));
            }

            if (summary.Updated.Count > 0)
            {
                ModpackLines.Add(new TileLine($"↻ {summary.Updated.Count} mis à jour", string.Join(", ", summary.Updated), TileLineKind.Updated));
            }

            if (ModpackLines.Count == 0)
            {
                ModpackLines.Add(new TileLine("", string.Join(" ", latest.Lines), TileLineKind.Plain));
            }
        }

        OnPropertyChanged(nameof(HasModpackLines));
    }

    private static string Plural(int count) => count > 1 ? "s" : "";

    /// <summary>Tuile LAUNCHER : notes de la version installée (sinon de la dernière), pastille DERNIÈRE.</summary>
    public void UpdateLauncher(IReadOnlyList<ReleaseChangelogEntry> releases, string currentVersionDisplay)
    {
        var currentTag = "v" + currentVersionDisplay;
        var release = releases.FirstOrDefault(r => string.Equals(r.Tag, currentTag, StringComparison.OrdinalIgnoreCase))
            ?? releases.FirstOrDefault();
        if (release is null)
        {
            LauncherPillText = currentTag;
            LauncherPillKind = StatusKind.Neutral;
            return;
        }

        LauncherUrl = release.Url;
        var isCurrentLatest = release.IsLatest && string.Equals(release.Tag, currentTag, StringComparison.OrdinalIgnoreCase);
        LauncherPillKind = isCurrentLatest ? StatusKind.Online : StatusKind.Neutral;
        LauncherPillText = isCurrentLatest ? $"{currentTag} · DERNIÈRE" : currentTag;
        LauncherBody = release.Notes.Count == 0
            ? "Pas de notes pour cette version."
            : string.Join("\n", release.Notes.Select(note => "— " + note));
    }
}
