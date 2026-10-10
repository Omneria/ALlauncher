using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using MinecraftLauncherPerso.Controls;
using MinecraftLauncherPerso.Services.Changelog;
using MinecraftLauncherPerso.Services.ModSync;
using MinecraftLauncherPerso.Services.News;
using MinecraftLauncherPerso.Services.Status;
using MinecraftLauncherPerso.Services.Update;

namespace MinecraftLauncherPerso;

// Contenu du hub (v2.0.0) : les trois tuiles (dernière actu, modpack, launcher), la page ACTUS
// qui porte l'historique complet, et la ligne d'état sous le bouton JOUER.
public partial class MainWindow
{
    private const int MaxNewsItems = 20;

    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    private IReadOnlyList<ModpackChangelogEntry> _modpackChangelog = [];
    private IReadOnlyList<ReleaseChangelogEntry> _releases = [];
    private int? _modCount;
    private DateTimeOffset _modCountFetchedAt = DateTimeOffset.MinValue;
    private string? _tileReleaseUrl;
    private bool? _javaReady;

    /// <summary>
    /// Actus (news.txt, historique conservé localement : le VPS n'en garde aucun) et mises à jour du
    /// pack (changelog.json, v1.12.0). Mêlées dans une seule liste jusqu'à la 1.18.0, elles ont
    /// maintenant chacune leur tuile et leur section dans la page ACTUS.
    /// </summary>
    private async Task ShowNewsAsync()
    {
        var newsTask = _newsService.FetchNewsAsync(_settings.ModpackZipUrl);
        var changelogTask = _modSyncService.FetchChangelogAsync(_settings.ModpackManifestUrl);
        var news = await newsTask;
        _modpackChangelog = await changelogTask;
        if (news is not null)
        {
            _newsHistory = _newsHistoryStore.RecordIfNew(_newsHistory, news);
        }

        // Le manifest est plus lourd que les autres fichiers : relu toutes les 10 minutes seulement.
        if (DateTimeOffset.UtcNow - _modCountFetchedAt > TimeSpan.FromMinutes(10))
        {
            _modCount = await _modSyncService.FetchModCountAsync(_settings.ModpackManifestUrl) ?? _modCount;
            _modCountFetchedAt = DateTimeOffset.UtcNow;
        }

        ActusPageView.SetNews(_newsHistory
            .OrderByDescending(entry => entry.FetchedAt)
            .Take(MaxNewsItems)
            .Select(entry => new ActusEntry(FormatNewsDate(entry.FetchedAt), entry.Content))
            .ToList());
        ActusPageView.SetModpack(_modpackChangelog
            .OrderByDescending(entry => entry.Date)
            .Take(MaxNewsItems)
            .Select(entry => new ActusEntry(FormatNewsDate(entry.Date), string.Join("\n", entry.Lines)))
            .ToList());

        UpdateNewsTile();
        UpdateModpackTile();
        RefreshHeroMeta();
    }

    /// <summary>
    /// Notes de version du launcher (releases GitHub, mêmes règles de nettoyage que la landing
    /// page, voir ReleaseChangelogService). Best-effort : une requête échouée laisse le contenu
    /// précédent plutôt que de vider la tuile.
    /// </summary>
    private async Task ShowChangelogAsync()
    {
        var releases = await _changelogService.GetRecentReleasesAsync(count: 4);
        if (releases.Count == 0)
        {
            return;
        }

        _releases = releases;
        ActusPageView.SetReleases(releases
            .Select(release => new ReleaseEntry(
                release.Tag,
                release.Url,
                release.PublishedAt.ToLocalTime().ToString("d MMMM yyyy", FrenchCulture),
                release.IsLatest ? Visibility.Visible : Visibility.Collapsed,
                release.Notes))
            .ToList());
        UpdateLauncherTile();
    }

    private static string FormatNewsDate(DateTimeOffset date) => date.ToLocalTime().ToString("dd/MM · HH:mm", FrenchCulture);

    private void UpdateNewsTile()
    {
        var latest = _newsHistory.OrderByDescending(entry => entry.FetchedAt).FirstOrDefault();
        if (latest is null)
        {
            NewsTileDate.Text = "";
            NewsTileTitle.Text = "";
            NewsTileTitle.Visibility = Visibility.Collapsed;
            NewsTileBody.Text = "Aucune actualité pour le moment.";
            return;
        }

        var (title, body) = SplitNews(latest.Content);
        NewsTileDate.Text = FormatNewsDate(latest.FetchedAt);
        NewsTileTitle.Text = title;
        NewsTileTitle.Visibility = title.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NewsTileBody.Text = body;
    }

    /// <summary>La première ligne d'une actu fait office de titre, le reste du texte. Une actu d'une
    /// seule ligne n'a pas de titre : tout est le texte.</summary>
    internal static (string Title, string Body) SplitNews(string content)
    {
        var lines = content.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return lines.Length switch
        {
            0 => ("", ""),
            1 => ("", lines[0]),
            _ => (lines[0], string.Join(" ", lines.Skip(1))),
        };
    }

    private void UpdateModpackTile()
    {
        var latest = _modpackChangelog.OrderByDescending(entry => entry.Date).FirstOrDefault();
        ModpackTileTitle.Text = _modCount is { } count ? $"{count} mods · {_settings.MinecraftVersion}" : $"Modpack · {_settings.MinecraftVersion}";
        ModpackTileAge.Text = latest is null ? "" : $"MIS À JOUR {FormatRelativeTime(latest.Date).ToUpperInvariant()}";

        ModpackTileBody.Inlines.Clear();
        if (latest is null)
        {
            ModpackTileBody.Inlines.Add(new Run("Aucune mise à jour du modpack pour le moment."));
            return;
        }

        var summary = ModpackChangeSummary.From(latest.Lines);
        var first = true;
        void AddLine(string text, string names, string brushKey)
        {
            if (!first)
            {
                ModpackTileBody.Inlines.Add(new LineBreak());
            }

            first = false;
            ModpackTileBody.Inlines.Add(new Run(text) { Foreground = (Brush)FindResource(brushKey) });
            ModpackTileBody.Inlines.Add(new Run(" · " + names));
        }

        if (summary.Added.Count > 0)
        {
            AddLine($"+ {summary.Added.Count} ajouté{(summary.Added.Count > 1 ? "s" : "")}", string.Join(", ", summary.Added), "CyanBrush");
        }

        if (summary.Removed.Count > 0)
        {
            AddLine($"− {summary.Removed.Count} retiré{(summary.Removed.Count > 1 ? "s" : "")}", string.Join(", ", summary.Removed), "MagentaBrush");
        }

        if (summary.Updated.Count > 0)
        {
            AddLine($"↻ {summary.Updated.Count} mis à jour", string.Join(", ", summary.Updated), "InkDimBrush");
        }

        if (first)
        {
            ModpackTileBody.Inlines.Add(new Run(string.Join(" ", latest.Lines)));
        }
    }

    private void UpdateLauncherTile()
    {
        var currentTag = "v" + AppVersion.Display;
        var release = _releases.FirstOrDefault(r => string.Equals(r.Tag, currentTag, StringComparison.OrdinalIgnoreCase))
            ?? _releases.FirstOrDefault();
        if (release is null)
        {
            return;
        }

        _tileReleaseUrl = release.Url;
        var isCurrentLatest = release.IsLatest && string.Equals(release.Tag, currentTag, StringComparison.OrdinalIgnoreCase);
        LauncherTilePill.Set(
            isCurrentLatest ? StatusKind.Online : StatusKind.Neutral,
            isCurrentLatest ? $"{currentTag} · DERNIÈRE" : currentTag);
        LauncherTileBody.Text = release.Notes.Count == 0
            ? "Pas de notes pour cette version."
            : string.Join("\n", release.Notes.Select(note => "— " + note));
    }

    /// <summary>Ligne sous JOUER : « MODPACK À JOUR · JAVA 17 PRÊT » et ses variantes honnêtes.</summary>
    private void RefreshHeroMeta()
    {
        var lastSynced = _modSyncService.GetLastSyncedAt(_settings.GameDirectory);
        var latestChange = _modpackChangelog.Count == 0 ? (DateTimeOffset?)null : _modpackChangelog.Max(entry => entry.Date);
        var modpack = lastSynced is null ? "MODPACK À INSTALLER"
            : latestChange is { } changed && changed > lastSynced ? "MODPACK À METTRE À JOUR"
            : "MODPACK À JOUR";
        var java = _javaReady switch
        {
            true => "JAVA 17 PRÊT",
            false => "JAVA 17 À INSTALLER",
            null => "",
        };
        HeroReadyMeta.Text = java.Length == 0 ? modpack : $"{modpack} · {java}";
    }

    private async Task CheckJavaReadyAsync()
    {
        _javaReady = await Task.Run(_javaManager.IsJavaAvailable);
        RefreshHeroMeta();
    }
}
