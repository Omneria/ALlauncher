using System.Net;
using System.Net.Http;
using System.Text.Json;
using MinecraftLauncherPerso.Services.Diagnostics;

namespace MinecraftLauncherPerso.Services.GitHub;

/// <summary>
/// Liste des releases GitHub du launcher, partagée par la vérification de mise à jour
/// (GitHubUpdateService) et la carte Changelog (ReleaseChangelogService) depuis la v1.12.0 :
/// auparavant chacun faisait son propre appel (releases/latest et releases?per_page=4), soit le
/// double des requêtes pour la même information.
///
/// L'API GitHub non authentifiée est limitée à 60 requêtes par heure et par IP, et les deux
/// services sont rafraîchis toutes les minutes. D'où deux protections :
/// - requête conditionnelle (If-None-Match) : une réponse 304 ne compte pas dans le quota ;
/// - réponse gardée <see cref="FreshFor"/> : deux appels rapprochés (démarrage, où les deux
///   services partent en même temps) n'en font qu'un, grâce au verrou.
/// </summary>
public sealed class GitHubReleasesClient
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/Omneria/ALlauncher/releases?per_page=10";
    private static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _etag;
    private JsonElement? _releases;
    private DateTimeOffset _checkedAt = DateTimeOffset.MinValue;

    /// <param name="now">Horloge, injectable pour les tests.</param>
    public GitHubReleasesClient(HttpClient httpClient, Func<DateTimeOffset>? now = null)
    {
        _httpClient = httpClient;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Tableau JSON des releases (les plus récentes d'abord, brouillons et pré-releases compris :
    /// à filtrer par l'appelant), ou la dernière réponse connue si GitHub refuse la requête
    /// (rate-limit, panne). Null si aucune réponse n'a encore jamais été obtenue. Lève sur une
    /// erreur réseau : les appelants gèrent déjà ce cas (best-effort).
    /// </summary>
    public async Task<JsonElement?> GetReleasesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_releases is not null && _now() - _checkedAt < FreshFor)
            {
                return _releases;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            if (_etag is not null)
            {
                request.Headers.IfNoneMatch.ParseAdd(_etag);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _checkedAt = _now();
                return _releases;
            }

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warn("GitHubReleasesClient", $"Liste des releases refusée : HTTP {(int)response.StatusCode} {response.StatusCode}.");
                return _releases;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                Logger.Warn("GitHubReleasesClient", "Réponse inattendue de GitHub (pas une liste de releases).");
                return _releases;
            }

            _releases = doc.RootElement.Clone();
            _etag = response.Headers.ETag?.ToString();
            _checkedAt = _now();
            return _releases;
        }
        finally
        {
            _gate.Release();
        }
    }
}
