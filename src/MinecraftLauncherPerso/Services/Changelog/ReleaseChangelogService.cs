using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using MinecraftLauncherPerso.Services.Diagnostics;
using MinecraftLauncherPerso.Services.GitHub;

namespace MinecraftLauncherPerso.Services.Changelog;

/// <summary>
/// Même source et même règles de nettoyage des notes que la carte "Changelog" de la landing page
/// (Omneria-landing/index.html) : GitHub Releases (pas de fichier changelog.txt séparé à
/// maintenir), notes = lignes "- ..."/"* ..." du corps de la release, liens Markdown/gras/backticks/
/// "by @user in url"/URLs nettoyés, 4 notes max par release. Garder les deux implémentations
/// alignées si l'une des deux change de règle de nettoyage.
/// </summary>
public sealed partial class ReleaseChangelogService : IReleaseChangelogService
{
    /// <summary>Affiché à la place des notes quand le corps de la release ne contient aucune ligne
    /// "- ..." exploitable (release publiée avant RELEASE_NOTES.md, ou corps jamais rempli).</summary>
    public const string MissingNotesPlaceholder = "Notes non renseignées pour cette version (voir la release sur GitHub).";

    // Flux Atom public des releases (github.com, pas l'API) : sert de secours quand l'API refuse
    // (limite de 60 requêtes/heure et par IP des appels non authentifiés, adresse partagée) ou ne
    // répond pas. Il contient les mêmes notes, sans clé ni quota.
    private const string ReleasesFeedUrl = "https://github.com/Omneria/ALlauncher/releases.atom";
    private static readonly TimeSpan FeedTimeout = TimeSpan.FromSeconds(10);

    private readonly GitHubReleasesClient _releasesClient;
    private readonly HttpClient? _feedClient;

    /// <param name="releasesClient">Liste des releases, partagée avec la vérification de mise à
    /// jour (un seul appel à l'API GitHub pour les deux, voir GitHubReleasesClient).</param>
    /// <param name="feedClient">Client HTTP du flux de secours ; sans lui, pas de secours (tests).</param>
    public ReleaseChangelogService(GitHubReleasesClient releasesClient, HttpClient? feedClient = null)
    {
        _releasesClient = releasesClient;
        _feedClient = feedClient;
    }

    public async Task<IReadOnlyList<ReleaseChangelogEntry>> GetRecentReleasesAsync(int count, CancellationToken cancellationToken = default)
    {
        var wanted = Math.Max(1, count);

        try
        {
            var releases = await _releasesClient.GetReleasesAsync(cancellationToken);
            if (releases is { } list)
            {
                var entries = ParseReleases(list, wanted);
                if (entries.Count > 0)
                {
                    return entries;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best-effort comme GitHubUpdateService.CheckForUpdateAsync : ne doit jamais empêcher
            // d'afficher le dashboard, quelle que soit la cause (réseau, JSON malformé, etc.).
            Logger.Warn("ReleaseChangelogService", $"Récupération du changelog échouée : {ex.Message}");
        }

        return await GetFromFeedAsync(wanted, cancellationToken);
    }

    private async Task<IReadOnlyList<ReleaseChangelogEntry>> GetFromFeedAsync(int count, CancellationToken cancellationToken)
    {
        if (_feedClient is null)
        {
            return [];
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(FeedTimeout);
            var xml = await _feedClient.GetStringAsync(ReleasesFeedUrl, timeout.Token);

            var entries = ParseFeed(xml, count);
            if (entries.Count > 0)
            {
                Logger.Info("ReleaseChangelogService", "Notes de version lues dans le flux GitHub : l'API des releases n'a rien renvoyé.");
            }

            return entries;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warn("ReleaseChangelogService", $"Flux des releases indisponible : {ex.Message}");
            return [];
        }
    }

    internal static List<ReleaseChangelogEntry> ParseReleases(JsonElement root, int count)
    {
        var entries = new List<ReleaseChangelogEntry>();
        var isFirst = true;

        foreach (var release in root.EnumerateArray())
        {
            if (release.GetBoolOrDefault("draft") || release.GetBoolOrDefault("prerelease"))
            {
                continue;
            }

            var tag = release.GetStringOrNull("tag_name");
            var htmlUrl = release.GetStringOrNull("html_url");
            var publishedAtRaw = release.GetStringOrNull("published_at");
            if (tag is null || htmlUrl is null || publishedAtRaw is null
                || !DateTimeOffset.TryParse(publishedAtRaw, out var publishedAt))
            {
                continue;
            }

            var body = release.GetStringOrNull("body");
            var notes = CleanNotes(body);

            entries.Add(new ReleaseChangelogEntry(
                tag,
                htmlUrl,
                publishedAt,
                IsLatest: isFirst,
                Notes: notes.Count > 0 ? notes : [MissingNotesPlaceholder]));
            isFirst = false;

            if (entries.Count >= count)
            {
                break;
            }
        }

        return entries;
    }

    /// <summary>
    /// Flux Atom des releases (les plus récentes d'abord) : le titre est le tag, "content" le corps
    /// de la release rendu en HTML (une liste &lt;ul&gt;&lt;li&gt; pour des notes en "- ..."). Le flux
    /// ne contient ni les brouillons ni l'indicateur de pré-release, et "updated" tient lieu de date.
    /// </summary>
    internal static List<ReleaseChangelogEntry> ParseFeed(string xml, int count)
    {
        var atom = XNamespace.Get("http://www.w3.org/2005/Atom");
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        var document = XDocument.Load(reader);

        var entries = new List<ReleaseChangelogEntry>();
        foreach (var entry in document.Descendants(atom + "entry"))
        {
            var tag = entry.Element(atom + "title")?.Value.Trim();
            var url = entry.Elements(atom + "link")
                .FirstOrDefault(link => (string?)link.Attribute("rel") == "alternate")?.Attribute("href")?.Value;
            var updatedRaw = entry.Element(atom + "updated")?.Value;
            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(url)
                || !DateTimeOffset.TryParse(updatedRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var publishedAt))
            {
                continue;
            }

            var notes = CleanNotes(FeedContentToBullets(entry.Element(atom + "content")?.Value));
            entries.Add(new ReleaseChangelogEntry(
                tag,
                url,
                publishedAt,
                IsLatest: entries.Count == 0,
                Notes: notes.Count > 0 ? notes : [MissingNotesPlaceholder]));

            if (entries.Count >= count)
            {
                break;
            }
        }

        return entries;
    }

    /// <summary>Corps HTML d'une entrée du flux -> lignes "- ..." (une par &lt;li&gt;), le format que
    /// CleanNotes sait lire : balises retirées, entités décodées, retours à la ligne aplatis.</summary>
    internal static string FeedContentToBullets(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }

        var lines = ListItemPattern().Matches(html)
            .Select(match => WhitespacePattern().Replace(WebUtility.HtmlDecode(HtmlTagPattern().Replace(match.Groups[1].Value, "")), " ").Trim())
            .Where(text => text.Length > 0)
            .Select(text => $"- {text}");
        return string.Join('\n', lines);
    }

    // internal (voir InternalsVisibleTo dans AssemblyInfo.cs) : testé directement par
    // MinecraftLauncherPerso.Tests, sans avoir besoin de mocker une réponse HTTP complète pour
    // valider les règles de nettoyage (alignées avec la landing page, voir doc de la classe).
    internal static List<string> CleanNotes(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return [];
        }

        return body.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => BulletLinePattern().IsMatch(line))
            .Select(line => BulletLinePattern().Replace(line, ""))
            .Select(line => MarkdownLinkPattern().Replace(line, "$1"))
            .Select(line => BoldOrBacktickPattern().Replace(line, ""))
            .Select(line => AuthorMentionPattern().Replace(line, ""))
            .Select(line => UrlPattern().Replace(line, "").Trim())
            .Where(line => line.Length > 0)
            .Take(4)
            .ToList();
    }

    [GeneratedRegex(@"<li[^>]*>(.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"^[-*] ")]
    private static partial Regex BulletLinePattern();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex MarkdownLinkPattern();

    [GeneratedRegex(@"\*\*|`")]
    private static partial Regex BoldOrBacktickPattern();

    [GeneratedRegex(@" by @\S+ in https?://\S+")]
    private static partial Regex AuthorMentionPattern();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex UrlPattern();
}
