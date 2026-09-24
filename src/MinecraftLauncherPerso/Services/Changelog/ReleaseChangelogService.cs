using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using MinecraftLauncherPerso.Services.Diagnostics;

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
    private const string ReleasesApiUrlTemplate = "https://api.github.com/repos/Omneria/ALlauncher/releases?per_page={0}";

    private readonly HttpClient _httpClient;

    public ReleaseChangelogService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();

        // Même raison que GitHubUpdateService : l'API GitHub rejette (403) toute requête sans User-Agent.
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ALLauncher-UpdateChecker");
        }
    }

    public async Task<IReadOnlyList<ReleaseChangelogEntry>> GetRecentReleasesAsync(int count, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = string.Format(ReleasesApiUrlTemplate, Math.Max(1, count));
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var entries = new List<ReleaseChangelogEntry>();
            var isFirst = true;

            foreach (var release in doc.RootElement.EnumerateArray())
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
                    Notes: notes.Count > 0 ? notes : ["Voir les notes de version sur GitHub."]));
                isFirst = false;

                if (entries.Count >= count)
                {
                    break;
                }
            }

            return entries;
        }
        catch (Exception ex)
        {
            // Best-effort comme GitHubUpdateService.CheckForUpdateAsync : ne doit jamais empêcher
            // d'afficher le dashboard, quelle que soit la cause (réseau, JSON malformé, etc.).
            Logger.Warn("ReleaseChangelogService", $"Récupération du changelog échouée : {ex.Message}");
            return [];
        }
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

file static class JsonElementExtensions
{
    public static string? GetStringOrNull(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static bool GetBoolOrDefault(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
        && value.GetBoolean();
}
