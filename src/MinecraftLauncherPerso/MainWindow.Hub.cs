using System.Globalization;
using System.Windows;
using MinecraftLauncherPerso.Controls;
using MinecraftLauncherPerso.Services.Changelog;
using MinecraftLauncherPerso.Services.ModSync;
using MinecraftLauncherPerso.Services.News;
using MinecraftLauncherPerso.Services.Update;
using MinecraftLauncherPerso.ViewModels;

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

        _vm.Tiles.UpdateNews(_newsHistory);
        _vm.Tiles.UpdateModpack(_modpackChangelog, _modCount, _settings.MinecraftVersion, DateTimeOffset.Now);
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
        _vm.Tiles.UpdateLauncher(releases, AppVersion.Display);
    }

    private static string FormatNewsDate(DateTimeOffset date) => TilesViewModel.FormatDate(date);

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
        Launch.ReadyMeta = java.Length == 0 ? modpack : $"{modpack} · {java}";
    }

    private async Task CheckJavaReadyAsync()
    {
        _javaReady = await Task.Run(_javaManager.IsJavaAvailable);
        RefreshHeroMeta();
    }
}
